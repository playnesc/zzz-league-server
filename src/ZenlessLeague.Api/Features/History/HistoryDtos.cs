namespace ZenlessLeague.Api.Features.History;

public record RegisterMatchResultRequest(
	string P1PlayerId,
	string P2PlayerId,
	bool P1Win,
	int OverrideEloChange,
	bool TechLoss = false,
	string? ResultP1 = null,
	string? ResultP2 = null);

public record SetPlayerEloRequest(int Elo);

public record EloPreviewDto(int P1ChangeOnWin, int P1ChangeOnLoss, int P2ChangeOnWin, int P2ChangeOnLoss);

public record HistoryEntryDto(
	Guid Id,
	string P1PlayerId,
	int P1Change,
	string? P2PlayerId,
	int? P2Change,
	Guid? TournamentId,
	string? TournamentName,
	Guid? TournamentMatchId,
	TournamentMatchKind Kind,
	string? ResultP1,
	string? ResultP2,
	DateTimeOffset Timestamp);

// HasMore is `Entries.Count == take` (the page-size the caller asked for) — a cheap heuristic
// that avoids an extra COUNT query.
public record HistoryPageDto(List<HistoryEntryDto> Entries, bool HasMore);
