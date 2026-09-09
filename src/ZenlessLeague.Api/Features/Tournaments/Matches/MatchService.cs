using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Storage;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Realtime;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public interface IMatchService
{
    // Port of functions/utils/updateTournamentGames.js — pulls match state back from Challonge,
    // including the double-elimination "reset" grand-finals placeholder handling, and flips the
    // tournament to AwaitingReview once every synced match has a winner.
    Task SyncFromChallongeAsync(Guid tournamentId, CancellationToken ct = default);

    // Port of functions/user/approveResult.js.
    Task<TournamentMatchDto?> SubmitPlayerResultAsync(Guid tournamentId, Guid matchId, string callerPlayerId, SubmitPlayerResultRequest request, CancellationToken ct = default);

    // Port of functions/admin/tournament/adminSetMatchResult.js.
    Task<TournamentMatchDto?> SetAdminResultAsync(Guid tournamentId, Guid matchId, SetAdminResultRequest request, CancellationToken ct = default);

    Task<List<TournamentMatchDto>> ListAsync(Guid tournamentId, CancellationToken ct = default);
    Task<TournamentMatchDto?> GetAsync(Guid tournamentId, Guid matchId, CancellationToken ct = default);
}

/// <summary>Thrown for match validation failures (already finalized, malformed result, unknown participant) — maps to 400.</summary>
public class MatchValidationException(string message) : Exception(message);

/// <summary>Thrown when the caller isn't a participant of the match they're trying to act on — maps to 403.</summary>
public class MatchForbiddenException(string message) : Exception(message);

public class MatchService(
    ITournamentRepository tournamentRepository,
    ITournamentChallongeParticipantRepository challongeParticipantRepository,
    ITournamentMatchRepository matchRepository,
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
        var challongeToUid = participants.ToDictionary(p => p.ChallongeParticipantId, p => p.PlayerId);

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

            var winnerUid = ResolveUid(m.WinnerChallongeParticipantId, challongeToUid);
            var p1Uid = ResolveUid(m.P1ChallongeParticipantId, challongeToUid);
            var p2Uid = ResolveUid(m.P2ChallongeParticipantId, challongeToUid);

            var existing = await matchRepository.GetByChallongeMatchIdAsync(tournamentId, challongeMatchId, ct);
            if (existing is null)
            {
                await matchRepository.CreateAsync(new TournamentMatch
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournamentId,
                    ChallongeMatchId = challongeMatchId,
                    State = m.State,
                    WinnerPlayerId = winnerUid,
                    P1PlayerId = p1Uid,
                    P2PlayerId = p2Uid,
                }, ct);
            }
            else
            {
                // Only the Challonge-sourced fields are overwritten here — result/approval/history
                // fields belong to SubmitPlayerResultAsync/SetAdminResultAsync, not this sync.
                existing.State = m.State;
                existing.WinnerPlayerId = winnerUid;
                existing.P1PlayerId = p1Uid;
                existing.P2PlayerId = p2Uid;
            }

            if (winnerUid is null)
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

        if (match.P1PlayerId != callerPlayerId && match.P2PlayerId != callerPlayerId)
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

        if (callerPlayerId == match.P1PlayerId)
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

            var p1Win = ParseMinutes(match.ResultP1!) < ParseMinutes(match.ResultP2!);

            var (winnerUid, historyEntryId) = await ReportAndRegisterAsync(
                tournament, match, match.P1PlayerId!, match.P2PlayerId!, p1Win, "0",
                match.ResultP1, match.ResultP2, match.ResultScreenshotUrl, techLoss: false, ct);

            match.HistoryEntryId = historyEntryId;
            match.State = "complete";
            match.WinnerPlayerId = winnerUid;
        }

        await matchRepository.SaveChangesAsync(ct);
        await notifier.MatchChangedAsync(tournamentId, matchId, ct);
        return ToDto(match);
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

        var techLoss = request.TechLossPlayerId is not null;
        bool p1Win;
        string? finalResultP1 = match.ResultP1;
        string? finalResultP2 = match.ResultP2;

        if (techLoss)
        {
            if (request.TechLossPlayerId != match.P1PlayerId && request.TechLossPlayerId != match.P2PlayerId)
            {
                throw new MatchValidationException("techLossPlayerId must be a participant of this match.");
            }

            p1Win = request.TechLossPlayerId == match.P2PlayerId;
        }
        else
        {
            if (request.ResultP1 is null || request.ResultP2 is null)
            {
                throw new MatchValidationException("resultP1 and resultP2 are required.");
            }

            finalResultP1 = request.ResultP1.Trim();
            finalResultP2 = request.ResultP2.Trim();
            p1Win = ParseMinutes(finalResultP1) < ParseMinutes(finalResultP2);
        }

        var uploadedScreenshotUrl = request.ResultScreenshot is not null
            ? await storageClient.UploadImageAsync(request.ResultScreenshot, $"tournaments/{tournamentId}/matches/{match.ChallongeMatchId}", ct)
            : null;
        var resultScreenshotUrl = uploadedScreenshotUrl ?? match.ResultScreenshotUrl;

        if (match.HistoryEntryId is Guid oldEntryId)
        {
            await historyService.ReverseEntryAsync(oldEntryId, ct);
        }

        if (match.P1PlayerId is null || match.P2PlayerId is null)
        {
            throw new MatchValidationException("Match does not have two known participants yet.");
        }

        var (winnerUid, historyEntryId) = await ReportAndRegisterAsync(
            tournament, match, match.P1PlayerId, match.P2PlayerId, p1Win, techLoss ? "999" : "0",
            finalResultP1, finalResultP2, resultScreenshotUrl, techLoss, ct);

        match.ResultP1 = finalResultP1;
        match.ResultP2 = finalResultP2;
        match.ResultScreenshotUrl = resultScreenshotUrl;
        match.P1ApprovedResult = true;
        match.P2ApprovedResult = true;
        match.State = "complete";
        match.WinnerPlayerId = winnerUid;
        match.TechLossPlayerId = techLoss ? request.TechLossPlayerId : null;
        match.HistoryEntryId = historyEntryId;

        await matchRepository.SaveChangesAsync(ct);
        await notifier.MatchChangedAsync(tournamentId, matchId, ct);
        return ToDto(match);
    }

    public async Task<List<TournamentMatchDto>> ListAsync(Guid tournamentId, CancellationToken ct = default) =>
        (await matchRepository.ListByTournamentAsync(tournamentId, ct)).Select(ToDto).ToList();

    public async Task<TournamentMatchDto?> GetAsync(Guid tournamentId, Guid matchId, CancellationToken ct = default)
    {
        var match = await matchRepository.GetByIdAsync(tournamentId, matchId, ct);
        return match is null ? null : ToDto(match);
    }

    // Shared tail of both approveResult.js and adminSetMatchResult.js: report the score to
    // Challonge, resync the bracket, then register the Elo/history change.
    private async Task<(string WinnerUid, Guid HistoryEntryId)> ReportAndRegisterAsync(
        Tournament tournament, TournamentMatch match, string p1Uid, string p2Uid, bool p1Win, string loserScore,
        string? resultP1, string? resultP2, string? resultScreenshotUrl, bool techLoss, CancellationToken ct)
    {
        if (tournament.ChallongeTournamentId is null)
        {
            throw new MatchValidationException("Tournament has no Challonge bracket yet.");
        }

        var participants = await challongeParticipantRepository.ListByTournamentAsync(tournament.Id, ct);
        var uidToChallonge = participants.ToDictionary(p => p.PlayerId, p => p.ChallongeParticipantId);

        if (!uidToChallonge.TryGetValue(p1Uid, out var p1ChallongeId) || !uidToChallonge.TryGetValue(p2Uid, out var p2ChallongeId))
        {
            throw new MatchValidationException("Match participants are not registered as Challonge participants.");
        }

        await challongeClient.UpdateMatchResultAsync(
            tournament.ChallongeTournamentId, match.ChallongeMatchId.ToString(), p1ChallongeId, p2ChallongeId, p1Win, loserScore, ct);

        await SyncFromChallongeAsync(tournament.Id, ct);

        var entry = await historyService.RegisterMatchResultAsync(
            p1Uid, p2Uid, p1Win, tournament.OverrideEloChange ?? -1, tournament.Id, match.Id, TournamentMatchKind.None,
            resultP1, resultP2, resultScreenshotUrl, techLoss, ct);

        return (p1Win ? p1Uid : p2Uid, entry.Id);
    }

    private static string? ResolveUid(string? challongeParticipantId, Dictionary<string, string> challongeToUid) =>
        challongeParticipantId is not null && challongeToUid.TryGetValue(challongeParticipantId, out var uid) ? uid : null;

    // Direct port of the toMinutes()/toSeconds() helpers in approveResult.js/adminSetMatchResult.js
    // — despite the legacy name, this parses "H:MM" as hours*60+minutes, not seconds.
    private static int ParseMinutes(string time)
    {
        var parts = time.Split(':');
        return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
    }

    private static TournamentMatchDto ToDto(TournamentMatch m) => new(
        m.Id, m.TournamentId, m.ChallongeMatchId, m.P1PlayerId, m.P2PlayerId, m.State, m.WinnerPlayerId,
        m.ResultScreenshotUrl, m.ResultP1, m.ResultP2, m.P1ApprovedResult, m.P2ApprovedResult, m.TechLossPlayerId);
}
