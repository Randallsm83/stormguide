using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class GladeClearTimeTests
{
    [Fact]
    public void ZeroRemaining_ReturnsNull()
    {
        Assert.Null(GladeClearTime.Estimate(0, scoutCount: 3));
    }

    [Fact]
    public void NegativeRemaining_ReturnsNull()
    {
        Assert.Null(GladeClearTime.Estimate(-2, scoutCount: 3));
    }

    [Fact]
    public void ZeroScouts_ReturnsNull()
    {
        Assert.Null(GladeClearTime.Estimate(remainingGlades: 5, scoutCount: 0));
    }

    [Fact]
    public void NegativeScouts_ReturnsNull()
    {
        Assert.Null(GladeClearTime.Estimate(remainingGlades: 5, scoutCount: -1));
    }

    [Fact]
    public void NonPositiveSeconds_ReturnsNull()
    {
        Assert.Null(GladeClearTime.Estimate(5, 2, secondsPerScoutPerGlade: 0));
        Assert.Null(GladeClearTime.Estimate(5, 2, secondsPerScoutPerGlade: -10));
    }

    [Fact]
    public void NormalCase_UsesDefaultRate()
    {
        // 10 glades × 90s / 2 scouts = 450s = 7.5 min
        var est = GladeClearTime.Estimate(10, 2);
        Assert.NotNull(est);
        Assert.Equal(10, est!.RemainingGlades);
        Assert.Equal(2, est.ScoutCount);
        Assert.Equal(GladeClearTime.DefaultSecondsPerScoutPerGlade, est.SecondsPerScoutPerGlade);
        Assert.Equal(7.5, est.MinutesToClear, 4);
    }

    [Fact]
    public void OneScoutPerGlade_TakesOneRateUnit()
    {
        // 1 glade / 1 scout @ 90s = 90s = 1.5 min
        var est = GladeClearTime.Estimate(1, 1);
        Assert.NotNull(est);
        Assert.Equal(1.5, est!.MinutesToClear, 4);
    }

    [Fact]
    public void ManyScouts_ScalesDown()
    {
        // 10 / 10 @ 90s = 90s = 1.5 min
        var est = GladeClearTime.Estimate(10, 10);
        Assert.NotNull(est);
        Assert.Equal(1.5, est!.MinutesToClear, 4);
    }

    [Fact]
    public void CustomSecondsParameter_Honoured()
    {
        // 6 glades * 60s / 3 scouts = 120s = 2 min
        var est = GladeClearTime.Estimate(6, 3, secondsPerScoutPerGlade: 60);
        Assert.NotNull(est);
        Assert.Equal(60, est!.SecondsPerScoutPerGlade);
        Assert.Equal(2.0, est.MinutesToClear, 4);
    }

    [Fact]
    public void LargeNumbers_DoNotOverflow()
    {
        // 100 glades × 30s / 1 scout = 3000s = 50 min
        var est = GladeClearTime.Estimate(100, 1, secondsPerScoutPerGlade: 30);
        Assert.NotNull(est);
        Assert.Equal(50.0, est!.MinutesToClear, 4);
    }

    [Fact]
    public void DefaultRateConstant_Is90Seconds()
    {
        Assert.Equal(90.0, GladeClearTime.DefaultSecondsPerScoutPerGlade);
    }
}
