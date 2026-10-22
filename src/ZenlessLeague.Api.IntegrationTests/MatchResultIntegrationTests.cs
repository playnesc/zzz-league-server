using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Tests;

namespace ZenlessLeague.Api.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class MatchResultIntegrationTests(PostgresFixture postgres)
{
	[Fact]
	public async Task TwoSimultaneousFinalConfirmations_RegisterTheResultExactlyOnce()
	{
		var database = await postgres.CreateDatabaseAsync();
		var (tournamentId, matchId) = await SeedMatchAwaitingP1ConfirmationAsync(database);

		// Both requests (e.g. a double-click) must load the match before either saves — the gate
		// holds each inside its Challonge call until both have arrived, which is the real-world
		// window (a Challonge round-trip) during which they'd overlap.
		var gate = new Rendezvous(parties: 2);
		var outcomes = await Task.WhenAll(
			Task.Run(() => SubmitAsync(database, tournamentId, matchId, gate)),
			Task.Run(() => SubmitAsync(database, tournamentId, matchId, gate)));

		Assert.Single(outcomes, o => o is null);
		Assert.Single(outcomes, o => o is DbUpdateConcurrencyException);

		await using var db = postgres.CreateContext(database);
		Assert.Single(await db.HistoryEntries.ToListAsync());
		var p1 = await db.Players.SingleAsync(p => p.Uid == "p1");
		Assert.Equal(1, p1.Wins);
	}

	[Fact]
	public async Task FailureWhileRecordingTheResult_LeavesTheMatchUnfinalizedAndRetryable()
	{
		var database = await postgres.CreateDatabaseAsync();
		var (tournamentId, matchId) = await SeedMatchAwaitingP1ConfirmationAsync(database);

		// Challonge accepts the score, then the Elo/history step fails.
		var failure = await SubmitAsync(database, tournamentId, matchId, new Rendezvous(parties: 1), failRegistration: true);
		Assert.IsType<SimulatedFailureException>(failure);

		await using (var db = postgres.CreateContext(database))
		{
			var match = await db.TournamentMatches.SingleAsync(m => m.Id == matchId);
			Assert.Equal("open", match.State);       // not left "complete" without Elo applied
			Assert.False(match.P1ApprovedResult);     // P1's confirmation rolled back too
			Assert.True(match.P2ApprovedResult);
			Assert.Empty(await db.HistoryEntries.ToListAsync());
			Assert.Equal(TournamentState.Started, (await db.Tournaments.SingleAsync()).State);
		}

		// Re-reporting the same score to Challonge is harmless, so P1 just confirms again.
		Assert.Null(await SubmitAsync(database, tournamentId, matchId, new Rendezvous(parties: 1)));

		await using var after = postgres.CreateContext(database);
		Assert.Equal("complete", (await after.TournamentMatches.SingleAsync(m => m.Id == matchId)).State);
		Assert.Single(await after.HistoryEntries.ToListAsync());
		Assert.Equal(1, (await after.Players.SingleAsync(p => p.Uid == "p1")).Wins);
	}

	[Fact]
	public async Task FailedAdminCorrection_KeepsThePreviousResultIntact()
	{
		var database = await postgres.CreateDatabaseAsync();
		var (tournamentId, matchId) = await SeedMatchAwaitingP1ConfirmationAsync(database);
		Assert.Null(await SubmitAsync(database, tournamentId, matchId, new Rendezvous(parties: 1))); // P1 wins

		// A moderator flips the result to a P2 win, but recording the new result fails — after the
		// old result had already been reversed within the same request.
		await using (var db = postgres.CreateContext(database))
		{
			var service = CreateMatchService(db, CompletedMatchChallonge(new Rendezvous(parties: 1), winner: "c2"), failRegistration: true);
			await Assert.ThrowsAsync<SimulatedFailureException>(() =>
				service.SetAdminResultAsync(tournamentId, matchId, new SetAdminResultRequest(ResultP1: "3:00", ResultP2: "2:00")));
		}

		await using var after = postgres.CreateContext(database);
		var entry = Assert.Single(await after.HistoryEntries.ToListAsync());
		Assert.Equal((await after.TournamentMatches.SingleAsync(m => m.Id == matchId)).HistoryEntryId, entry.Id);
		var p1 = await after.Players.SingleAsync(p => p.Uid == "p1");
		var p2 = await after.Players.SingleAsync(p => p.Uid == "p2");
		Assert.Equal((1, 0), (p1.Wins, p1.Losses)); // the original P1 win still stands
		Assert.Equal((0, 1), (p2.Wins, p2.Losses));
	}

	// Returns null on success, or the exception the request failed with.
	private async Task<Exception?> SubmitAsync(string database, Guid tournamentId, Guid matchId, Rendezvous gate, bool failRegistration = false)
	{
		await using var db = postgres.CreateContext(database);
		var service = CreateMatchService(db, CompletedMatchChallonge(gate, winner: "c1"), failRegistration);

		try
		{
			await service.SubmitPlayerResultAsync(
				tournamentId, matchId, "p1", new SubmitPlayerResultRequest(ResultP1: "1:00", ResultP2: "2:00"));
			return null;
		}
		catch (Exception ex)
		{
			return ex;
		}
	}

	// What Challonge reports back once match 555 has been decided in `winner`'s favor.
	private static GatedChallongeClient CompletedMatchChallonge(Rendezvous gate, string winner) =>
		new(gate) { Matches = [new ChallongeMatchResult("555", 1, 1, "complete", winner, "c1", "c2")] };

	// P2 has already confirmed "1:00 vs 2:00" (with a Nexus link as proof); P1's confirmation is
	// the one that finalizes the match.
	private async Task<(Guid TournamentId, Guid MatchId)> SeedMatchAwaitingP1ConfirmationAsync(string database)
	{
		await using var db = postgres.CreateContext(database);
		var p1 = TestPlayers.Create(db, "p1");
		var p2 = TestPlayers.Create(db, "p2");
		var tournament = new Tournament
		{
			Id = Guid.NewGuid(),
			Name = "Cup",
			RegistrationType = TournamentRegistrationType.Solo,
			State = TournamentState.Started,
			ChallongeTournamentId = "ctid",
			BracketType = "single elimination",
		};
		db.Tournaments.Add(tournament);
		db.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c1", PlayerId = p1.Id });
		db.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant { TournamentId = tournament.Id, ChallongeParticipantId = "c2", PlayerId = p2.Id });
		var match = new TournamentMatch
		{
			Id = Guid.NewGuid(),
			TournamentId = tournament.Id,
			ChallongeMatchId = 555,
			P1PlayerId = p1.Id,
			P2PlayerId = p2.Id,
			State = "open",
			ResultP1 = "1:00",
			ResultP2 = "2:00",
			NexusResultId = "abcdefghij12",
			P2ApprovedResult = true,
		};
		db.TournamentMatches.Add(match);
		await db.SaveChangesAsync();
		return (tournament.Id, match.Id);
	}

	private static MatchService CreateMatchService(AppDbContext db, IChallongeClient challonge, bool failRegistration = false)
	{
		var playerRepository = new PlayerRepository(db);
		IHistoryService historyService = new HistoryService(
			playerRepository, new HistoryRepository(db), new TeamRepository(db),
			TestDiscord.CreateRoleAssignmentService(db), new TournamentRepository(db));
		if (failRegistration)
		{
			historyService = new FailingRegistrationHistoryService(historyService);
		}
		return new MatchService(
			new TournamentRepository(db),
			new TournamentChallongeParticipantRepository(db),
			new TournamentMatchRepository(db),
			new TeamRepository(db),
			playerRepository,
			challonge,
			historyService,
			new FakeFirebaseStorageClient(),
			new FakeTournamentNotifier(),
			new FakeNexusClient(),
			new TournamentRegistrationRepository(db),
			NullLogger<MatchService>.Instance,
			Options.Create(new NexusOptions { ParseResults = false }),
			new EfUnitOfWork(db));
	}

	private sealed class SimulatedFailureException() : Exception("Simulated failure while registering the result.");

	// The real history service, except that registering a match result fails after doing its
	// writes — the worst point to fail, with player stats and the history row already saved.
	private sealed class FailingRegistrationHistoryService(IHistoryService inner) : IHistoryService
	{
		public async Task<HistoryEntryDto> RegisterMatchResultAsync(
			Guid p1PlayerId, Guid p2PlayerId, bool p1Win, int overrideEloChange, Guid? tournamentId = null, Guid? tournamentMatchId = null,
			TournamentMatchKind kind = TournamentMatchKind.None, string? resultP1 = null, string? resultP2 = null, bool techLoss = false, CancellationToken ct = default)
		{
			await inner.RegisterMatchResultAsync(p1PlayerId, p2PlayerId, p1Win, overrideEloChange, tournamentId, tournamentMatchId, kind, resultP1, resultP2, techLoss, ct);
			throw new SimulatedFailureException();
		}

		public Task<(HistoryEntryDto Entry1, HistoryEntryDto Entry2)> RegisterTeamMatchResultAsync(
			Guid p1TeamId, Guid p2TeamId, bool p1Win, int overrideEloChange, Guid tournamentId, Guid tournamentMatchId,
			TournamentMatchKind kind, string? resultP1 = null, string? resultP2 = null, bool techLoss = false, CancellationToken ct = default) =>
			throw new SimulatedFailureException();

		public Task<bool> ReverseEntryAsync(Guid historyEntryId, CancellationToken ct = default) => inner.ReverseEntryAsync(historyEntryId, ct);

		public Task<HistoryEntryDto> SetPlayerEloAsync(string uid, int elo, CancellationToken ct = default) => inner.SetPlayerEloAsync(uid, elo, ct);

		public Task<EloPreviewDto> PreviewEloChangeAsync(string p1Uid, string p2Uid, CancellationToken ct = default) => inner.PreviewEloChangeAsync(p1Uid, p2Uid, ct);

		public Task<List<HistoryEntryDto>> ListAsync(CancellationToken ct = default) => inner.ListAsync(ct);

		public Task<List<HistoryEntryDto>> ListByPlayerAsync(string uid, CancellationToken ct = default) => inner.ListByPlayerAsync(uid, ct);

		public Task<HistoryPageDto> ListPageAsync(int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default) =>
			inner.ListPageAsync(take, beforeTimestamp, beforeId, ct);

		public Task<HistoryPageDto> ListPageByPlayerAsync(string uid, int take, DateTimeOffset? beforeTimestamp, Guid? beforeId, CancellationToken ct = default) =>
			inner.ListPageByPlayerAsync(uid, take, beforeTimestamp, beforeId, ct);
	}

	// Lets N concurrent callers proceed only once all of them have arrived.
	private sealed class Rendezvous(int parties)
	{
		private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int arrived;

		public Task ArriveAndWaitAsync()
		{
			if (Interlocked.Increment(ref arrived) == parties)
			{
				allArrived.SetResult();
			}

			return allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
		}
	}

	private sealed class GatedChallongeClient(Rendezvous gate) : IChallongeClient
	{
		private readonly FakeChallongeClient inner = new();

		public List<ChallongeMatchResult> Matches { get => inner.Matches; init => inner.Matches = value; }

		public async Task UpdateMatchResultAsync(string challongeTournamentId, string matchId, string p1ParticipantId, string p2ParticipantId, bool p1Win, string loserScore, CancellationToken ct = default)
		{
			await gate.ArriveAndWaitAsync();
			await inner.UpdateMatchResultAsync(challongeTournamentId, matchId, p1ParticipantId, p2ParticipantId, p1Win, loserScore, ct);
		}

		public Task<List<ChallongeMatchResult>> GetMatchesAsync(string challongeTournamentId, CancellationToken ct = default) =>
			inner.GetMatchesAsync(challongeTournamentId, ct);

		public Task<ChallongeCreatedTournament> CreateTournamentAsync(string name, string tournamentType, string? description, DateTimeOffset startsAt, int? consolationMatchesTargetRank, CancellationToken ct = default) =>
			inner.CreateTournamentAsync(name, tournamentType, description, startsAt, consolationMatchesTargetRank, ct);

		public Task BulkAddParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default) =>
			inner.BulkAddParticipantsAsync(challongeTournamentId, participants, ct);

		public Task<List<ChallongeParticipantResult>> RandomizeParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default) =>
			inner.RandomizeParticipantsAsync(challongeTournamentId, participants, ct);

		public Task<List<ChallongeParticipantResult>> GetAllParticipantsAsync(string challongeTournamentId, CancellationToken ct = default) =>
			inner.GetAllParticipantsAsync(challongeTournamentId, ct);

		public Task<bool> StartAsync(string challongeTournamentId, CancellationToken ct = default) => inner.StartAsync(challongeTournamentId, ct);

		public Task<bool> FinalizeAsync(string challongeTournamentId, CancellationToken ct = default) => inner.FinalizeAsync(challongeTournamentId, ct);

		public Task<bool> ResetAsync(string challongeTournamentId, CancellationToken ct = default) => inner.ResetAsync(challongeTournamentId, ct);

		public Task DeleteTournamentAsync(string challongeTournamentId, CancellationToken ct = default) => inner.DeleteTournamentAsync(challongeTournamentId, ct);
	}
}
