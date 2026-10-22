using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Storage;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments.Realtime;
using ZenlessLeague.Api.Infrastructure.Errors;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public interface IMatchService
{
	// Pulls match state back from Challonge, including the double-elimination "reset"
	// grand-finals placeholder handling, and flips the tournament to AwaitingReview once every
	// synced match has a winner.
	Task SyncFromChallongeAsync(Guid tournamentId, CancellationToken ct = default);

	Task<TournamentMatchDto?> SubmitPlayerResultAsync(Guid tournamentId, Guid matchId, string callerPlayerId, SubmitPlayerResultRequest request, CancellationToken ct = default);

	Task<TournamentMatchDto?> SetAdminResultAsync(Guid tournamentId, Guid matchId, SetAdminResultRequest request, CancellationToken ct = default);

	// Parses a pasted Nexus result link for a solo match without saving anything, so the popup can
	// show the result, the side mapping, and any values that couldn't be read before submitting.
	Task<NexusPreviewDto?> PreviewNexusResultAsync(Guid tournamentId, Guid matchId, string callerPlayerId, NexusPreviewRequest request, CancellationToken ct = default);

	Task<List<TournamentMatchDto>> ListAsync(Guid tournamentId, CancellationToken ct = default);
	Task<TournamentMatchDto?> GetAsync(Guid tournamentId, Guid matchId, CancellationToken ct = default);
}

/// <summary>Thrown for match validation failures (already finalized, malformed result, unknown participant) — maps to 400.</summary>
public class MatchValidationException(string message) : ValidationApiException(message);

/// <summary>Thrown when the caller isn't a participant of the match they're trying to act on — maps to 403.</summary>
public class MatchForbiddenException(string message) : ForbiddenApiException(message);

// Result submission for tournament matches: players' own submissions and confirmations, and the
// moderator override. The Challonge round-trip lives in ChallongeMatchSync, Nexus link handling in
// NexusResultResolver.
public class MatchService(
	ITournamentRepository tournamentRepository,
	ITournamentMatchRepository matchRepository,
	ITeamRepository teamRepository,
	IPlayerRepository playerRepository,
	ChallongeMatchSync challongeSync,
	NexusResultResolver nexusResolver,
	IHistoryService historyService,
	IFirebaseStorageClient storageClient,
	ITournamentNotifier notifier,
	IOptions<NexusOptions> nexusOptions,
	IUnitOfWork unitOfWork) : IMatchService
{
	private bool ParseNexusResults => nexusOptions.Value.ParseResults;

	public async Task SyncFromChallongeAsync(Guid tournamentId, CancellationToken ct = default)
	{
		var tournamentChanged = await challongeSync.SyncAsync(tournamentId, ct);
		await NotifySyncedAsync(tournamentId, tournamentChanged, ct);
	}

	private async Task NotifySyncedAsync(Guid tournamentId, bool tournamentChanged, CancellationToken ct)
	{
		await notifier.MatchesSyncedAsync(tournamentId, ct);
		if (tournamentChanged)
		{
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

		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct)
			?? throw new MatchValidationException($"Tournament '{tournamentId}' does not exist.");

		if (isTeamMatch)
		{
			await ApplyTeamResultAsync(tournamentId, match, request, ct);
		}
		else
		{
			if (ParseNexusResults)
			{
				await ApplySoloNexusResultAsync(tournament, match, request, ct);
			}
			else
			{
				ApplySoloTypedResult(match, request);
			}
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
			var p1Win = IsP1Win(tournament.GameMode, match.ResultP1!, match.ResultP2!);
			await challongeSync.ReportResultAsync(tournament, match, p1Win, "0", ct);
			var tournamentChanged = await RecordResultAsync(
				tournament, match, p1Win, match.ResultP1, match.ResultP2, techLoss: false, beforeRecording: null, ct);
			await NotifySyncedAsync(tournamentId, tournamentChanged, ct);
		}
		else
		{
			await matchRepository.SaveChangesAsync(ct);
		}

		await notifier.MatchChangedAsync(tournamentId, matchId, ct);
		return (await ToDtosAsync([match], ct))[0];
	}

	public async Task<NexusPreviewDto?> PreviewNexusResultAsync(Guid tournamentId, Guid matchId, string callerPlayerId, NexusPreviewRequest request, CancellationToken ct = default)
	{
		var match = await matchRepository.GetByIdAsync(tournamentId, matchId, ct);
		if (match is null)
		{
			return null;
		}

		var caller = await playerRepository.GetEntityByUidAsync(callerPlayerId, ct)
			?? throw new MatchForbiddenException("Not a participant of this match.");
		if (match.P1PlayerId != caller.Id && match.P2PlayerId != caller.Id && caller.Role < PlayerRole.Moderator)
		{
			throw new MatchForbiddenException("Not a participant of this match.");
		}

		if (match.P1TeamId is not null || match.P2TeamId is not null)
		{
			throw new MatchValidationException("Nexus result links are only supported for solo matches.");
		}

		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct)
			?? throw new MatchValidationException($"Tournament '{tournamentId}' does not exist.");

		var nexus = await nexusResolver.LoadAsync(tournament, match, request.Url, request.NexusP1Side, ct);
		var (resultP1, resultP2) = NexusResultResolver.MapTotals(nexus.Parsed, nexus.P1IsLeft);
		return new NexusPreviewDto(
			nexus.ResultId, nexus.Parsed.LeftName, nexus.Parsed.RightName, nexus.P1IsLeft,
			nexus.Parsed.LeftTotal, nexus.Parsed.RightTotal, resultP1, resultP2,
			nexus.Parsed.TotalGames, nexus.Parsed.CountedGames, nexus.Parsed.IsTie,
			ToMissingDtos(nexus.Parsed, nexus.P1IsLeft));
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
			if (request.NexusResultUrl is not null && isTeamMatch)
			{
				throw new MatchValidationException("Nexus result links are only supported for solo matches.");
			}

			if (request.NexusResultUrl is not null && !ParseNexusResults)
			{
				// Stored as proof only (see ApplySoloTypedResult); the typed values are the result.
				SetNexusResultId(match, NexusResultResolver.ParseLinkOrNull(request.NexusResultUrl));
			}
			else if (request.NexusResultUrl is not null)
			{
				var nexus = await nexusResolver.LoadAsync(tournament, match, request.NexusResultUrl, request.NexusP1Side, ct);
				var (nexusP1, nexusP2) = NexusResultResolver.MapTotals(nexus.Parsed, nexus.P1IsLeft);
				match.NexusResultId = nexus.ResultId;
				match.NexusSnapshot = nexus.Json;
				match.NexusP1IsLeft = nexus.P1IsLeft;

				// Typed values win; the link only fills in what the moderator left empty.
				request = request with
				{
					ResultP1 = string.IsNullOrWhiteSpace(request.ResultP1) ? nexusP1 : request.ResultP1,
					ResultP2 = string.IsNullOrWhiteSpace(request.ResultP2) ? nexusP2 : request.ResultP2,
				};
			}

			if (string.IsNullOrWhiteSpace(request.ResultP1) || string.IsNullOrWhiteSpace(request.ResultP2))
			{
				throw new MatchValidationException("resultP1 and resultP2 are required.");
			}

			finalResultP1 = request.ResultP1.Trim();
			finalResultP2 = request.ResultP2.Trim();
			p1Win = IsP1Win(tournament.GameMode, finalResultP1, finalResultP2);
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

		var uploadedScreenshotUrl = request.ResultScreenshot is not null
			? await storageClient.UploadImageAsync(request.ResultScreenshot, $"tournaments/{tournamentId}/matches/{match.ChallongeMatchId}", ct)
			: null;
		var resultScreenshotUrl = uploadedScreenshotUrl ?? match.ResultScreenshotUrl;

		await challongeSync.ReportResultAsync(tournament, match, p1Win, techLoss ? "999" : "0", ct);

		// Undoing the previous result (if any) is part of the same transaction as recording the new
		// one, so a failure can't leave the match with its old result reversed but no new one applied.
		var tournamentChanged = await RecordResultAsync(
			tournament, match, p1Win, finalResultP1, finalResultP2, techLoss,
			beforeRecording: async () =>
			{
				if (match.HistoryEntryId is Guid oldEntryId)
				{
					await historyService.ReverseEntryAsync(oldEntryId, ct);
				}

				if (match.HistoryEntryId2 is Guid oldEntryId2)
				{
					await historyService.ReverseEntryAsync(oldEntryId2, ct);
				}

				match.ResultP1 = finalResultP1;
				match.ResultP2 = finalResultP2;
				match.ResultScreenshotUrl = resultScreenshotUrl;
				match.P1ApprovedResult = true;
				match.P2ApprovedResult = true;
				match.TechLossPlayerId = techLoss && !isTeamMatch ? techLossPlayerId : null;
				match.TechLossTeamId = techLoss && isTeamMatch ? request.TechLossTeamId : null;
			},
			ct);

		await NotifySyncedAsync(tournamentId, tournamentChanged, ct);
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

	// Finalizing a result is two steps, shared by player confirmation and the admin override:
	//  1. ChallongeMatchSync.ReportResultAsync — the external, non-transactional part. Re-reporting
	//     the same score is harmless, so if step 2 then fails the whole thing can simply be retried.
	//  2. RecordResultAsync — every database write (resync, Elo/history, the match itself) in ONE
	//     transaction: either the result is fully recorded or nothing changed and the match is still
	//     unfinalized and retryable. Notifications are the caller's job, after this has committed.
	//
	// Returns whether the tournament's own state changed (for the caller's post-commit notify).
	// `beforeRecording` runs first inside the same transaction (the admin path reverses the previous
	// result there). Branches on match.P1TeamId/P2TeamId (set XOR with P1PlayerId/P2PlayerId): a team
	// match only ever populates WinnerTeamId/HistoryEntryId2, a solo match only WinnerPlayerId.
	private Task<bool> RecordResultAsync(
		Tournament tournament, TournamentMatch match, bool p1Win, string? resultP1, string? resultP2, bool techLoss,
		Func<Task>? beforeRecording, CancellationToken ct) =>
		unitOfWork.InTransactionAsync(async () =>
		{
			if (beforeRecording is not null)
			{
				await beforeRecording();
			}

			var tournamentChanged = await challongeSync.SyncAsync(tournament.Id, ct);

			// Tagged so history shows "tech loss" instead of whatever result was typed before it.
			var kind = techLoss ? TournamentMatchKind.TechLoss : TournamentMatchKind.None;
			if (match.P1TeamId is Guid t1 && match.P2TeamId is Guid t2)
			{
				var (entry1, entry2) = await historyService.RegisterTeamMatchResultAsync(
					t1, t2, p1Win, tournament.OverrideEloChange ?? -1, tournament.Id, match.Id, kind,
					resultP1, resultP2, techLoss, ct);

				match.WinnerPlayerId = null;
				match.WinnerTeamId = p1Win ? t1 : t2;
				match.HistoryEntryId = entry1.Id;
				match.HistoryEntryId2 = entry2.Id;
			}
			else
			{
				var entry = await historyService.RegisterMatchResultAsync(
					match.P1PlayerId!.Value, match.P2PlayerId!.Value, p1Win, tournament.OverrideEloChange ?? -1, tournament.Id, match.Id, kind,
					resultP1, resultP2, techLoss, ct);

				match.WinnerPlayerId = p1Win ? match.P1PlayerId : match.P2PlayerId;
				match.WinnerTeamId = null;
				match.HistoryEntryId = entry.Id;
				match.HistoryEntryId2 = null;
			}

			match.State = "complete";
			await matchRepository.SaveChangesAsync(ct);
			return tournamentChanged;
		}, ct);

	// Team matches: typed result + screenshot, as before Nexus links existed. Any change to the
	// result (or a replacement screenshot) resets both confirmations.
	private async Task ApplyTeamResultAsync(Guid tournamentId, TournamentMatch match, SubmitPlayerResultRequest request, CancellationToken ct)
	{
		if (request.ResultP1 is null || request.ResultP2 is null)
		{
			throw new MatchValidationException("resultP1 and resultP2 are required.");
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
	}

	// Solo matches: the result comes only from a Nexus link. Without a link the caller is just
	// confirming the result already stored. A new link, or the same link whose data changed on
	// Nexus, resets both confirmations so the opponent sees and confirms what was actually stored.
	private async Task ApplySoloNexusResultAsync(Tournament tournament, TournamentMatch match, SubmitPlayerResultRequest request, CancellationToken ct)
	{
		if (request.NexusResultUrl is null)
		{
			if (match.NexusResultId is null)
			{
				throw new MatchValidationException("Paste the Nexus result link.");
			}

			return;
		}

		var nexus = await nexusResolver.LoadAsync(tournament, match, request.NexusResultUrl, request.NexusP1Side, ct);
		if (nexus.P1IsLeft is null)
		{
			throw new MatchValidationException("Couldn't match the Nexus player names — choose which side is yours.");
		}

		if (nexus.Parsed.CountedGames == 0)
		{
			throw new MatchValidationException("This Nexus result has no finished game yet.");
		}

		if (nexus.Parsed.IsTie)
		{
			throw new MatchValidationException("This Nexus result is a tie — ask a moderator to set the result.");
		}

		var (resultP1, resultP2) = NexusResultResolver.MapTotals(nexus.Parsed, nexus.P1IsLeft);
		var changed = nexus.ResultId != match.NexusResultId ||
			nexus.P1IsLeft != match.NexusP1IsLeft ||
			resultP1 != match.ResultP1 ||
			resultP2 != match.ResultP2;

		if (changed)
		{
			match.P1ApprovedResult = false;
			match.P2ApprovedResult = false;
			match.ResultP1 = resultP1;
			match.ResultP2 = resultP2;
			match.NexusResultId = nexus.ResultId;
			match.NexusSnapshot = nexus.Json;
			match.NexusP1IsLeft = nexus.P1IsLeft;
		}
	}

	// Used while Nexus results can't be parsed (NexusOptions.ParseResults = false): typed result
	// + the Nexus link as proof (shown as an embed instead of a screenshot). The link is only
	// checked for shape, not fetched. Without a new link the stored one is kept. Any change to the
	// result or the link resets both confirmations.
	private static void ApplySoloTypedResult(TournamentMatch match, SubmitPlayerResultRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.ResultP1) || string.IsNullOrWhiteSpace(request.ResultP2))
		{
			throw new MatchValidationException("resultP1 and resultP2 are required.");
		}

		var resultId = NexusResultResolver.ParseLinkOrNull(request.NexusResultUrl) ?? match.NexusResultId;
		if (resultId is null && match.ResultScreenshotUrl is null)
		{
			throw new MatchValidationException("Paste the Nexus result link.");
		}

		var trimmedP1 = request.ResultP1.Trim();
		var trimmedP2 = request.ResultP2.Trim();
		var changed = trimmedP1 != match.ResultP1 ||
			trimmedP2 != match.ResultP2 ||
			resultId != match.NexusResultId;

		if (changed)
		{
			match.P1ApprovedResult = false;
			match.P2ApprovedResult = false;
			match.ResultP1 = trimmedP1;
			match.ResultP2 = trimmedP2;
			SetNexusResultId(match, resultId);
		}
	}

	// A different link invalidates whatever was parsed from the old one.
	private static void SetNexusResultId(TournamentMatch match, string? resultId)
	{
		if (resultId == match.NexusResultId)
		{
			return;
		}

		match.NexusResultId = resultId;
		match.NexusSnapshot = null;
		match.NexusP1IsLeft = null;
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

	private static TournamentMatchDto ToDto(TournamentMatch m, Dictionary<Guid, string> uidsById)
	{
		// Re-derived from the stored snapshot rather than persisted, so the popup can show which
		// values couldn't be read without an extra column. A snapshot that no longer parses (Nexus
		// format change) just drops the details; the stored ResultP1/ResultP2 stay authoritative.
		NexusParsedResult? nexus = null;
		if (m.NexusSnapshot is not null)
		{
			try
			{
				nexus = NexusResultParser.Parse(m.NexusSnapshot, expectedMode: null);
			}
			catch (Exception ex) when (ex is NexusResultException or NexusFormatException)
			{
			}
		}

		return new(
			m.Id, m.TournamentId, m.ChallongeMatchId,
			m.P1PlayerId is Guid p1 ? uidsById.GetValueOrDefault(p1) : null,
			m.P2PlayerId is Guid p2 ? uidsById.GetValueOrDefault(p2) : null,
			m.P1TeamId, m.P2TeamId, m.State,
			m.WinnerPlayerId is Guid w ? uidsById.GetValueOrDefault(w) : null,
			m.WinnerTeamId,
			m.ResultScreenshotUrl, m.ResultP1, m.ResultP2, m.P1ApprovedResult, m.P2ApprovedResult,
			m.TechLossPlayerId is Guid t ? uidsById.GetValueOrDefault(t) : null,
			m.TechLossTeamId,
			m.NexusResultId,
			nexus?.TotalGames,
			nexus?.CountedGames,
			nexus is null ? null : ToMissingDtos(nexus, m.NexusP1IsLeft));
	}

	private static List<NexusMissingDto> ToMissingDtos(NexusParsedResult parsed, bool? p1IsLeft) =>
		parsed.Missing.Select(v => new NexusMissingDto(v.Game, SideLabel(v.Side, p1IsLeft))).ToList();

	private static string SideLabel(NexusSide side, bool? p1IsLeft) => p1IsLeft switch
	{
		null => side == NexusSide.Left ? "left" : "right",
		_ => (side == NexusSide.Left) == p1IsLeft.Value ? "p1" : "p2",
	};
}
