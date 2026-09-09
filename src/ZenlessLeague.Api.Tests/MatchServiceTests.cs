using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;

namespace ZenlessLeague.Api.Tests;

public class MatchServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Tournament Tournament, TournamentMatch Match)> SeedAsync(AppDbContext context, string matchState = "open")
    {
        context.Players.Add(new Player { Uid = "p1", Name = "p1", Elo = 1000, CreatedAt = DateTimeOffset.UtcNow });
        context.Players.Add(new Player { Uid = "p2", Name = "p2", Elo = 1000, CreatedAt = DateTimeOffset.UtcNow });

        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = "Test Cup",
            RegistrationType = TournamentRegistrationType.Solo,
            State = TournamentState.Started,
            ChallongeTournamentId = "ctid",
            BracketType = "single elimination",
            OverrideEloChange = -1,
        };
        context.Tournaments.Add(tournament);

        context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c1", PlayerId = "p1" });
        context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c2", PlayerId = "p2" });

        var match = new TournamentMatch
        {
            Id = Guid.NewGuid(),
            TournamentId = tournament.Id,
            ChallongeMatchId = 555,
            P1PlayerId = "p1",
            P2PlayerId = "p2",
            State = matchState,
        };
        context.TournamentMatches.Add(match);

        await context.SaveChangesAsync();
        return (tournament, match);
    }

    private static (IMatchService Service, FakeChallongeClient Challonge, FakeTournamentNotifier Notifier) CreateService(AppDbContext context)
    {
        var challonge = new FakeChallongeClient();
        var playerRepository = new PlayerRepository(context);
        var historyService = new HistoryService(playerRepository, new HistoryRepository(context), TestDiscord.CreateRoleAssignmentService(context));
        var notifier = new FakeTournamentNotifier();
        var service = new MatchService(
            new TournamentRepository(context),
            new TournamentChallongeParticipantRepository(context),
            new TournamentMatchRepository(context),
            challonge,
            historyService,
            new FakeFirebaseStorageClient(),
            notifier);
        return (service, challonge, notifier);
    }

    [Fact]
    public async Task SyncFromChallongeAsync_DoubleElim_RemovesResetPlaceholderAndExcludesItFromAllMatchesPlayed()
    {
        await using var context = CreateContext();
        var (tournament, _) = await SeedAsync(context);
        tournament.BracketType = "double elimination";
        await context.SaveChangesAsync();

        var (service, challonge, notifier) = CreateService(context);

        // Round 2 (max round) has two matches: the real grand final (played) and the "reset"
        // placeholder (not played) — the reset one should be dropped, not counted.
        challonge.Matches =
        [
            new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2"),
            new ChallongeMatchResult("700", 2, 1, "complete", "c1", "c1", "c2"),
            new ChallongeMatchResult("701", 2, 2, "open", null, "c2", "c1"),
        ];

        await service.SyncFromChallongeAsync(tournament.Id);

        var matches = await context.TournamentMatches.Where(m => m.TournamentId == tournament.Id).ToListAsync();
        Assert.DoesNotContain(matches, m => m.ChallongeMatchId == 701);
        Assert.Contains(matches, m => m.ChallongeMatchId == 700 && m.WinnerPlayerId == "p1");

        var reloadedTournament = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
        Assert.Equal(TournamentState.AwaitingReview, reloadedTournament.State);
    }

    [Fact]
    public async Task SubmitPlayerResultAsync_BothApprove_FinalizesAndRegistersHistory()
    {
        await using var context = CreateContext();
        var (tournament, match) = await SeedAsync(context);
        var (service, challonge, notifier) = CreateService(context);

        challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2")];

        // p1 submits first — not both approved yet, match stays open.
        var afterP1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1", new SubmitPlayerResultRequest("1:00", "1:30"));
        Assert.NotNull(afterP1);
        Assert.True(afterP1!.P1ApprovedResult);
        Assert.False(afterP1.P2ApprovedResult);
        Assert.NotEqual("complete", afterP1.State);

        // p2 approves the same result — triggers finalize.
        var afterP2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2", new SubmitPlayerResultRequest("1:00", "1:30"));
        Assert.NotNull(afterP2);
        Assert.Equal("complete", afterP2!.State);
        Assert.Equal("p1", afterP2.WinnerPlayerId); // 1:00 < 1:30

        Assert.Equal(("ctid", "555", "c1", "c2", true, "0"), challonge.LastUpdateMatchResult);

        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        Assert.Equal(1, p1.Wins);
        Assert.True(p1.TournamentPoints > 0);

        var history = await context.HistoryEntries.ToListAsync();
        Assert.Single(history);
        Assert.Equal(match.Id, history[0].TournamentMatchId);

        Assert.Contains((tournament.Id, match.Id), notifier.MatchChanged);
        Assert.Contains(tournament.Id, notifier.MatchesSynced);
    }

    [Fact]
    public async Task SubmitPlayerResultAsync_NonParticipant_ThrowsForbidden()
    {
        await using var context = CreateContext();
        var (tournament, match) = await SeedAsync(context);
        var (service, _, _) = CreateService(context);

        await Assert.ThrowsAsync<MatchForbiddenException>(() =>
            service.SubmitPlayerResultAsync(tournament.Id, match.Id, "someone-else", new SubmitPlayerResultRequest("1:00", "1:30")));
    }

    [Fact]
    public async Task SetAdminResultAsync_TechLoss_WinnerIsOpponentOfTechLossPlayer()
    {
        await using var context = CreateContext();
        var (tournament, match) = await SeedAsync(context);
        var (service, challonge, notifier) = CreateService(context);

        challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2")];

        var result = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest(TechLossPlayerId: "p2"));

        Assert.NotNull(result);
        Assert.Equal("p1", result!.WinnerPlayerId);
        Assert.Equal("p2", result.TechLossPlayerId);
        Assert.Equal("complete", result.State);
        Assert.Equal(("ctid", "555", "c1", "c2", true, "999"), challonge.LastUpdateMatchResult);

        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        var p2 = await context.Players.SingleAsync(p => p.Uid == "p2");
        Assert.Equal(0, p1.TournamentPoints); // winner gets 0 change on a tech loss
        Assert.True(p2.TournamentPoints < 0);
    }

    [Fact]
    public async Task SetAdminResultAsync_Override_ReversesOldHistoryBeforeRegisteringNew()
    {
        await using var context = CreateContext();
        var (tournament, match) = await SeedAsync(context, matchState: "complete");
        var (service, challonge, notifier) = CreateService(context);
        challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2")];

        // First result: p1 wins.
        var first = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("1:00", "1:30"));
        Assert.Equal("p1", first!.WinnerPlayerId);
        var p1AfterFirst = await context.Players.SingleAsync(p => p.Uid == "p1");
        Assert.Equal(1, p1AfterFirst.Wins);

        // Admin corrects it: p2 actually won — the old history entry should be reversed first.
        challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c2", "c1", "c2")];
        var corrected = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("2:00", "1:30"));
        Assert.Equal("p2", corrected!.WinnerPlayerId);

        var p1AfterCorrection = await context.Players.SingleAsync(p => p.Uid == "p1");
        var p2AfterCorrection = await context.Players.SingleAsync(p => p.Uid == "p2");
        Assert.Equal(0, p1AfterCorrection.Wins); // reversed back to 0, then not re-incremented (p1 lost this time)
        Assert.Equal(1, p1AfterCorrection.Losses);
        Assert.Equal(1, p2AfterCorrection.Wins);

        // Exactly one live history entry should remain (the reversed one was deleted).
        Assert.Single(await context.HistoryEntries.ToListAsync());
    }
}
