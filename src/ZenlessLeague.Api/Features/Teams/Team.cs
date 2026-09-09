namespace ZenlessLeague.Api.Features.Teams;

public class Team
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? PhotoUrl { get; set; }

    // Firebase player uid, not a Postgres FK — players still live in RTDB.
    public required string CreatorPlayerId { get; set; }
    public required string Player2PlayerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
