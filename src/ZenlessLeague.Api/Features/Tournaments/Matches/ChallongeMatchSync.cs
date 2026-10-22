using ZenlessLeague.Api.Features.Tournaments.Challonge;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

// The Challonge side of a tournament's matches, in both directions: pulling the bracket's match
// state into local TournamentMatch rows, and reporting a finalized result back. Neither notifies
// SignalR clients — callers do that once their own writes have committed (see MatchService).
public class ChallongeMatchSync(
	ITournamentRepository tournamentRepository,
	ITournamentChallongeParticipantRepository challongeParticipantRepository,
	ITournamentMatchRepository matchRepository,
	IChallongeClient challongeClient)
{
	// Pulls match state back from Challonge, including the double-elimination "reset" grand-finals
	// placeholder handling, and flips the tournament to AwaitingReview once every synced match has a
	// winner. Returns whether the tournament's own state changed.
	public async Task<bool> SyncAsync(Guid tournamentId, CancellationToken ct = default)
	{
		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct)
			?? throw new MatchValidationException($"Tournament '{tournamentId}' does not exist.");
		if (tournament.ChallongeTournamentId is null)
		{
			throw new MatchValidationException("Tournament has no Challonge bracket yet.");
		}

		var participants = await challongeParticipantRepository.ListByTournamentAsync(tournamentId, ct);
		// Exactly one of these is populated per participant, depending on Tournament.RegistrationType
		// (see TournamentChallongeParticipant) — both dictionaries are built unconditionally since
		// resolving against the "wrong" one for this tournament's type is always a harmless miss.
		var challongeToPlayerId = participants.Where(p => p.PlayerId is not null).ToDictionary(p => p.ChallongeParticipantId, p => p.PlayerId!.Value);
		var challongeToTeamId = participants.Where(p => p.TeamId is not null).ToDictionary(p => p.ChallongeParticipantId, p => p.TeamId!.Value);

		var matches = await challongeClient.GetMatchesAsync(tournament.ChallongeTournamentId, ct);
		var resetChallongeMatchId = ResolveResetChallongeMatchId(tournament.BracketType, matches);

		var allMatchesPlayed = true;

		foreach (var m in matches)
		{
			var challongeMatchId = long.Parse(m.ChallongeMatchId);

			if (challongeMatchId == resetChallongeMatchId)
			{
				var placeholder = await matchRepository.GetByChallongeMatchIdAsync(tournamentId, challongeMatchId, ct);
				if (placeholder is not null)
				{
					await matchRepository.RemoveAsync(placeholder, ct);
				}

				continue;
			}

			var hasWinner = await UpsertMatchAsync(
				tournamentId, challongeMatchId, m, challongeToPlayerId, challongeToTeamId, ct);
			if (!hasWinner)
			{
				allMatchesPlayed = false;
			}
		}

		await matchRepository.SaveChangesAsync(ct);

		// Only ever Started -> AwaitingReview. A resync of a finished tournament (refresh-games, or
		// an admin correcting a result after Finish) must not knock Complete back to AwaitingReview,
		// which would let Finish run — and award prize Elo/played counts — a second time.
		if (allMatchesPlayed && tournament.State == TournamentState.Started)
		{
			tournament.State = TournamentState.AwaitingReview;
			await tournamentRepository.SaveChangesAsync(ct);
			return true;
		}

		return false;
	}

	// The external, non-transactional half of finalizing a result. Re-reporting the same score is
	// harmless, so if recording the result locally then fails the whole thing can simply be retried.
	public async Task ReportResultAsync(Tournament tournament, TournamentMatch match, bool p1Win, string loserScore, CancellationToken ct = default)
	{
		if (tournament.ChallongeTournamentId is null)
		{
			throw new MatchValidationException("Tournament has no Challonge bracket yet.");
		}

		// Only 2 rows are actually needed here (unlike SyncAsync, which legitimately resolves every
		// match against every participant) — targeted lookups instead of loading the whole
		// tournament's participant table just to find these two.
		string p1ChallongeId, p2ChallongeId;
		if (match.P1TeamId is Guid p1TeamId && match.P2TeamId is Guid p2TeamId)
		{
			var p1Participant = await challongeParticipantRepository.FindAsync(tournament.Id, null, p1TeamId, ct);
			var p2Participant = await challongeParticipantRepository.FindAsync(tournament.Id, null, p2TeamId, ct);
			if (p1Participant is null || p2Participant is null)
			{
				throw new MatchValidationException("Match participants are not registered as Challonge participants.");
			}

			p1ChallongeId = p1Participant.ChallongeParticipantId;
			p2ChallongeId = p2Participant.ChallongeParticipantId;
		}
		else
		{
			var p1Participant = await challongeParticipantRepository.FindAsync(tournament.Id, match.P1PlayerId!.Value, null, ct);
			var p2Participant = await challongeParticipantRepository.FindAsync(tournament.Id, match.P2PlayerId!.Value, null, ct);
			if (p1Participant is null || p2Participant is null)
			{
				throw new MatchValidationException("Match participants are not registered as Challonge participants.");
			}

			p1ChallongeId = p1Participant.ChallongeParticipantId;
			p2ChallongeId = p2Participant.ChallongeParticipantId;
		}

		await challongeClient.UpdateMatchResultAsync(
			tournament.ChallongeTournamentId, match.ChallongeMatchId.ToString(), p1ChallongeId, p2ChallongeId, p1Win, loserScore, ct);
	}

	// Double-elimination brackets carry a "grand finals reset" placeholder match for the case where
	// the loser's-bracket finalist wins the first grand-finals game — Challonge always creates it,
	// but it only actually gets played if that upset happens. Until then it's a dangling match with
	// no real participants, so it's identified here and skipped/removed rather than synced as real.
	private static long? ResolveResetChallongeMatchId(string? bracketType, List<ChallongeMatchResult> matches)
	{
		if (bracketType != "double elimination" || matches.Count == 0)
		{
			return null;
		}

		var maxRound = matches.Max(m => m.Round);
		var finalRoundMatches = matches
			.Where(m => m.Round == maxRound)
			.OrderBy(m => m.SuggestedPlayOrder)
			.ToList();

		return finalRoundMatches.Count > 1 ? long.Parse(finalRoundMatches[^1].ChallongeMatchId) : null;
	}

	// Creates or updates the local TournamentMatch row for one Challonge match. Only the
	// Challonge-sourced fields are ever written here — result/approval/history fields belong to
	// MatchService's submission flows, not this sync. Returns whether the match has a winner yet
	// (used by the caller to decide whether the whole tournament is done syncing).
	private async Task<bool> UpsertMatchAsync(
		Guid tournamentId, long challongeMatchId, ChallongeMatchResult m,
		Dictionary<string, Guid> challongeToPlayerId, Dictionary<string, Guid> challongeToTeamId, CancellationToken ct)
	{
		var winnerPlayerId = ResolveId(m.WinnerChallongeParticipantId, challongeToPlayerId);
		var p1PlayerId = ResolveId(m.P1ChallongeParticipantId, challongeToPlayerId);
		var p2PlayerId = ResolveId(m.P2ChallongeParticipantId, challongeToPlayerId);
		var winnerTeamId = ResolveId(m.WinnerChallongeParticipantId, challongeToTeamId);
		var p1TeamId = ResolveId(m.P1ChallongeParticipantId, challongeToTeamId);
		var p2TeamId = ResolveId(m.P2ChallongeParticipantId, challongeToTeamId);

		var existing = await matchRepository.GetByChallongeMatchIdAsync(tournamentId, challongeMatchId, ct);
		if (existing is null)
		{
			await matchRepository.CreateAsync(new TournamentMatch
			{
				Id = Guid.NewGuid(),
				TournamentId = tournamentId,
				ChallongeMatchId = challongeMatchId,
				State = m.State,
				WinnerPlayerId = winnerPlayerId,
				WinnerTeamId = winnerTeamId,
				P1PlayerId = p1PlayerId,
				P2PlayerId = p2PlayerId,
				P1TeamId = p1TeamId,
				P2TeamId = p2TeamId,
			}, ct);
		}
		else
		{
			existing.State = m.State;
			existing.WinnerPlayerId = winnerPlayerId;
			existing.WinnerTeamId = winnerTeamId;
			existing.P1PlayerId = p1PlayerId;
			existing.P2PlayerId = p2PlayerId;
			existing.P1TeamId = p1TeamId;
			existing.P2TeamId = p2TeamId;
		}

		return winnerPlayerId is not null || winnerTeamId is not null;
	}

	private static Guid? ResolveId(string? challongeParticipantId, Dictionary<string, Guid> challongeToLocalId) =>
		challongeParticipantId is not null && challongeToLocalId.TryGetValue(challongeParticipantId, out var id) ? id : null;
}
