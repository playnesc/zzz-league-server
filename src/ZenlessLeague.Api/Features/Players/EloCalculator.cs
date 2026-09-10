namespace ZenlessLeague.Api.Features.Players;

// Direct port of functions/utils/registerMatchResult.js's calculateEloChange/effectiveRating.
// Pure — the caller (Features/History, added in a later phase) is responsible for persisting
// the result.
public static class EloCalculator
{
	public readonly record struct RatingInfo(int Elo, int TournamentPoints, bool IsMidConfirmed);

	public static int EffectiveRating(RatingInfo player) => player.Elo + player.TournamentPoints;

	// K-factor logistic Elo: K depends on win/loss and mid-tier-confirmed status, and the
	// result is clamped to at least +-1 so a win always gains something and a loss always
	// costs something, even against a much weaker/stronger opponent.
	public static int CalculateChange(RatingInfo player, RatingInfo opponent, bool win)
	{
		var k = win
			? (player.IsMidConfirmed ? 25 : 50)
			: (player.IsMidConfirmed ? 20 : 45);

		var expected = 1.0 / (1.0 + Math.Pow(10, (EffectiveRating(opponent) - EffectiveRating(player)) / 400.0));
		var change = JsRound(k * ((win ? 1.0 : 0.0) - expected));

		if (win && change <= 0)
		{
			change = 1;
		}

		if (!win && change >= 0)
		{
			change = -1;
		}

		return change;
	}

	// JS's Math.round rounds half-way values toward +Infinity (e.g. -2.5 -> -2), unlike
	// C#'s Math.Round default banker's rounding — replicate the JS behavior exactly.
	private static int JsRound(double value) => (int)Math.Floor(value + 0.5);
}
