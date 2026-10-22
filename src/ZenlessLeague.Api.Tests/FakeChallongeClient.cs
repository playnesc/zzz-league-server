using ZenlessLeague.Api.Features.Tournaments.Challonge;

namespace ZenlessLeague.Api.Tests;

// Hand-rolled test double — records the last reported match result and serves canned
// GetMatchesAsync data, so MatchService's sync/finalize logic can be tested without a real
// Challonge account.
public class FakeChallongeClient : IChallongeClient
{
	public List<ChallongeMatchResult> Matches { get; set; } = [];

	public (string ChallongeTournamentId, string MatchId, string P1ParticipantId, string P2ParticipantId, bool P1Win, string LoserScore)? LastUpdateMatchResult { get; private set; }

	public List<string> CreatedTournamentIds { get; } = [];
	public List<string> DeletedTournamentIds { get; } = [];

	// Lets tests make bracket creation fail after Challonge already created the tournament.
	public bool FailBulkAdd { get; set; }

	public Task<ChallongeCreatedTournament> CreateTournamentAsync(string name, string tournamentType, string? description, DateTimeOffset startsAt, int? consolationMatchesTargetRank, CancellationToken ct = default)
	{
		var id = $"ct-{CreatedTournamentIds.Count + 1}";
		CreatedTournamentIds.Add(id);
		return Task.FromResult(new ChallongeCreatedTournament(id, $"https://challonge.com/{id}"));
	}

	public Task BulkAddParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default) =>
		FailBulkAdd ? throw new ChallongeApiException(500, "simulated bulk_add failure") : Task.CompletedTask;

	// Echoes the participants back in order with ids c1, c2, ... and the Uid as "misc", like Challonge does.
	public Task<List<ChallongeParticipantResult>> RandomizeParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default) =>
		Task.FromResult(participants.Select((p, i) => new ChallongeParticipantResult($"c{i + 1}", p.Uid, null)).ToList());

	public Task<List<ChallongeMatchResult>> GetMatchesAsync(string challongeTournamentId, CancellationToken ct = default) =>
		Task.FromResult(Matches);

	// Final standings served to FinishAsync (ChallongeParticipantId/Misc/FinalRank).
	public List<ChallongeParticipantResult> Participants { get; set; } = [];

	public Task<List<ChallongeParticipantResult>> GetAllParticipantsAsync(string challongeTournamentId, CancellationToken ct = default) =>
		Task.FromResult(Participants);

	// Lets tests simulate N transient failures before StartAsync finally succeeds — decremented
	// on every call, throwing while positive.
	public int StartFailuresRemaining { get; set; }
	public int StartAttempts { get; private set; }

	public Task<bool> StartAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		StartAttempts++;
		if (StartFailuresRemaining > 0)
		{
			StartFailuresRemaining--;
			throw new ChallongeApiException(503, "simulated transient failure");
		}

		return Task.FromResult(true);
	}

	public int FinalizeAttempts { get; private set; }

	public Task<bool> FinalizeAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		FinalizeAttempts++;
		return Task.FromResult(true);
	}

	public int ResetFailuresRemaining { get; set; }
	public int ResetAttempts { get; private set; }

	public Task<bool> ResetAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		ResetAttempts++;
		if (ResetFailuresRemaining > 0)
		{
			ResetFailuresRemaining--;
			throw new ChallongeApiException(503, "simulated transient failure");
		}

		return Task.FromResult(true);
	}

	public Task UpdateMatchResultAsync(string challongeTournamentId, string matchId, string p1ParticipantId, string p2ParticipantId, bool p1Win, string loserScore, CancellationToken ct = default)
	{
		LastUpdateMatchResult = (challongeTournamentId, matchId, p1ParticipantId, p2ParticipantId, p1Win, loserScore);
		return Task.CompletedTask;
	}

	public Task DeleteTournamentAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		DeletedTournamentIds.Add(challongeTournamentId);
		return Task.CompletedTask;
	}
}
