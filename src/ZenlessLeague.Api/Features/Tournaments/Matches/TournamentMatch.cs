namespace ZenlessLeague.Api.Features.Tournaments.Matches;

// Mirrors the legacy `tournaments/{id}/matches/{matchId}` RTDB node (functions/utils/updateTournamentGames.js,
// functions/user/approveResult.js, functions/admin/tournament/adminSetMatchResult.js).
// `ResultP1`/`ResultP2` stay strings ("H:MM" time — lower time wins, this is a racing/speedrun
// game format) to preserve the exact legacy parse/compare semantics rather than switching to TimeSpan.
public class TournamentMatch
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }

    // The Challonge match id — legacy used this directly as the RTDB key.
    public long ChallongeMatchId { get; set; }

    public string? P1PlayerId { get; set; } // null = legacy "TBD"
    public string? P2PlayerId { get; set; }

    public required string State { get; set; } // Challonge's own state vocabulary, passed through as-is
    public string? WinnerPlayerId { get; set; }

    public string? ResultScreenshotUrl { get; set; }
    public string? ResultP1 { get; set; }
    public string? ResultP2 { get; set; }
    public bool P1ApprovedResult { get; set; }
    public bool P2ApprovedResult { get; set; }
    public string? TechLossPlayerId { get; set; }

    public Guid? HistoryEntryId { get; set; } // replaces the legacy runtime-only `historyKey`
}
