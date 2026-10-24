using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Bracket;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Tests;

// The bracket end to end, through the same services the endpoints use: create, rearrange, start,
// play every match via the moderator override, correct a result, finish.
public class BracketServiceTests
{
	private static AppDbContext CreateContext() =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

	// Four players with a created double-elimination bracket laid out p1, p2, p3, p4 — so the
	// matches are M1 p1-p2, M2 p3-p4, M3 losers R1, M4 winners final, M5 losers final, M6 grand final.
	private static async Task<(Tournament Tournament, ITournamentLifecycleService Lifecycle, IBracketService Bracket, IMatchService Matches)> SeedDoubleEliminationAsync(
		AppDbContext context, bool start = true)
	{
		var tournament = await TournamentServiceTests.SeedTournamentAsync(context, TournamentState.RegistrationClosed, BracketFormats.DoubleElimination);
		tournament.OverrideEloChange = -1;
		await context.SaveChangesAsync();
		foreach (var uid in new[] { "p1", "p2", "p3", "p4" })
		{
			await TournamentServiceTests.SeedApprovedRegistrationAsync(context, tournament.Id, uid);
		}

		var (_, lifecycle, _, bracket, _) = TournamentServiceTests.CreateService(context);
		var (matches, _) = MatchServiceTests.CreateService(context);
		await lifecycle.CreateBracketAsync(tournament.Id);
		await bracket.SaveLayoutAsync(tournament.Id, new SaveBracketLayoutRequest(["p1", "p2", "p3", "p4"]));
		if (start)
		{
			await lifecycle.StartAsync(tournament.Id);
		}

		return (tournament, lifecycle, bracket, matches);
	}

	private static async Task<TournamentMatch> MatchAsync(AppDbContext context, Guid tournamentId, int number) =>
		await context.TournamentMatches.SingleAsync(m => m.TournamentId == tournamentId && m.Number == number);

	// Shiyu Defense: the lower time wins.
	private static async Task<TournamentMatchDto?> PlayAsync(
		AppDbContext context, IMatchService matches, Guid tournamentId, int number, bool p1Wins, bool confirmReset = false)
	{
		var match = await MatchAsync(context, tournamentId, number);
		return await matches.SetAdminResultAsync(tournamentId, match.Id, new SetAdminResultRequest(
			ResultP1: p1Wins ? "1:00" : "2:00", ResultP2: p1Wins ? "2:00" : "1:00", ConfirmReset: confirmReset));
	}

	private static async Task<Guid> PlayerIdAsync(AppDbContext context, string uid) =>
		(await context.Players.SingleAsync(p => p.Uid == uid)).Id;

	[Fact]
	public async Task SaveLayout_RegeneratesMatchesFromTheNewSlots()
	{
		await using var context = CreateContext();
		var (tournament, _, bracket, _) = await SeedDoubleEliminationAsync(context, start: false);

		var swapped = await bracket.SaveLayoutAsync(tournament.Id, new SaveBracketLayoutRequest(["p1", "p3", "p2", "p4"]));

		Assert.Equal(["p1", "p3", "p2", "p4"], swapped!.Slots);
		Assert.Equal(6, await context.TournamentMatches.CountAsync(m => m.TournamentId == tournament.Id));
		var first = await MatchAsync(context, tournament.Id, 1);
		Assert.Equal((await PlayerIdAsync(context, "p1"), await PlayerIdAsync(context, "p3")), (first.P1PlayerId, first.P2PlayerId));
		Assert.Equal(BracketService.Open, first.State);
	}

	[Fact]
	public async Task SaveLayout_InvalidLayout_Throws()
	{
		await using var context = CreateContext();
		var (tournament, _, bracket, _) = await SeedDoubleEliminationAsync(context, start: false);

		await Assert.ThrowsAsync<BracketValidationException>(() =>
			bracket.SaveLayoutAsync(tournament.Id, new SaveBracketLayoutRequest(["p1", "p1", "p2", "p4"])));
		await Assert.ThrowsAsync<BracketValidationException>(() =>
			bracket.SaveLayoutAsync(tournament.Id, new SaveBracketLayoutRequest(["p1", "p2", "p3"])));
	}

	[Fact]
	public async Task Rearranging_AfterStart_IsRejected()
	{
		await using var context = CreateContext();
		var (tournament, _, bracket, _) = await SeedDoubleEliminationAsync(context);

		await Assert.ThrowsAsync<TournamentLifecycleException>(() =>
			bracket.SaveLayoutAsync(tournament.Id, new SaveBracketLayoutRequest(["p4", "p3", "p2", "p1"])));
	}

	[Fact]
	public async Task Results_BeforeStartOrInAPendingMatch_AreRejected()
	{
		await using var context = CreateContext();
		var (tournament, lifecycle, _, matches) = await SeedDoubleEliminationAsync(context, start: false);
		var first = await MatchAsync(context, tournament.Id, 1);

		await Assert.ThrowsAsync<MatchValidationException>(() =>
			matches.SubmitPlayerResultAsync(tournament.Id, first.Id, "p1", new SubmitPlayerResultRequest(ResultP1: "1:00", ResultP2: "2:00", NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId))));
		await Assert.ThrowsAsync<MatchValidationException>(() => PlayAsync(context, matches, tournament.Id, 1, p1Wins: true));

		await lifecycle.StartAsync(tournament.Id);
		await PlayAsync(context, matches, tournament.Id, 1, p1Wins: true);
		// p1 is through to the winners final, but their opponent (M2's winner) isn't decided yet.
		var winnersFinal = await MatchAsync(context, tournament.Id, 4);
		Assert.Equal(BracketService.Pending, winnersFinal.State);
		await Assert.ThrowsAsync<MatchValidationException>(() =>
			matches.SubmitPlayerResultAsync(tournament.Id, winnersFinal.Id, "p1", new SubmitPlayerResultRequest(ResultP1: "1:00", ResultP2: "2:00", NexusResultUrl: NexusFixtures.Link(NexusFixtures.ShiyuFinishedId))));
	}

	[Fact]
	public async Task PlayingEveryMatch_AdvancesThroughBothBrackets_ThenFinishRanksAndRewards()
	{
		await using var context = CreateContext();
		var (tournament, lifecycle, bracket, matches) = await SeedDoubleEliminationAsync(context);

		// P1 wins every match: M1 p1, M2 p3, M3 p2 (over p4), M4 p1 (over p3), M5 p2 (over p3), M6 p1.
		for (var number = 1; number <= 6; number++)
		{
			await PlayAsync(context, matches, tournament.Id, number, p1Wins: true);
		}

		var losersFinal = await MatchAsync(context, tournament.Id, 5);
		Assert.Equal((await PlayerIdAsync(context, "p2"), await PlayerIdAsync(context, "p3")), (losersFinal.P1PlayerId, losersFinal.P2PlayerId));
		Assert.Equal(TournamentState.AwaitingReview, (await context.Tournaments.SingleAsync()).State);

		var p1EloBeforeFinish = (await context.Players.SingleAsync(p => p.Uid == "p1")).Elo;
		var result = await lifecycle.FinishAsync(tournament.Id);

		Assert.Equal("p1", result!.WinnerId);
		var standings = (await bracket.GetAsync(tournament.Id))!.Standings;
		Assert.Equal([("p1", 1), ("p2", 2), ("p3", 3), ("p4", 4)], standings.Select(s => (s.PlayerId!, s.Rank)));
		Assert.Equal(p1EloBeforeFinish + 40, (await context.Players.SingleAsync(p => p.Uid == "p1")).Elo);
	}

	[Fact]
	public async Task ChangingAPlayedWinner_NeedsConfirmation_ThenResetsWhatItFed()
	{
		await using var context = CreateContext();
		var (tournament, _, _, matches) = await SeedDoubleEliminationAsync(context);
		for (var number = 1; number <= 4; number++)
		{
			await PlayAsync(context, matches, tournament.Id, number, p1Wins: true);
		}

		// M1's winner (p1) went on to win M4; its loser (p2) won M3. Flipping M1 voids both.
		var ex = await Assert.ThrowsAsync<MatchResetConfirmationException>(() => PlayAsync(context, matches, tournament.Id, 1, p1Wins: false));
		Assert.Equal(428, ex.StatusCode);
		Assert.Contains("M3", ex.Message);
		Assert.Contains("M4", ex.Message);

		await PlayAsync(context, matches, tournament.Id, 1, p1Wins: false, confirmReset: true);

		var (p1, p2, p3, p4) = (await PlayerIdAsync(context, "p1"), await PlayerIdAsync(context, "p2"), await PlayerIdAsync(context, "p3"), await PlayerIdAsync(context, "p4"));
		var m3 = await MatchAsync(context, tournament.Id, 3);
		var m4 = await MatchAsync(context, tournament.Id, 4);
		Assert.Equal((p1, p4, BracketService.Open), (m3.P1PlayerId, m3.P2PlayerId, m3.State));
		Assert.Equal((p2, p3, BracketService.Open), (m4.P1PlayerId, m4.P2PlayerId, m4.State));
		Assert.Null(m3.WinnerPlayerId);
		Assert.Null(m4.HistoryEntryId);

		// Only M1 (now p2's win) and M2 (p3's) still count.
		var players = await context.Players.ToDictionaryAsync(p => p.Uid);
		Assert.Equal((0, 1), (players["p1"].Wins, players["p1"].Losses));
		Assert.Equal((1, 0), (players["p2"].Wins, players["p2"].Losses));
		Assert.Equal((1, 0), (players["p3"].Wins, players["p3"].Losses));
		Assert.Equal((0, 1), (players["p4"].Wins, players["p4"].Losses));
	}

	[Fact]
	public async Task ChangingAWinner_WithNothingPlayedDownstream_NeedsNoConfirmation()
	{
		await using var context = CreateContext();
		var (tournament, _, _, matches) = await SeedDoubleEliminationAsync(context);
		await PlayAsync(context, matches, tournament.Id, 1, p1Wins: true);

		await PlayAsync(context, matches, tournament.Id, 1, p1Wins: false);

		var m4 = await MatchAsync(context, tournament.Id, 4);
		var m3 = await MatchAsync(context, tournament.Id, 3);
		Assert.Equal(await PlayerIdAsync(context, "p2"), m4.P1PlayerId);
		Assert.Equal(await PlayerIdAsync(context, "p1"), m3.P1PlayerId);
	}

	[Fact]
	public async Task ChangingAWinner_AfterFinish_IsRejected()
	{
		await using var context = CreateContext();
		var (tournament, lifecycle, _, matches) = await SeedDoubleEliminationAsync(context);
		for (var number = 1; number <= 6; number++)
		{
			await PlayAsync(context, matches, tournament.Id, number, p1Wins: true);
		}

		await lifecycle.FinishAsync(tournament.Id);

		await Assert.ThrowsAsync<MatchValidationException>(() => PlayAsync(context, matches, tournament.Id, 6, p1Wins: false, confirmReset: true));
		// A score-only correction (same winner) is still allowed.
		await PlayAsync(context, matches, tournament.Id, 6, p1Wins: true);
	}
}
