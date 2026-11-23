using ZenlessLeague.Api.Features.Tournaments.Bracket;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

// Solo matches, NexusOptions.ParseResults on (default): the result comes from NexusResultUrl, any
// zzz.nexus draft/result link of the match (ResultP1/ResultP2/ResultScreenshot are ignored); omit it
// to just toggle your confirmation of the result already stored. NexusP1Side ("left" = the draft's
// creator, "right" = its opponent) is only needed when the Nexus nicks couldn't be matched to
// players. ParseResults off (fallback): ResultP1/ResultP2 are required and NexusResultUrl is stored
// as proof without being fetched (required unless one is already stored).
// Team matches: ResultP1/ResultP2 are required and ResultScreenshot is a base64 data-url image.
public record SubmitPlayerResultRequest(
	string? ResultP1 = null,
	string? ResultP2 = null,
	string? ResultScreenshot = null,
	string? NexusResultUrl = null,
	string? NexusP1Side = null);

// Moderator-typed ResultP1/ResultP2 always win; a NexusResultUrl is stored with the match and,
// when NexusOptions.ParseResults is on, fills in whichever result the moderator left empty.
// ConfirmReset: the moderator has seen (from a 428 response) which later matches changing this
// match's winner will reset, and wants it done.
public record SetAdminResultRequest(
	string? ResultP1 = null,
	string? ResultP2 = null,
	string? ResultScreenshot = null,
	string? TechLossPlayerId = null,
	Guid? TechLossTeamId = null,
	string? NexusResultUrl = null,
	string? NexusP1Side = null,
	bool ConfirmReset = false);

public record NexusPreviewRequest(string Url, string? NexusP1Side = null);

// Side is "p1"/"p2" when the Nexus side mapping is known, otherwise "left"/"right".
public record NexusMissingDto(int Game, string Side);

// P1IsLeft is null when the Nexus names couldn't be matched to the two players — the client then
// asks which side is whose and calls again with NexusP1Side. ResultP1/ResultP2 are null until the
// sides are known or when no game has both values.
public record NexusPreviewDto(
	string NexusResultId,
	string LeftName,
	string RightName,
	bool? P1IsLeft,
	string? LeftTotal,
	string? RightTotal,
	string? ResultP1,
	string? ResultP2,
	int TotalGames,
	int CountedGames,
	bool IsTie,
	List<NexusMissingDto> Missing);

// Number..LoserToSlot place the match in the bracket (see TournamentMatch); all null for matches
// of tournaments that were run on Challonge.
public record TournamentMatchDto(
	Guid Id,
	Guid TournamentId,
	int? Number,
	BracketSection? Section,
	int? Round,
	int? Position,
	Guid? WinnerToMatchId,
	int? WinnerToSlot,
	Guid? LoserToMatchId,
	int? LoserToSlot,
	string? P1PlayerId,
	string? P2PlayerId,
	Guid? P1TeamId,
	Guid? P2TeamId,
	string State,
	string? WinnerPlayerId,
	Guid? WinnerTeamId,
	string? ResultScreenshotUrl,
	string? ResultP1,
	string? ResultP2,
	bool P1ApprovedResult,
	bool P2ApprovedResult,
	string? TechLossPlayerId,
	Guid? TechLossTeamId,
	string? NexusResultId = null,
	int? NexusTotalGames = null,
	int? NexusCountedGames = null,
	List<NexusMissingDto>? NexusMissing = null);
