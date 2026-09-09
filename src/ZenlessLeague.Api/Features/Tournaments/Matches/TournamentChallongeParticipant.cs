namespace ZenlessLeague.Api.Features.Tournaments.Matches;

// Legacy `tournament.challongeParticipants` map (Challonge participant id -> uid), modeled as
// its own table instead of a JSON blob so both lookup directions are queryable — uid -> challonge
// id is needed when reporting a match result back to Challonge; challonge id -> uid is needed
// when syncing match state back from Challonge (functions/utils/updateTournamentGames.js).
public class TournamentChallongeParticipant
{
    public Guid TournamentId { get; set; }
    public required string ChallongeParticipantId { get; set; }
    public required string PlayerId { get; set; }
}
