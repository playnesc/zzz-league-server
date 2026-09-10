namespace ZenlessLeague.Api.Features.Seasons;

// Singleton row (fixed Id) holding the admin-set "league ends at" countdown shown on the home
// page. Mirrors the legacy RTDB `timer` value — purely informational display, no server logic
// keys off it (the actual season rollover is the separate, manually-triggered ResetSeasonAsync).
public class SeasonTimer
{
	public const int SingletonId = 1;
	public int Id { get; set; } = SingletonId;
	public DateTimeOffset? EndTimestamp { get; set; }
}
