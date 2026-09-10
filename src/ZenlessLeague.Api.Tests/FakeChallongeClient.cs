using ZenlessLeague.Api.Features.Tournaments.Challonge;

namespace ZenlessLeague.Api.Tests;

// Hand-rolled test double — records the last reported match result and serves canned
// GetMatchesAsync data, so MatchService's sync/finalize logic can be tested without a real
// Challonge account.
public class FakeChallongeClient : IChallongeClient
{
	public List<ChallongeMatchResult> Matches { get; set; } = [];

	public (string ChallongeTournamentId, string MatchId, string P1ParticipantId, string P2ParticipantId, bool P1Win, string LoserScore)? LastUpdateMatchResult { get; private set; }

	public Task<ChallongeCreatedTournament> CreateTournamentAsync(string name, string tournamentType, string? description, DateTimeOffset startsAt, int? consolationMatchesTargetRank, CancellationToken ct = default) =>
		throw new NotSupportedException();

	public Task BulkAddParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default) =>
		throw new NotSupportedException();

	public Task<List<ChallongeParticipantResult>> RandomizeParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default) =>
		throw new NotSupportedException();

	public Task<List<ChallongeMatchResult>> GetMatchesAsync(string challongeTournamentId, CancellationToken ct = default) =>
		Task.FromResult(Matches);

	public Task<List<ChallongeParticipantResult>> GetAllParticipantsAsync(string challongeTournamentId, CancellationToken ct = default) =>
		throw new NotSupportedException();

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

	public Task<bool> FinalizeAsync(string challongeTournamentId, CancellationToken ct = default) =>
		throw new NotSupportedException();

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

	public Task DeleteTournamentAsync(string challongeTournamentId, CancellationToken ct = default) =>
		throw new NotSupportedException();
}
