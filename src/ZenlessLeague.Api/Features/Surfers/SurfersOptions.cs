namespace ZenlessLeague.Api.Features.Surfers;

// "Surfers" config section. Scores come from the browser, so these only stop obviously forged ones,
// not a careful cheater. The game scores 1 point per meter: max speed 22 m/s x 1.4 nitro x 2 score
// multiplier is about 62 points/s, so the default leaves headroom for tuning before it rejects
// anything real.
public class SurfersOptions
{
	public double MaxScorePerSecond { get; set; } = 100;
	public double MaxRunSeconds { get; set; } = TimeSpan.FromHours(4).TotalSeconds;
}
