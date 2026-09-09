using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Tests;

public class TierConfirmationCalculatorTests
{
    [Fact]
    public void PromotesToMid_AtThreshold()
    {
        var (mid, high) = TierConfirmationCalculator.Compute(isMidConfirmed: false, isHighConfirmed: false, elo: 1200);
        Assert.True(mid);
        Assert.False(high);
    }

    [Fact]
    public void DoesNotPromoteToMid_BelowThreshold()
    {
        var (mid, _) = TierConfirmationCalculator.Compute(isMidConfirmed: false, isHighConfirmed: false, elo: 1199);
        Assert.False(mid);
    }

    [Fact]
    public void HysteresisKeepsMidConfirmed_InTheDeadZone()
    {
        // Between the demote (1150) and promote (1200) thresholds, an already-confirmed
        // player stays confirmed even though a never-confirmed player wouldn't promote yet.
        var (mid, _) = TierConfirmationCalculator.Compute(isMidConfirmed: true, isHighConfirmed: false, elo: 1175);
        Assert.True(mid);
    }

    [Fact]
    public void DemotesFromMid_BelowDemoteThreshold()
    {
        var (mid, _) = TierConfirmationCalculator.Compute(isMidConfirmed: true, isHighConfirmed: false, elo: 1149);
        Assert.False(mid);
    }

    [Fact]
    public void PromotesToHigh_AtThreshold()
    {
        var (mid, high) = TierConfirmationCalculator.Compute(isMidConfirmed: true, isHighConfirmed: false, elo: 1400);
        Assert.True(mid);
        Assert.True(high);
    }

    [Fact]
    public void DemotesFromHigh_BelowDemoteThreshold()
    {
        var (_, high) = TierConfirmationCalculator.Compute(isMidConfirmed: true, isHighConfirmed: true, elo: 1349);
        Assert.False(high);
    }
}
