namespace ZenlessLeague.Api.Features.Tournaments;

public class TournamentRegistration
{
	public Guid Id { get; set; }
	public Guid TournamentId { get; set; }

	// No stored Type field — Solo vs Team is 100% derivable from which of these two is set
	// (enforced XOR via the filtered unique indexes in AppDbContext) and is computed at the
	// DTO-mapping layer instead of duplicated here.
	public Guid? PlayerId { get; set; } // Solo only
	public Guid? TeamId { get; set; } // Team only

	public PlayerRegistrationDetails Player1Details { get; set; } = null!;
	public PlayerRegistrationDetails? Player2Details { get; set; } // Team only

	public bool Approved { get; set; }
	public DateTimeOffset RegisteredAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
}
