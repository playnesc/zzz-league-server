namespace ZenlessLeague.Api.Features.History;

public record RegisterMatchResultRequest(
    string P1PlayerId,
    string P2PlayerId,
    bool P1Win,
    int OverrideEloChange,
    bool TechLoss = false,
    string? ResultP1 = null,
    string? ResultP2 = null,
    string? ResultScreenshotUrl = null);

public record SetPlayerEloRequest(int Elo);

public record HistoryEntryDto(
    Guid Id,
    string P1PlayerId,
    int P1Change,
    string? P2PlayerId,
    int? P2Change,
    Guid? TournamentId,
    Guid? TournamentMatchId,
    TournamentMatchKind Kind,
    string? ResultP1,
    string? ResultP2,
    string? ResultScreenshotUrl,
    DateTimeOffset Timestamp);
