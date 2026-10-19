using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Nexus;
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

public class MatchService(
	ITournamentRepository tournamentRepository,
	ITournamentChallongeParticipantRepository challongeParticipantRepository,
	ITournamentMatchRepository matchRepository,
	ITeamRepository teamRepository,
	IPlayerRepository playerRepository,
	IChallongeClient challongeClient,
	IHistoryService historyService,
	IFirebaseStorageClient storageClient,
	ITournamentNotifier notifier,
	INexusClient nexusClient,
	ITournamentRegistrationRepository registrationRepository,
	ILogger<MatchService> logger) : IMatchService
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

			var hasWinner = await UpsertMatchFromChallongeAsync(
				tournamentId, challongeMatchId, m, challongeToPlayerId, challongeToTeamId, ct);
			if (!hasWinner)
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

		var tournament = await tournamentRepository.GetByIdAsync(tournamentId, ct)
			?? throw new MatchValidationException($"Tournament '{tournamentId}' does not exist.");

		if (isTeamMatch)
		{
			await ApplyTeamResultAsync(tournamentId, match, request, ct);
		}
		else
		{
			await ApplySoloNexusResultAsync(tournament, match, request, ct);
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

			var (winnerPlayerId, winnerTeamId, historyEntryId, historyEntryId2) = await ReportAndRegisterAsync(
				tournament, match, p1Win, "0",
				match.ResultP1, match.ResultP2, techLoss: false, ct);

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

		var nexus = await LoadNexusResultAsync(tournament, match, request.Url, request.NexusP1Side, ct);
		var (resultP1, resultP2) = MapTotals(nexus.Parsed, nexus.P1IsLeft);
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
			if (request.NexusResultUrl is not null)
			{
				if (isTeamMatch)
				{
					throw new MatchValidationException("Nexus result links are only supported for solo matches.");
				}

				var nexus = await LoadNexusResultAsync(tournament, match, request.NexusResultUrl, request.NexusP1Side, ct);
				var (nexusP1, nexusP2) = MapTotals(nexus.Parsed, nexus.P1IsLeft);
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
			finalResultP1, finalResultP2, techLoss, ct);

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
		string? resultP1, string? resultP2, bool techLoss, CancellationToken ct)
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
				resultP1, resultP2, techLoss, ct);

			return (null, p1Win ? t1 : t2, entry1.Id, entry2.Id);
		}
		else
		{
			var entry = await historyService.RegisterMatchResultAsync(
				match.P1PlayerId!.Value, match.P2PlayerId!.Value, p1Win, tournament.OverrideEloChange ?? -1, tournament.Id, match.Id, TournamentMatchKind.None,
				resultP1, resultP2, techLoss, ct);

			return (p1Win ? match.P1PlayerId : match.P2PlayerId, null, entry.Id, null);
		}
	}

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

		var nexus = await LoadNexusResultAsync(tournament, match, request.NexusResultUrl, request.NexusP1Side, ct);
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

		var (resultP1, resultP2) = MapTotals(nexus.Parsed, nexus.P1IsLeft);
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

	private sealed record LoadedNexusResult(string ResultId, string Json, NexusParsedResult Parsed, bool? P1IsLeft);

	// Fetches + parses a pasted Nexus link and works out which Nexus side is P1: an explicit
	// "left"/"right" from the caller wins, otherwise the names on Nexus are matched against the two
	// players. P1IsLeft stays null when neither applies.
	private async Task<LoadedNexusResult> LoadNexusResultAsync(Tournament tournament, TournamentMatch match, string url, string? p1Side, CancellationToken ct)
	{
		if (!NexusLink.TryParseId(url, out var resultId))
		{
			throw new NexusResultException("That's not a Nexus result link (https://nexus-shiyu.com/results/…).");
		}

		if (match.P1PlayerId is not Guid p1Id || match.P2PlayerId is not Guid p2Id)
		{
			throw new MatchValidationException("Match does not have two known participants yet.");
		}

		var json = await nexusClient.GetResultJsonAsync(resultId, ct);
		NexusParsedResult parsed;
		try
		{
			parsed = NexusResultParser.Parse(json, tournament.GameMode);
		}
		catch (NexusFormatException ex)
		{
			// Nexus's API is undocumented and changes often — the raw response is what an admin
			// needs to update NexusResultParser (Coolify → API app → Logs).
			logger.LogError(ex, "Couldn't parse Nexus result {ResultId}; Nexus may have changed its format. Response: {Response}",
				resultId, json.Length > 8000 ? json[..8000] + "…" : json);
			throw;
		}

		var p1IsLeft = p1Side?.Trim().ToLowerInvariant() switch
		{
			"left" => true,
			"right" => false,
			null or "" => await MatchNexusSidesAsync(tournament.Id, p1Id, p2Id, parsed, ct),
			_ => throw new MatchValidationException("nexusP1Side must be \"left\" or \"right\"."),
		};

		return new LoadedNexusResult(resultId, json, parsed, p1IsLeft);
	}

	private static (string? ResultP1, string? ResultP2) MapTotals(NexusParsedResult parsed, bool? p1IsLeft) => p1IsLeft switch
	{
		true => (parsed.LeftTotal, parsed.RightTotal),
		false => (parsed.RightTotal, parsed.LeftTotal),
		null => (null, null),
	};

	// A player is recognized by their Nexus nickname from this tournament's registration (the
	// "DarteNickname" field), or failing that their site name / Discord username — case- and
	// whitespace-insensitive. One recognized player is enough to place both; contradictory matches
	// (e.g. both players look like "left") leave the sides unknown.
	private async Task<bool?> MatchNexusSidesAsync(Guid tournamentId, Guid p1Id, Guid p2Id, NexusParsedResult parsed, CancellationToken ct)
	{
		var p1Names = await GetKnownNexusNamesAsync(tournamentId, p1Id, ct);
		var p2Names = await GetKnownNexusNamesAsync(tournamentId, p2Id, ct);
		var left = NormalizeName(parsed.LeftName);
		var right = NormalizeName(parsed.RightName);

		var saysP1IsLeft = p1Names.Contains(left) || p2Names.Contains(right);
		var saysP1IsRight = p1Names.Contains(right) || p2Names.Contains(left);

		return saysP1IsLeft == saysP1IsRight ? null : saysP1IsLeft;
	}

	private async Task<HashSet<string>> GetKnownNexusNamesAsync(Guid tournamentId, Guid playerId, CancellationToken ct)
	{
		var registration = await registrationRepository.FindExistingAsync(tournamentId, playerId, null, ct);
		var player = await playerRepository.GetEntityByIdAsync(playerId, ct);

		return new[] { registration?.Player1Details.DarteNickname, player?.Name, player?.DiscordUsername }
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => NormalizeName(name!))
			.ToHashSet();
	}

	private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

	private async Task<bool> IsTeamMemberAsync(Guid? teamId, Guid playerId, CancellationToken ct)
	{
		if (teamId is null)
		{
			return false;
		}

		var team = await teamRepository.GetByIdAsync(teamId.Value, ct);
		return team is not null && (team.Player1Id == playerId || team.Player2Id == playerId);
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
	// SubmitPlayerResultAsync/SetAdminResultAsync, not this sync. Returns whether the match has a
	// winner yet (used by the caller to decide whether the whole tournament is done syncing).
	private async Task<bool> UpsertMatchFromChallongeAsync(
		Guid tournamentId, long challongeMatchId, ChallongeMatchResult m,
		Dictionary<string, Guid> challongeToPlayerId, Dictionary<string, Guid> challongeToTeamId, CancellationToken ct)
	{
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
