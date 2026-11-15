namespace ZenlessLeague.Api.Features.Surfers;

// A player's best Anime Surfers run — one row per player, replaced only when beaten. The game keeps
// its own local best too; this is the copy the public leaderboard reads.
public class SurfersScore
{
	public Guid PlayerId { get; set; }
	public int Score { get; set; }
	// How long the best run took by the game's own clock — kept so implausible scores can be looked at later.
	public double DurationSeconds { get; set; }
	public DateTimeOffset AchievedAt { get; set; }
	// Postgres xmin: two submits racing on the same row surface as 409 instead of a lower score winning.
	public uint Version { get; set; }
}
