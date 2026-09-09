using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.History;

public interface IHistoryService
{
    // Direct port of functions/utils/registerMatchResult.js. `tournamentId`/`kind` are null/None
    // for ad-hoc (non-tournament) matches — Phase 3's MatchService will pass a real tournamentId
    // once TournamentMatch exists.
    Task<HistoryEntryDto> RegisterMatchResultAsync(
        string p1Uid, string p2Uid, bool p1Win, int overrideEloChange,
        Guid? tournamentId = null, Guid? tournamentMatchId = null, TournamentMatchKind kind = TournamentMatchKind.None,
        string? resultP1 = null, string? resultP2 = null, string? resultScreenshotUrl = null,
        bool techLoss = false, CancellationToken ct = default);

    // Undoes a history entry's effect on the referenced player(s) and deletes it. Ports both the
    // reversal block embedded in functions/admin/tournament/adminSetMatchResult.js (2-player match
    // results — used before a result gets recomputed) and functions/admin/history/deleteHistoryEntry.js's
    // single-player branch (Elo-adjustment entries — reverts elo directly, recomputes tier flags).
    // Returns false if the entry doesn't exist.
    Task<bool> ReverseEntryAsync(Guid historyEntryId, CancellationToken ct = default);

    // Port of functions/utils/setPlayerElo.js.
    Task<HistoryEntryDto> SetPlayerEloAsync(string uid, int elo, CancellationToken ct = default);

    Task<List<HistoryEntryDto>> ListAsync(CancellationToken ct = default);
    Task<List<HistoryEntryDto>> ListByPlayerAsync(string uid, CancellationToken ct = default);
}

/// <summary>Thrown for history validation failures (missing player) — maps to 400.</summary>
public class HistoryValidationException(string message) : Exception(message);

public class HistoryService(
    IPlayerRepository playerRepository,
    IHistoryRepository historyRepository,
    IDiscordRoleAssignmentService discordRoleAssignmentService) : IHistoryService
{
    public async Task<HistoryEntryDto> RegisterMatchResultAsync(
        string p1Uid, string p2Uid, bool p1Win, int overrideEloChange,
        Guid? tournamentId = null, Guid? tournamentMatchId = null, TournamentMatchKind kind = TournamentMatchKind.None,
        string? resultP1 = null, string? resultP2 = null, string? resultScreenshotUrl = null,
        bool techLoss = false, CancellationToken ct = default)
    {
        var p1 = await playerRepository.GetEntityByIdAsync(p1Uid, ct)
            ?? throw new HistoryValidationException($"Player '{p1Uid}' does not exist.");
        var p2 = await playerRepository.GetEntityByIdAsync(p2Uid, ct)
            ?? throw new HistoryValidationException($"Player '{p2Uid}' does not exist.");

        var (p1Change, p2Change) = CalculateChanges(p1, p2, p1Win, overrideEloChange, techLoss);

        p1.TournamentPoints += p1Change;
        p2.TournamentPoints += p2Change;
        if (p1Win)
        {
            p1.Wins++;
            p2.Losses++;
        }
        else
        {
            p1.Losses++;
            p2.Wins++;
        }

        var now = DateTimeOffset.UtcNow;
        p1.LastPlayedTournamentTimestamp = now;
        p2.LastPlayedTournamentTimestamp = now;

        var entry = new HistoryEntry
        {
            Id = Guid.NewGuid(),
            P1PlayerId = p1Uid,
            P1Change = p1Change,
            P2PlayerId = p2Uid,
            P2Change = p2Change,
            TournamentId = tournamentId,
            TournamentMatchId = tournamentMatchId,
            // Legacy: when there's no tournamentId, the "kind" sentinel is forced from techLoss
            // regardless of what the caller passed — a real tournament match always passes its
            // own tournamentId, so this branch only ever applies to ad-hoc matches.
            Kind = tournamentId is null ? (techLoss ? TournamentMatchKind.TechLoss : TournamentMatchKind.Custom) : kind,
            ResultP1 = resultP1,
            ResultP2 = resultP2,
            ResultScreenshotUrl = resultScreenshotUrl,
            Timestamp = now,
        };

        await historyRepository.CreateAsync(entry, ct);
        return ToDto(entry);
    }

    public async Task<bool> ReverseEntryAsync(Guid historyEntryId, CancellationToken ct = default)
    {
        var entry = await historyRepository.GetByIdAsync(historyEntryId, ct);
        if (entry is null)
        {
            return false;
        }

        if (entry.P2PlayerId is not null && entry.P2Change is not null)
        {
            var p1 = await playerRepository.GetEntityByIdAsync(entry.P1PlayerId, ct)
                ?? throw new InvalidOperationException($"Player '{entry.P1PlayerId}' referenced by history entry '{entry.Id}' is missing.");
            var p2 = await playerRepository.GetEntityByIdAsync(entry.P2PlayerId, ct)
                ?? throw new InvalidOperationException($"Player '{entry.P2PlayerId}' referenced by history entry '{entry.Id}' is missing.");

            var oldP1Won = entry.P1Change > entry.P2Change.Value;

            p1.TournamentPoints -= entry.P1Change;
            p2.TournamentPoints -= entry.P2Change.Value;

            if (oldP1Won)
            {
                p1.Wins--;
                p2.Losses--;
            }
            else
            {
                p1.Losses--;
                p2.Wins--;
            }
        }
        else
        {
            // Single-player Elo adjustment (e.g. setPlayerElo/updatePlayerElo) — revert elo
            // directly and recompute tier flags, matching deleteHistoryEntry.js's other branch.
            var p1 = await playerRepository.GetEntityByIdAsync(entry.P1PlayerId, ct)
                ?? throw new InvalidOperationException($"Player '{entry.P1PlayerId}' referenced by history entry '{entry.Id}' is missing.");

            var revertedElo = p1.Elo - entry.P1Change;
            var (isMidConfirmed, isHighConfirmed) = TierConfirmationCalculator.Compute(p1.IsMidConfirmed, p1.IsHighConfirmed, revertedElo);
            p1.Elo = revertedElo;
            p1.IsMidConfirmed = isMidConfirmed;
            p1.IsHighConfirmed = isHighConfirmed;

            if (entry.P1Change > 0)
            {
                p1.Wins--;
            }
            else if (entry.P1Change < 0)
            {
                p1.Losses--;
            }
        }

        await historyRepository.DeleteAsync(entry, ct);
        return true;
    }

    public async Task<HistoryEntryDto> SetPlayerEloAsync(string uid, int elo, CancellationToken ct = default)
    {
        var player = await playerRepository.GetEntityByIdAsync(uid, ct)
            ?? throw new HistoryValidationException($"Player '{uid}' does not exist.");

        var change = elo - player.Elo;
        var (isMidConfirmed, isHighConfirmed) = TierConfirmationCalculator.Compute(player.IsMidConfirmed, player.IsHighConfirmed, elo);

        player.Elo = elo;
        player.IsMidConfirmed = isMidConfirmed;
        player.IsHighConfirmed = isHighConfirmed;

        var entry = new HistoryEntry
        {
            Id = Guid.NewGuid(),
            P1PlayerId = uid,
            P1Change = change,
            Kind = TournamentMatchKind.Adjustment,
            Timestamp = DateTimeOffset.UtcNow,
        };

        await historyRepository.CreateAsync(entry, ct);

        // Legacy calls this fire-and-forget (unawaited); we await it but swallow failures so a
        // Discord outage (or Discord simply not being configured) never blocks an Elo update.
        try
        {
            await discordRoleAssignmentService.AssignRoleForPlayerAsync(uid, ct);
        }
        catch
        {
            // best-effort — see comment above.
        }

        return ToDto(entry);
    }

    public async Task<List<HistoryEntryDto>> ListAsync(CancellationToken ct = default) =>
        (await historyRepository.ListAsync(ct)).Select(ToDto).ToList();

    public async Task<List<HistoryEntryDto>> ListByPlayerAsync(string uid, CancellationToken ct = default) =>
        (await historyRepository.ListByPlayerAsync(uid, ct)).Select(ToDto).ToList();

    // Direct port of registerMatchResult.js's win/loss change calculation.
    private static (int P1Change, int P2Change) CalculateChanges(Player p1, Player p2, bool p1Win, int overrideEloChange, bool techLoss)
    {
        var r1 = new EloCalculator.RatingInfo(p1.Elo, p1.TournamentPoints, p1.IsMidConfirmed);
        var r2 = new EloCalculator.RatingInfo(p2.Elo, p2.TournamentPoints, p2.IsMidConfirmed);

        if (techLoss)
        {
            var loserChange = overrideEloChange != -1
                ? -overrideEloChange
                : (p1Win ? EloCalculator.CalculateChange(r2, r1, false) : EloCalculator.CalculateChange(r1, r2, false));
            return p1Win ? (0, loserChange) : (loserChange, 0);
        }

        var p1Change = overrideEloChange != -1
            ? (p1Win ? overrideEloChange : -overrideEloChange)
            : EloCalculator.CalculateChange(r1, r2, p1Win);
        var p2Change = overrideEloChange != -1
            ? (p1Win ? -overrideEloChange : overrideEloChange)
            : EloCalculator.CalculateChange(r2, r1, !p1Win);

        return (p1Change, p2Change);
    }

    private static HistoryEntryDto ToDto(HistoryEntry e) => new(
        e.Id, e.P1PlayerId, e.P1Change, e.P2PlayerId, e.P2Change,
        e.TournamentId, e.TournamentMatchId, e.Kind, e.ResultP1, e.ResultP2, e.ResultScreenshotUrl, e.Timestamp);
}
