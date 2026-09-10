namespace ZenlessLeague.Api.Features.Players;

// Direct port of functions/utils/tierConfirmation.js. Hysteresis (distinct promote/demote
// thresholds) means a player's tier flags don't flap back and forth around a single cutoff.
public static class TierConfirmationCalculator
{
	private const int MidTierPromoteElo = 1200;
	private const int MidTierDemoteElo = 1150;
	private const int HighTierPromoteElo = 1400;
	private const int HighTierDemoteElo = 1350;

	public static (bool IsMidConfirmed, bool IsHighConfirmed) Compute(bool isMidConfirmed, bool isHighConfirmed, int elo)
	{
		if (isMidConfirmed && elo < MidTierDemoteElo)
		{
			isMidConfirmed = false;
		}

		if (!isMidConfirmed && elo >= MidTierPromoteElo)
		{
			isMidConfirmed = true;
		}

		if (isHighConfirmed && elo < HighTierDemoteElo)
		{
			isHighConfirmed = false;
		}

		if (!isHighConfirmed && elo >= HighTierPromoteElo)
		{
			isHighConfirmed = true;
		}

		return (isMidConfirmed, isHighConfirmed);
	}
}
