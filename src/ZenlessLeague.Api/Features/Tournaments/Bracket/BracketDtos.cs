namespace ZenlessLeague.Api.Features.Tournaments.Bracket;

// Slots are the round-1 layout, one entry per slot (null = bye): a player's uid in solo
// tournaments, a team id in team tournaments — the same identifiers the rest of the API uses.
// The matches themselves come from the match list (TournamentMatchDto carries the bracket fields).
public record BracketDto(
	string Format,
	int Size,
	List<string?> Slots,
	List<BracketStandingDto> Standings);

// Filled once the tournament is finished. Ranks can be shared ("5, 5, 7, 7").
public record BracketStandingDto(string? PlayerId, Guid? TeamId, int Rank);

public record SaveBracketLayoutRequest(List<string?> Slots);

// One match of a not-yet-saved layout (see BracketService.PreviewLayoutAsync): matches are keyed
// by Number, entrants by uid / team id like the slots.
public record BracketPreviewMatchDto(
	int Number,
	BracketSection Section,
	int Round,
	int Position,
	string? P1,
	string? P2,
	int? WinnerToNumber,
	int? WinnerToSlot,
	int? LoserToNumber,
	int? LoserToSlot);
