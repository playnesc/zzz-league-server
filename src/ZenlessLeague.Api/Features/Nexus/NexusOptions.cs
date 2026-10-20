namespace ZenlessLeague.Api.Features.Nexus;

// "Nexus" config section. ParseResults = false (default) while Nexus has no stable API: solo
// results are typed by the players and the pasted link is only stored as proof (shown as an
// embed). true = the result is fetched and parsed from the link (NexusResultParser). Flip together
// with NEXUS_PARSING_ENABLED in the frontend's TournamentMatchPopup.
public class NexusOptions
{
	public bool ParseResults { get; set; }
}
