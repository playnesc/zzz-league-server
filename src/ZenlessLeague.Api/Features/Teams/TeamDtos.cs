using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Teams;

// Photo is a base64 data-url image, uploaded server-side (same contract as registration/match
// screenshots) — omit to leave the team without a logo (create) or keep the existing one (edit).
public record CreateTeamRequest(string CreatorPlayerId, string Player2PlayerId, string Name, string? Photo);

public record EditTeamRequest(string Name, string? Photo, string Player2PlayerId);

public record TeamDto(Guid Id, string Name, string? PhotoUrl, PlayerSummary Creator, PlayerSummary Player2, DateTimeOffset CreatedAt);

public record TeamPageDto(List<TeamDto> Teams, bool HasMore);
