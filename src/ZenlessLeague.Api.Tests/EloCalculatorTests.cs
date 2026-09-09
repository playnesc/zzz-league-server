using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Tests;

public class EloCalculatorTests
{
    private static EloCalculator.RatingInfo Rating(int elo, int points = 0, bool midConfirmed = false) => new(elo, points, midConfirmed);

    [Fact]
    public void EqualRatings_UnconfirmedWinner_Gains50PercentOfK50()
    {
        // expected = 0.5, k = 50 (win, unconfirmed) -> round(50 * (1 - 0.5)) = 25
        var change = EloCalculator.CalculateChange(Rating(1000), Rating(1000), win: true);
        Assert.Equal(25, change);
    }

    [Fact]
    public void EqualRatings_UnconfirmedLoser_Loses50PercentOfK45()
    {
        // expected = 0.5, k = 45 (loss, unconfirmed) -> round(45 * (0 - 0.5)) = -23 via JS Math.round(-22.5) = -22
        var change = EloCalculator.CalculateChange(Rating(1000), Rating(1000), win: false);
        Assert.Equal(-22, change);
    }

    [Fact]
    public void HeavyFavoriteWinner_StillGainsAtLeastOne()
    {
        // expected win probability for the favorite is close to 1, so raw change rounds to <=0 — clamped to +1.
        var change = EloCalculator.CalculateChange(Rating(2000), Rating(1000), win: true);
        Assert.Equal(1, change);
    }

    [Fact]
    public void HeavyUnderdogLoser_StillLosesAtLeastOne()
    {
        var change = EloCalculator.CalculateChange(Rating(1000), Rating(2000), win: false);
        Assert.Equal(-1, change);
    }

    [Fact]
    public void MidConfirmedWinner_UsesLowerKFactor()
    {
        // expected = 0.5, k = 25 (win, confirmed) -> round(25 * 0.5) = 13 (JS Math.round(12.5) = 13)
        var change = EloCalculator.CalculateChange(Rating(1000, midConfirmed: true), Rating(1000), win: true);
        Assert.Equal(13, change);
    }

    [Fact]
    public void EffectiveRating_IncludesTournamentPoints()
    {
        Assert.Equal(1050, EloCalculator.EffectiveRating(Rating(1000, points: 50)));
    }
}
