using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class PriceTrendTests
{
    [Fact]
    public void Null_Returns_Null()
    {
        Assert.Null(PriceTrend.Compute(null!, 1.0));
    }

    [Fact]
    public void TooFewSamples_Returns_Null()
    {
        Assert.Null(PriceTrend.Compute(Array.Empty<float>(), 1.0));
        Assert.Null(PriceTrend.Compute(new float[] { 1f }, 1.0));
        Assert.Null(PriceTrend.Compute(new float[] { 1f, 2f }, 1.0));
    }

    [Fact]
    public void NonPositiveSpacing_Returns_Null()
    {
        Assert.Null(PriceTrend.Compute(new float[] { 1f, 2f, 3f },  0.0));
        Assert.Null(PriceTrend.Compute(new float[] { 1f, 2f, 3f }, -1.0));
    }

    [Fact]
    public void FlatLine_HasZeroSlope()
    {
        var est = PriceTrend.Compute(new float[] { 5f, 5f, 5f, 5f }, 1.0);
        Assert.NotNull(est);
        Assert.Equal(0.0, est!.SlopePerMin, 6);
        Assert.Equal(5.0, est.FittedNow,    6);
        Assert.Equal(5f,  est.LastValue);
        Assert.Equal(4,   est.SampleCount);
    }

    [Fact]
    public void RisingLine_HasPositiveSlope_PerMinute()
    {
        // y = i, sampled once per second → +1/sec → +60/min.
        var est = PriceTrend.Compute(new float[] { 0f, 1f, 2f, 3f, 4f }, 1.0);
        Assert.NotNull(est);
        Assert.Equal(60.0, est!.SlopePerMin, 4);
    }

    [Fact]
    public void FallingLine_HasNegativeSlope_PerMinute()
    {
        // y = -i, sampled once per second → -1/sec → -60/min.
        var est = PriceTrend.Compute(new float[] { 4f, 3f, 2f, 1f, 0f }, 1.0);
        Assert.NotNull(est);
        Assert.Equal(-60.0, est!.SlopePerMin, 4);
    }

    [Fact]
    public void Spacing_ScalesSlope()
    {
        // Same y values but sampled every 2 s → slope halves.
        var est = PriceTrend.Compute(new float[] { 0f, 1f, 2f, 3f, 4f }, 2.0);
        Assert.NotNull(est);
        Assert.Equal(30.0, est!.SlopePerMin, 4);
    }

    [Fact]
    public void FittedNow_TracksRegressionLineAtLastSample()
    {
        // Perfectly linear: fitted-now should equal the last sample's y.
        var est = PriceTrend.Compute(new float[] { 10f, 11f, 12f, 13f }, 1.0);
        Assert.NotNull(est);
        Assert.Equal(13.0, est!.FittedNow, 4);
        Assert.Equal(13f,  est.LastValue);
    }

    [Fact]
    public void NoisyData_Yields_AveragedSlope()
    {
        // y = i + jitter; least-squares should flatten the noise to ~+1/sec.
        var est = PriceTrend.Compute(
            new float[] { 0.1f, 1.2f, 1.9f, 3.1f, 3.8f, 5.2f }, 1.0);
        Assert.NotNull(est);
        Assert.InRange(est!.SlopePerMin, 55.0, 65.0);
    }

    [Fact]
    public void Project_ExtrapolatesFromFittedNow()
    {
        // +1/sec line ending at fittedNow=4 → +5 min adds 5*60 = 300 units.
        var est = PriceTrend.Compute(new float[] { 0f, 1f, 2f, 3f, 4f }, 1.0);
        Assert.NotNull(est);
        Assert.Equal(304.0, PriceTrend.Project(est!, 5.0), 4);
    }

    [Fact]
    public void Project_NegativeMinutes_ProjectsBackwards()
    {
        var est = PriceTrend.Compute(new float[] { 0f, 1f, 2f, 3f, 4f }, 1.0);
        Assert.NotNull(est);
        // 1 min before fittedNow=4 with +60/min slope → 4 - 60 = -56.
        Assert.Equal(-56.0, PriceTrend.Project(est!, -1.0), 4);
    }

    [Fact]
    public void Project_NullEstimate_ReturnsZero()
    {
        Assert.Equal(0.0, PriceTrend.Project(null!, 1.0));
    }
}
