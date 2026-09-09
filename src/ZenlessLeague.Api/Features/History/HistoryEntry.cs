namespace ZenlessLeague.Api.Features.History;

// Mirrors the legacy `tournamentMatch` sentinel string ("custom"/"techloss"/"adjustment") for
// entries that aren't tied to a real bracket match. A real bracket match entry (Phase 3) will
// have Kind=None and TournamentMatchId set instead.
public enum TournamentMatchKind { None, Custom, TechLoss, Adjustment }

// Replaces the legacy `historyV3` + `historyByPlayer` fan-out (functions/utils/registerMatchResult.js)
// with one indexed table — both legacy read patterns (global history scan, per-player history)
// become a WHERE clause instead of a second write path.
public class HistoryEntry
{
    public Guid Id { get; set; }

    public required string P1PlayerId { get; set; }
    public int P1Change { get; set; }

    public string? P2PlayerId { get; set; } // null for setPlayerElo-style single-player adjustments
    public int? P2Change { get; set; }

    public Guid? TournamentId { get; set; }
    public Guid? TournamentMatchId { get; set; }
    public TournamentMatchKind Kind { get; set; } = TournamentMatchKind.None;

    public string? ResultP1 { get; set; }
    public string? ResultP2 { get; set; }
    public string? ResultScreenshotUrl { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    // The legacy `historyV3` RTDB push-key — same traceability/idempotency rationale as
    // Tournament.LegacyId. Also how the Phase 8 migrator resolves TournamentMatch.HistoryEntryId
    // (legacy stored that as a runtime-only `historyKey` string on the match, not a real FK).
    public string? LegacyHistoryKey { get; set; }
}
