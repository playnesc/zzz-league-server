namespace ZenlessLeague.Api.Features.Nexus;

// "Nexus" config section. ParseResults = true (default): solo results are fetched from the pasted
// draft link via Nexus's playnesc integration endpoint (NexusResultParser). false = fallback for when
// that endpoint is down or changed: players type the result and the link is only stored as proof
// (shown as an embed). Flip together with NEXUS_PARSING_ENABLED in the frontend's TournamentMatchPopup.
public class NexusOptions
{
	public bool ParseResults { get; set; } = true;
}
