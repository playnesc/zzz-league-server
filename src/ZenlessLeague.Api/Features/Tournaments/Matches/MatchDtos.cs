namespace ZenlessLeague.Api.Features.Tournaments.Matches;

// ResultScreenshot is a base64 data-url image — the service uploads it and stores the resulting
// URL, matching legacy's client-sends-base64-in-body contract (functions/user/approveResult.js).
public record SubmitPlayerResultRequest(string ResultP1, string ResultP2, string? ResultScreenshot = null);

public record SetAdminResultRequest(
    string? ResultP1 = null,
    string? ResultP2 = null,
    string? ResultScreenshot = null,
    string? TechLossPlayerId = null);

public record TournamentMatchDto(
    Guid Id,
    Guid TournamentId,
    long ChallongeMatchId,
    string? P1PlayerId,
    string? P2PlayerId,
    string State,
    string? WinnerPlayerId,
    string? ResultScreenshotUrl,
    string? ResultP1,
    string? ResultP2,
    bool P1ApprovedResult,
    bool P2ApprovedResult,
    string? TechLossPlayerId);
