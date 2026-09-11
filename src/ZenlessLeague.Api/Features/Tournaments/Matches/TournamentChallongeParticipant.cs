namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public class TournamentChallongeParticipant
{
	public Guid TournamentId { get; set; }
	public required string ChallongeParticipantId { get; set; }
	public Guid? PlayerId { get; set; }
	public Guid? TeamId { get; set; }
}
