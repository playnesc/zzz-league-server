using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Teams;

// Photo is a base64 data-url image, uploaded server-side (same contract as registration/match
// screenshots) — omit to leave the team without a logo (create) or keep the existing one (edit).
public record CreateTeamRequest(string CreatorPlayerId, string Player2PlayerId, string Name, string? Photo);

// The second player is fixed at creation — a team's roster can't be reassigned afterward, only
// its name/logo, so an edit only ever touches those two fields.
public record EditTeamRequest(string Name, string? Photo);

public record TeamDto(Guid Id, string Name, string? PhotoUrl, PlayerSummary Creator, PlayerSummary Player2, DateTimeOffset CreatedAt);

public record TeamPageDto(List<TeamDto> Teams, bool HasMore);
