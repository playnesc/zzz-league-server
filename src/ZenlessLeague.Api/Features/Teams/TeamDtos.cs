using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Teams;

public record CreateTeamRequest(string CreatorPlayerId, string Player2PlayerId, string Name, string? PhotoUrl);

public record EditTeamRequest(string Name, string? PhotoUrl, string Player2PlayerId);

public record TeamDto(Guid Id, string Name, string? PhotoUrl, PlayerSummary Creator, PlayerSummary Player2, DateTimeOffset CreatedAt);
