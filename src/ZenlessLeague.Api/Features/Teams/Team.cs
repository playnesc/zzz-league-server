namespace ZenlessLeague.Api.Features.Teams;

public class Team
{
	public Guid Id { get; set; }
	public required string Name { get; set; }
	public string? PhotoUrl { get; set; }

	// FK into Players.Id (see AppDbContext's HasForeignKey config). Player1Id is the team's
	// creator — the name change from CreatorPlayerId matches Player1Details/Player2Details'
	// existing convention on TournamentRegistration.
	public Guid Player1Id { get; set; }
	public Guid Player2Id { get; set; }

	public DateTimeOffset CreatedAt { get; set; }
}
