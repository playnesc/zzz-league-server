using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Storage;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Realtime;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public interface IMatchService
{
	// Pulls match state back from Challonge, including the double-elimination "reset"
	// grand-finals placeholder handling, and flips the tournament to AwaitingReview once every
	// synced match has a winner.
	Task SyncFromChallongeAsync(Guid tournamentId, CancellationToken ct = default);

	Task<TournamentMatchDto?> SubmitPlayerResultAsync(Guid tournamentId, Guid matchId, string callerPlayerId, SubmitPlayerResultRequest request, CancellationToken ct = default);

	Task<TournamentMatchDto?> SetAdminResultAsync(Guid tournamentId, Guid matchId, SetAdminResultRequest request, CancellationToken ct = default);

	Task<List<TournamentMatchDto>> ListAsync(Guid tournamentId, CancellationToken ct = default);
	Task<TournamentMatchDto?> GetAsync(Guid tournamentId, Guid matchId, CancellationToken ct = default);
}

/// <summary>Thrown for match validation failures (already finalized, malformed result, unknown participant) — maps to 400.</summary>
public class MatchValidationException(string message) : ValidationApiException(message);

/// <summary>Thrown when the caller isn't a participant of the match they're trying to act on — maps to 403.</summary>
public class MatchForbiddenException(string message) : ForbiddenApiException(message);

public class MatchService(
	ITournamentRepository tournamentRepository,
	ITournamentChallongeParticipantRepository challongeParticipantRepository,
	ITournamentMatchRepository matchRepository,
	ITeamRepository teamRepository,
	IPlayerRepository playerRepository,
	IChallongeClient challongeClient,
	IHistoryService historyService,
	IFirebaseStorageClient storageClient,
	ITournamentNotifier notifier) : IMatchService
{
	public async Task SyncFromChallongeAsync(Guid tournamentId, CancellationToken ct = default)
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

		long? resetChallongeMatchId = null;
		if (tournament.BracketType == "double elimination" && matches.Count > 0)
		{
			var maxRound = matches.Max(m => m.Round);
			var finalRoundMatches = matches
				.Where(m => m.Round == maxRound)
				.OrderBy(m => m.SuggestedPlayOrder)
				.ToList();
			if (finalRoundMatches.Count > 1)
			{
				resetChallongeMatchId = long.Parse(finalRoundMatches[^1].ChallongeMatchId);
			}
		}

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

			var winnerPlayerId = ResolvePlayerId(m.WinnerChallongeParticipantId, challongeToPlayerId);
			var p1PlayerId = ResolvePlayerId(m.P1ChallongeParticipantId, challongeToPlayerId);
			var p2PlayerId = ResolvePlayerId(m.P2ChallongeParticipantId, challongeToPlayerId);
			var winnerTeamId = ResolveTeamId(m.WinnerChallongeParticipantId, challongeToTeamId);
			var p1TeamId = ResolveTeamId(m.P1ChallongeParticipantId, challongeToTeamId);
			var p2TeamId = ResolveTeamId(m.P2ChallongeParticipantId, challongeToTeamId);

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
				// Only the Challonge-sourced fields are overwritten here — result/approval/history
				// fields belong to SubmitPlayerResultAsync/SetAdminResultAsync, not this sync.
				existing.State = m.State;
				existing.WinnerPlayerId = winnerPlayerId;
				existing.WinnerTeamId = winnerTeamId;
				existing.P1PlayerId = p1PlayerId;
				existing.P2PlayerId = p2PlayerId;
				existing.P1TeamId = p1TeamId;
				existing.P2TeamId = p2TeamId;
			}

			if (winnerPlayerId is null && winnerTeamId is null)
			{
				allMatchesPlayed = false;
			}
		}

		await matchRepository.SaveChangesAsync(ct);
		await notifier.MatchesSyncedAsync(tournamentId, ct);

		if (allMatchesPlayed)
		{
			tournament.State = TournamentState.AwaitingReview;
			await tournamentRepository.SaveChangesAsync(ct);
			await notifier.TournamentChangedAsync(tournamentId, ct);
		}
	}

	public async Task<TournamentMatchDto?> SubmitPlayerResultAsync(Guid tournamentId, Guid matchId, string callerPlayerId, SubmitPlayerResultRequest request, CancellationToken ct = default)
	{
		var match = await matchRepository.GetByIdAsync(tournamentId, matchId, ct);
		if (match is null)
		{
			return null;
		}

		var caller = await playerRepository.GetEntityByUidAsync(callerPlayerId, ct)
			?? throw new MatchForbiddenException("Not a participant of this match.");

		var isTeamMatch = match.P1TeamId is not null || match.P2TeamId is not null;
		var callerIsP1 = isTeamMatch
			? await IsTeamMemberAsync(match.P1TeamId, caller.Id, ct)
			: match.P1PlayerId == caller.Id;
		var callerIsP2 = isTeamMatch
			? await IsTeamMemberAsync(match.P2TeamId, caller.Id, ct)
			: match.P2PlayerId == caller.Id;

		if (!callerIsP1 && !callerIsP2)
		{
			throw new MatchForbiddenException("Not a participant of this match.");
		}

		if (match.State == "complete")
		{
			throw new MatchValidationException("Match result is already finalized.");
		}

		var uploadedScreenshotUrl = request.ResultScreenshot is not null
			? await storageClient.UploadImageAsync(request.ResultScreenshot, $"tournaments/{tournamentId}/matches/{match.ChallongeMatchId}", ct)
			: null;

		var trimmedP1 = request.ResultP1.Trim();
		var trimmedP2 = request.ResultP2.Trim();
		var resetResult = trimmedP1 != (match.ResultP1 ?? string.Empty) ||
			trimmedP2 != (match.ResultP2 ?? string.Empty) ||
			(match.ResultScreenshotUrl is not null && uploadedScreenshotUrl is not null);

		if (resetResult)
		{
			match.P1ApprovedResult = false;
			match.P2ApprovedResult = false;
			match.ResultP1 = trimmedP1;
			match.ResultP2 = trimmedP2;
		}

		if (uploadedScreenshotUrl is not null)
		{
			match.ResultScreenshotUrl = uploadedScreenshotUrl;
		}

		if (callerIsP1)
		{
			match.P1ApprovedResult = !match.P1ApprovedResult;
		}
		else
		{
			match.P2ApprovedResult = !match.P2ApprovedResult;
		}

		if (match.P1ApprovedResult && match.P2ApprovedResult)
		{
			var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct)
				?? throw new MatchValidationException($"Tournament '{tournamentId}' does not exist.");

			var p1Win = IsP1Win(tournament.GameMode, match.ResultP1!, match.ResultP2!);

			var (winnerPlayerId, winnerTeamId, historyEntryId, historyEntryId2) = await ReportAndRegisterAsync(
				tournament, match, p1Win, "0",
				match.ResultP1, match.ResultP2, match.ResultScreenshotUrl, techLoss: false, ct);

			match.HistoryEntryId = historyEntryId;
			match.HistoryEntryId2 = historyEntryId2;
			match.State = "complete";
			match.WinnerPlayerId = winnerPlayerId;
			match.WinnerTeamId = winnerTeamId;
		}

		await matchRepository.SaveChangesAsync(ct);
		await notifier.MatchChangedAsync(tournamentId, matchId, ct);
		return (await ToDtosAsync([match], ct))[0];
	}

	public async Task<TournamentMatchDto?> SetAdminResultAsync(Guid tournamentId, Guid matchId, SetAdminResultRequest request, CancellationToken ct = default)
	{
		var match = await matchRepository.GetByIdAsync(tournamentId, matchId, ct);
		if (match is null)
		{
			return null;
		}

		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct)
			?? throw new MatchValidationException($"Tournament '{tournamentId}' does not exist.");

		var isTeamMatch = match.P1TeamId is not null || match.P2TeamId is not null;
		Guid? techLossPlayerId = null;
		if (request.TechLossPlayerId is not null)
		{
			var techLossPlayer = await playerRepository.GetEntityByUidAsync(request.TechLossPlayerId, ct)
				?? throw new MatchValidationException($"Player '{request.TechLossPlayerId}' does not exist.");
			techLossPlayerId = techLossPlayer.Id;
		}
		var techLoss = isTeamMatch ? request.TechLossTeamId is not null : techLossPlayerId is not null;
		bool p1Win;
		string? finalResultP1 = match.ResultP1;
		string? finalResultP2 = match.ResultP2;

		if (techLoss)
		{
			if (isTeamMatch)
			{
				if (request.TechLossTeamId != match.P1TeamId && request.TechLossTeamId != match.P2TeamId)
				{
					throw new MatchValidationException("techLossTeamId must be a participant of this match.");
				}

				p1Win = request.TechLossTeamId == match.P2TeamId;
			}
			else
			{
				if (techLossPlayerId != match.P1PlayerId && techLossPlayerId != match.P2PlayerId)
				{
					throw new MatchValidationException("techLossPlayerId must be a participant of this match.");
				}

				p1Win = techLossPlayerId == match.P2PlayerId;
			}
		}
		else
		{
			if (request.ResultP1 is null || request.ResultP2 is null)
			{
				throw new MatchValidationException("resultP1 and resultP2 are required.");
			}

			finalResultP1 = request.ResultP1.Trim();
			finalResultP2 = request.ResultP2.Trim();
			p1Win = IsP1Win(tournament.GameMode, finalResultP1, finalResultP2);
		}

		var uploadedScreenshotUrl = request.ResultScreenshot is not null
			? await storageClient.UploadImageAsync(request.ResultScreenshot, $"tournaments/{tournamentId}/matches/{match.ChallongeMatchId}", ct)
			: null;
		var resultScreenshotUrl = uploadedScreenshotUrl ?? match.ResultScreenshotUrl;

		if (match.HistoryEntryId is Guid oldEntryId)
		{
			await historyService.ReverseEntryAsync(oldEntryId, ct);
		}

		if (match.HistoryEntryId2 is Guid oldEntryId2)
		{
			await historyService.ReverseEntryAsync(oldEntryId2, ct);
		}

		if (isTeamMatch)
		{
			if (match.P1TeamId is null || match.P2TeamId is null)
			{
				throw new MatchValidationException("Match does not have two known team participants yet.");
			}
		}
		else if (match.P1PlayerId is null || match.P2PlayerId is null)
		{
			throw new MatchValidationException("Match does not have two known participants yet.");
		}

		var (winnerPlayerId, winnerTeamId, historyEntryId, historyEntryId2) = await ReportAndRegisterAsync(
			tournament, match, p1Win, techLoss ? "999" : "0",
			finalResultP1, finalResultP2, resultScreenshotUrl, techLoss, ct);

		match.ResultP1 = finalResultP1;
		match.ResultP2 = finalResultP2;
		match.ResultScreenshotUrl = resultScreenshotUrl;
		match.P1ApprovedResult = true;
		match.P2ApprovedResult = true;
		match.State = "complete";
		match.WinnerPlayerId = winnerPlayerId;
		match.WinnerTeamId = winnerTeamId;
		match.TechLossPlayerId = techLoss && !isTeamMatch ? techLossPlayerId : null;
		match.TechLossTeamId = techLoss && isTeamMatch ? request.TechLossTeamId : null;
		match.HistoryEntryId = historyEntryId;
		match.HistoryEntryId2 = historyEntryId2;

		await matchRepository.SaveChangesAsync(ct);
		await notifier.MatchChangedAsync(tournamentId, matchId, ct);
		return (await ToDtosAsync([match], ct))[0];
	}

	public async Task<List<TournamentMatchDto>> ListAsync(Guid tournamentId, CancellationToken ct = default) =>
		await ToDtosAsync(await matchRepository.ListByTournamentAsync(tournamentId, ct), ct);

	public async Task<TournamentMatchDto?> GetAsync(Guid tournamentId, Guid matchId, CancellationToken ct = default)
	{
		var match = await matchRepository.GetByIdAsync(tournamentId, matchId, ct);
		return match is null ? null : (await ToDtosAsync([match], ct))[0];
	}

	// Shared tail of both approveResult.js and adminSetMatchResult.js: report the score to
	// Challonge, resync the bracket, then register the Elo/history change. Branches on
	// match.P1TeamId/P2TeamId (set XOR with P1PlayerId/P2PlayerId) to call the team- or
	// solo-flavored history registration; a team match only ever populates WinnerTeamId/
	// HistoryEntryId2, a solo match only ever populates WinnerPlayerId/leaves HistoryEntryId2 null.
	private async Task<(Guid? WinnerPlayerId, Guid? WinnerTeamId, Guid HistoryEntryId, Guid? HistoryEntryId2)> ReportAndRegisterAsync(
		Tournament tournament, TournamentMatch match, bool p1Win, string loserScore,
		string? resultP1, string? resultP2, string? resultScreenshotUrl, bool techLoss, CancellationToken ct)
	{
		if (tournament.ChallongeTournamentId is null)
		{
			throw new MatchValidationException("Tournament has no Challonge bracket yet.");
		}

		// Only 2 rows are actually needed here (unlike SyncFromChallongeAsync, which legitimately
		// resolves every match against every participant) — targeted lookups instead of loading
		// the whole tournament's participant table just to find these two.
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

		await SyncFromChallongeAsync(tournament.Id, ct);

		if (match.P1TeamId is Guid t1 && match.P2TeamId is Guid t2)
		{
			var (entry1, entry2) = await historyService.RegisterTeamMatchResultAsync(
				t1, t2, p1Win, tournament.OverrideEloChange ?? -1, tournament.Id, match.Id, TournamentMatchKind.None,
				resultP1, resultP2, resultScreenshotUrl, techLoss, ct);

			return (null, p1Win ? t1 : t2, entry1.Id, entry2.Id);
		}
		else
		{
			var entry = await historyService.RegisterMatchResultAsync(
				match.P1PlayerId!.Value, match.P2PlayerId!.Value, p1Win, tournament.OverrideEloChange ?? -1, tournament.Id, match.Id, TournamentMatchKind.None,
				resultP1, resultP2, resultScreenshotUrl, techLoss, ct);

			return (p1Win ? match.P1PlayerId : match.P2PlayerId, null, entry.Id, null);
		}
	}

	private async Task<bool> IsTeamMemberAsync(Guid? teamId, Guid playerId, CancellationToken ct)
	{
		if (teamId is null)
		{
			return false;
		}

		var team = await teamRepository.GetByIdAsync(teamId.Value, ct);
		return team is not null && (team.Player1Id == playerId || team.Player2Id == playerId);
	}

	private static Guid? ResolvePlayerId(string? challongeParticipantId, Dictionary<string, Guid> challongeToPlayerId) =>
		challongeParticipantId is not null && challongeToPlayerId.TryGetValue(challongeParticipantId, out var playerId) ? playerId : null;

	private static Guid? ResolveTeamId(string? challongeParticipantId, Dictionary<string, Guid> challongeToTeamId) =>
		challongeParticipantId is not null && challongeToTeamId.TryGetValue(challongeParticipantId, out var teamId) ? teamId : null;

	private static bool IsP1Win(TournamentGameMode mode, string resultP1, string resultP2)
	{
		try
		{
			return mode == TournamentGameMode.DeadlyAssault
				? int.Parse(resultP1) > int.Parse(resultP2)
				: ParseMinutes(resultP1) < ParseMinutes(resultP2);
		}
		catch (Exception ex) when (ex is FormatException or OverflowException or IndexOutOfRangeException)
		{
			throw new MatchValidationException(
				$"'{resultP1}'/'{resultP2}' is not a valid result for this tournament's game mode ({mode}).");
		}
	}

	// Despite the name, this parses "H:MM" as hours*60+minutes, not seconds.
	private static int ParseMinutes(string time)
	{
		var parts = time.Split(':');
		return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
	}

	// Batch Id -> Uid resolution for the (up to 4) player-id fields per match — one query for
	// the whole list/page, not one per row or per field.
	private async Task<List<TournamentMatchDto>> ToDtosAsync(List<TournamentMatch> matches, CancellationToken ct)
	{
		var ids = matches
			.SelectMany(m => new[] { m.P1PlayerId, m.P2PlayerId, m.WinnerPlayerId, m.TechLossPlayerId })
			.Where(id => id is not null)
			.Select(id => id!.Value)
			.Distinct();
		var uidsById = await playerRepository.GetUidsByIdsAsync(ids, ct);

		return matches.Select(m => ToDto(m, uidsById)).ToList();
	}

	private static TournamentMatchDto ToDto(TournamentMatch m, Dictionary<Guid, string> uidsById) => new(
		m.Id, m.TournamentId, m.ChallongeMatchId,
		m.P1PlayerId is Guid p1 ? uidsById.GetValueOrDefault(p1) : null,
		m.P2PlayerId is Guid p2 ? uidsById.GetValueOrDefault(p2) : null,
		m.P1TeamId, m.P2TeamId, m.State,
		m.WinnerPlayerId is Guid w ? uidsById.GetValueOrDefault(w) : null,
		m.WinnerTeamId,
		m.ResultScreenshotUrl, m.ResultP1, m.ResultP2, m.P1ApprovedResult, m.P2ApprovedResult,
		m.TechLossPlayerId is Guid t ? uidsById.GetValueOrDefault(t) : null,
		m.TechLossTeamId);
}
