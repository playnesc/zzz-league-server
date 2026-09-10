using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;

namespace ZenlessLeague.Api.Tests;

public class MatchServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	private static async Task<(Tournament Tournament, TournamentMatch Match, Player P1, Player P2)> SeedAsync(AppDbContext context, string matchState = "open")
	{
		var p1 = TestPlayers.Create(context, "p1");
		var p2 = TestPlayers.Create(context, "p2");

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

		context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c1", PlayerId = p1.Id });
		context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c2", PlayerId = p2.Id });

		var match = new TournamentMatch
		{
			Id = Guid.NewGuid(),
			TournamentId = tournament.Id,
			ChallongeMatchId = 555,
			P1PlayerId = p1.Id,
			P2PlayerId = p2.Id,
			State = matchState,
		};
		context.TournamentMatches.Add(match);

		await context.SaveChangesAsync();
		return (tournament, match, p1, p2);
	}

	private static async Task<(Tournament Tournament, TournamentMatch Match, Team Team1, Team Team2)> SeedTeamAsync(AppDbContext context, string matchState = "open")
	{
		var p1a = TestPlayers.Create(context, "p1a");
		var p1b = TestPlayers.Create(context, "p1b");
		var p2a = TestPlayers.Create(context, "p2a");
		var p2b = TestPlayers.Create(context, "p2b");

		var team1 = new Team { Id = Guid.NewGuid(), Name = "Team One", Player1Id = p1a.Id, Player2Id = p1b.Id, CreatedAt = DateTimeOffset.UtcNow };
		var team2 = new Team { Id = Guid.NewGuid(), Name = "Team Two", Player1Id = p2a.Id, Player2Id = p2b.Id, CreatedAt = DateTimeOffset.UtcNow };
		context.Teams.Add(team1);
		context.Teams.Add(team2);

		var tournament = new Tournament
		{
			Id = Guid.NewGuid(),
			Name = "Test 2v2 Cup",
			RegistrationType = TournamentRegistrationType.Team,
			State = TournamentState.Started,
			ChallongeTournamentId = "ctid",
			BracketType = "single elimination",
			OverrideEloChange = -1,
		};
		context.Tournaments.Add(tournament);

		context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c1", TeamId = team1.Id });
		context.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c2", TeamId = team2.Id });

		var match = new TournamentMatch
		{
			Id = Guid.NewGuid(),
			TournamentId = tournament.Id,
			ChallongeMatchId = 555,
			P1TeamId = team1.Id,
			P2TeamId = team2.Id,
			State = matchState,
		};
		context.TournamentMatches.Add(match);

		await context.SaveChangesAsync();
		return (tournament, match, team1, team2);
	}

	private static (IMatchService Service, FakeChallongeClient Challonge, FakeTournamentNotifier Notifier) CreateService(AppDbContext context)
	{
		var challonge = new FakeChallongeClient();
		var playerRepository = new PlayerRepository(context);
		var historyService = new HistoryService(playerRepository, new HistoryRepository(context), new TeamRepository(context), TestDiscord.CreateRoleAssignmentService(context));
		var notifier = new FakeTournamentNotifier();
		var service = new MatchService(
			new TournamentRepository(context),
			new TournamentChallongeParticipantRepository(context),
			new TournamentMatchRepository(context),
			new TeamRepository(context),
			playerRepository,
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
		var (tournament, _, p1, _) = await SeedAsync(context);
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
		Assert.Contains(matches, m => m.ChallongeMatchId == 700 && m.WinnerPlayerId == p1.Id);

		var reloadedTournament = await context.Tournaments.SingleAsync(t => t.Id == tournament.Id);
		Assert.Equal(TournamentState.AwaitingReview, reloadedTournament.State);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_BothApprove_FinalizesAndRegistersHistory()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
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
		var (tournament, match, _, _) = await SeedAsync(context);
		var (service, _, _) = CreateService(context);

		await Assert.ThrowsAsync<MatchForbiddenException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "someone-else", new SubmitPlayerResultRequest("1:00", "1:30")));
	}

	[Fact]
	public async Task SetAdminResultAsync_TechLoss_WinnerIsOpponentOfTechLossPlayer()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedAsync(context);
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
		var (tournament, match, _, _) = await SeedAsync(context, matchState: "complete");
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

	[Fact]
	public async Task SyncFromChallongeAsync_TeamTournament_ResolvesTeamIdsNotPlayerIds()
	{
		await using var context = CreateContext();
		var (tournament, _, team1, team2) = await SeedTeamAsync(context);
		var (service, challonge, _) = CreateService(context);

		challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2")];

		await service.SyncFromChallongeAsync(tournament.Id);

		var match = await context.TournamentMatches.SingleAsync(m => m.TournamentId == tournament.Id);
		Assert.Equal(team1.Id, match.P1TeamId);
		Assert.Equal(team2.Id, match.P2TeamId);
		Assert.Equal(team1.Id, match.WinnerTeamId);
		Assert.Null(match.P1PlayerId);
		Assert.Null(match.WinnerPlayerId);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_TeamMember_CanApproveOnBehalfOfTeam()
	{
		await using var context = CreateContext();
		var (tournament, match, team1, team2) = await SeedTeamAsync(context);
		var (service, challonge, _) = CreateService(context);
		challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2")];

		// p1b (team1's second player, not the creator) approves on behalf of team1.
		var afterTeam1 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p1b", new SubmitPlayerResultRequest("1:00", "1:30"));
		Assert.True(afterTeam1!.P1ApprovedResult);
		Assert.False(afterTeam1.P2ApprovedResult);

		// p2a (team2's creator) approves — triggers finalize.
		var afterTeam2 = await service.SubmitPlayerResultAsync(tournament.Id, match.Id, "p2a", new SubmitPlayerResultRequest("1:00", "1:30"));
		Assert.Equal("complete", afterTeam2!.State);
		Assert.Equal(team1.Id, afterTeam2.WinnerTeamId);
		Assert.Null(afterTeam2.WinnerPlayerId);

		// All four players get the same team-derived change, and both go to the winning/losing side uniformly.
		var p1a = await context.Players.SingleAsync(p => p.Uid == "p1a");
		var p1b = await context.Players.SingleAsync(p => p.Uid == "p1b");
		var p2a = await context.Players.SingleAsync(p => p.Uid == "p2a");
		var p2b = await context.Players.SingleAsync(p => p.Uid == "p2b");
		Assert.Equal(p1a.TournamentPoints, p1b.TournamentPoints);
		Assert.Equal(p2a.TournamentPoints, p2b.TournamentPoints);
		Assert.True(p1a.TournamentPoints > 0);
		Assert.True(p2a.TournamentPoints < 0);
		Assert.Equal(1, p1a.Wins);
		Assert.Equal(1, p2a.Losses);

		// Two "lane" history entries (creator-vs-creator, player2-vs-player2), both referenced by the match.
		var history = await context.HistoryEntries.Where(h => h.TournamentMatchId == match.Id).ToListAsync();
		Assert.Equal(2, history.Count);

		var reloadedMatch = await context.TournamentMatches.SingleAsync(m => m.Id == match.Id);
		Assert.NotNull(reloadedMatch.HistoryEntryId);
		Assert.NotNull(reloadedMatch.HistoryEntryId2);
		Assert.NotEqual(reloadedMatch.HistoryEntryId, reloadedMatch.HistoryEntryId2);
	}

	[Fact]
	public async Task SubmitPlayerResultAsync_NonTeamMember_ThrowsForbidden()
	{
		await using var context = CreateContext();
		var (tournament, match, _, _) = await SeedTeamAsync(context);
		var (service, _, _) = CreateService(context);

		await Assert.ThrowsAsync<MatchForbiddenException>(() =>
			service.SubmitPlayerResultAsync(tournament.Id, match.Id, "someone-else", new SubmitPlayerResultRequest("1:00", "1:30")));
	}

	[Fact]
	public async Task SetAdminResultAsync_TeamTechLoss_WinnerIsOpponentTeam()
	{
		await using var context = CreateContext();
		var (tournament, match, team1, team2) = await SeedTeamAsync(context);
		var (service, challonge, _) = CreateService(context);
		challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2")];

		var result = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest(TechLossTeamId: team2.Id));

		Assert.NotNull(result);
		Assert.Equal(team1.Id, result!.WinnerTeamId);
		Assert.Equal(team2.Id, result.TechLossTeamId);
		Assert.Null(result.TechLossPlayerId);
		Assert.Equal("complete", result.State);

		var p1a = await context.Players.SingleAsync(p => p.Uid == "p1a");
		var p2a = await context.Players.SingleAsync(p => p.Uid == "p2a");
		Assert.Equal(0, p1a.TournamentPoints); // winning team gets 0 change on a tech loss
		Assert.True(p2a.TournamentPoints < 0);
	}

	[Fact]
	public async Task SetAdminResultAsync_TeamOverride_ReversesBothOldHistoryEntriesBeforeRegisteringNew()
	{
		await using var context = CreateContext();
		var (tournament, match, team1, team2) = await SeedTeamAsync(context, matchState: "complete");
		var (service, challonge, _) = CreateService(context);
		challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c1", "c1", "c2")];

		var first = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("1:00", "1:30"));
		Assert.Equal(team1.Id, first!.WinnerTeamId);
		Assert.Equal(2, await context.HistoryEntries.CountAsync());

		// Admin corrects it: team2 actually won.
		challonge.Matches = [new ChallongeMatchResult("555", 1, 1, "complete", "c2", "c1", "c2")];
		var corrected = await service.SetAdminResultAsync(tournament.Id, match.Id, new SetAdminResultRequest("2:00", "1:30"));
		Assert.Equal(team2.Id, corrected!.WinnerTeamId);

		var p1a = await context.Players.SingleAsync(p => p.Uid == "p1a");
		var p2a = await context.Players.SingleAsync(p => p.Uid == "p2a");
		Assert.Equal(0, p1a.Wins);
		Assert.Equal(1, p1a.Losses);
		Assert.Equal(1, p2a.Wins);

		// The two reversed entries from the first result were deleted; exactly the two new ones remain.
		Assert.Equal(2, await context.HistoryEntries.CountAsync());
	}
}
