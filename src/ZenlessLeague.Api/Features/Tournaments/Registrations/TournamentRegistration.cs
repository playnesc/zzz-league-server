namespace ZenlessLeague.Api.Features.Tournaments;

public class TournamentRegistration
{
    public Guid Id { get; set; }
    public Guid TournamentId { get; set; }
    public TournamentRegistrationType Type { get; set; }

    public string? PlayerId { get; set; } // Firebase uid — Solo only
    public Guid? TeamId { get; set; } // Postgres Team id — Team only

    public PlayerRegistrationDetails Player1Details { get; set; } = null!;
    public PlayerRegistrationDetails? Player2Details { get; set; } // Team only

    public bool Approved { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
