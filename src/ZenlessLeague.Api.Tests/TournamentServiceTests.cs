using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Matches;

namespace ZenlessLeague.Api.Tests;

public class TournamentServiceTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static (ITournamentService Service, FakeTournamentNotifier Notifier) CreateService(AppDbContext context)
    {
        var playerRepository = new PlayerRepository(context);
        var historyService = new HistoryService(playerRepository, new HistoryRepository(context), TestDiscord.CreateRoleAssignmentService(context));
        var matchService = new MatchService(
            new TournamentRepository(context),
            new TournamentChallongeParticipantRepository(context),
            new TournamentMatchRepository(context),
            new FakeChallongeClient(),
            historyService,
            new FakeFirebaseStorageClient(),
            new FakeTournamentNotifier());

        var notifier = new FakeTournamentNotifier();
        var service = new TournamentService(
            new TournamentRepository(context),
            new TournamentRegistrationRepository(context),
            new TeamRepository(context),
            playerRepository,
            new TournamentChallongeParticipantRepository(context),
            new FakeChallongeClient(),
            matchService,
            historyService,
            TestDiscord.CreateTournamentResourcesService(context),
            notifier);
        return (service, notifier);
    }

    private static async Task<Tournament> SeedTournamentAsync(AppDbContext context, TournamentState state = TournamentState.Registration)
    {
        var tournament = new Tournament
        {
            Id = Guid.NewGuid(),
            Name = "Cup",
            RegistrationType = TournamentRegistrationType.Solo,
            State = state,
            RegistrationEndDate = DateTimeOffset.UtcNow.AddDays(1),
        };
        context.Tournaments.Add(tournament);
        await context.SaveChangesAsync();
        return tournament;
    }

    private static async Task<TournamentRegistration> SeedApprovedRegistrationAsync(AppDbContext context, Guid tournamentId, string uid)
    {
        context.Players.Add(new Player { Uid = uid, Name = uid, CreatedAt = DateTimeOffset.UtcNow });
        var registration = new TournamentRegistration
        {
            Id = Guid.NewGuid(),
            TournamentId = tournamentId,
            Type = TournamentRegistrationType.Solo,
            PlayerId = uid,
            Player1Details = new PlayerRegistrationDetails
            {
                GameUid = "g", DarteNickname = "n", DartePresetName = "p", RosterName = "r",
            },
            Approved = true,
            RegisteredAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        context.TournamentRegistrations.Add(registration);
        await context.SaveChangesAsync();
        return registration;
    }

    [Fact]
    public async Task CloseRegistrationAsync_WhenOpen_TransitionsState()
    {
        await using var context = CreateContext();
        var tournament = await SeedTournamentAsync(context);
        var (service, notifier) = CreateService(context);

        var result = await service.CloseRegistrationAsync(tournament.Id);

        Assert.NotNull(result);
        Assert.Equal(TournamentState.RegistrationClosed, result!.State);
        Assert.Contains(tournament.Id, notifier.TournamentChanged);
    }

    [Fact]
    public async Task CloseRegistrationAsync_WhenAlreadyClosed_Throws()
    {
        await using var context = CreateContext();
        var tournament = await SeedTournamentAsync(context, TournamentState.RegistrationClosed);
        var (service, notifier) = CreateService(context);

        await Assert.ThrowsAsync<TournamentLifecycleException>(() => service.CloseRegistrationAsync(tournament.Id));
    }

    [Fact]
    public async Task IncrementTournamentCountAsync_BumpsOnlyApprovedRegistrants()
    {
        await using var context = CreateContext();
        var tournament = await SeedTournamentAsync(context);
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
        var (service, notifier) = CreateService(context);

        await service.IncrementTournamentCountAsync(tournament.Id, seasonal: true);

        var p1 = await context.Players.SingleAsync(p => p.Uid == "p1");
        Assert.Equal(1, p1.PlayedTournamentCount);
        Assert.Equal(1, p1.SeasonalPlayedTournamentCount);
    }

    [Fact]
    public async Task IncrementTournamentCountAsync_NoApprovedRegistrants_Throws()
    {
        await using var context = CreateContext();
        var tournament = await SeedTournamentAsync(context);
        var (service, notifier) = CreateService(context);

        await Assert.ThrowsAsync<TournamentLifecycleException>(() => service.IncrementTournamentCountAsync(tournament.Id, seasonal: false));
    }

    [Fact]
    public async Task SplitAsync_CreatesDivisionsAndReparentsRegistrations()
    {
        await using var context = CreateContext();
        var tournament = await SeedTournamentAsync(context);
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p3");
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p4");
        var (service, notifier) = CreateService(context);

        var result = await service.SplitAsync(tournament.Id, new SplitTournamentRequest([["p1", "p2"], ["p3", "p4"]]));

        Assert.NotNull(result);
        Assert.Equal(2, result!.DivisionTournamentIds.Count);
        Assert.Equal(tournament.Id, result.DivisionTournamentIds[0]);

        var original = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
        Assert.Equal("Cup A", original.Name);
        Assert.Equal(TournamentState.RegistrationClosed, original.State);
        Assert.Equal(tournament.Id.ToString(), original.DivisionGroupId);
        Assert.Equal(1, original.DivisionIndex);

        var divisionB = await context.Tournaments.SingleAsync(t => t.Id == result.DivisionTournamentIds[1]);
        Assert.Equal("Cup B", divisionB.Name);
        Assert.Equal(2, divisionB.DivisionIndex);
        Assert.Equal(tournament.Id.ToString(), divisionB.DivisionGroupId);

        // p1/p2 stay on the original tournament, p3/p4 move to the new division.
        var originalRegs = await context.TournamentRegistrations.Where(r => r.TournamentId == tournament.Id).ToListAsync();
        Assert.Equal(2, originalRegs.Count);
        Assert.All(originalRegs, r => Assert.Contains(r.PlayerId, new[] { "p1", "p2" }));

        var divisionBRegs = await context.TournamentRegistrations.Where(r => r.TournamentId == divisionB.Id).ToListAsync();
        Assert.Equal(2, divisionBRegs.Count);
        Assert.All(divisionBRegs, r => Assert.Contains(r.PlayerId, new[] { "p3", "p4" }));

        // Both the original tournament and the new division get a change notification.
        Assert.Contains(tournament.Id, notifier.TournamentChanged);
        Assert.Contains(divisionB.Id, notifier.TournamentChanged);
    }

    [Fact]
    public async Task SplitAsync_GroupsDontCoverEveryApprovedPlayerExactlyOnce_Throws()
    {
        await using var context = CreateContext();
        var tournament = await SeedTournamentAsync(context);
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p1");
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p2");
        await SeedApprovedRegistrationAsync(context, tournament.Id, "p3");
        var (service, notifier) = CreateService(context);

        // p2 appears twice and p3's group is short — every approved player must appear exactly once.
        await Assert.ThrowsAsync<TournamentLifecycleException>(() =>
            service.SplitAsync(tournament.Id, new SplitTournamentRequest([["p1", "p2"], ["p2", "p3"]])));
    }
}
