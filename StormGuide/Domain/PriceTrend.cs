namespace StormGuide.Domain;

/// <summary>
/// Pure linear-regression helper used by the Good tab's price-history chart
/// to extrapolate the within-visit price trend forward (e.g. "if the trend
/// holds, the price ~3 minutes from now is ~Y"). Lives in <c>Domain/</c> so
/// it's source-shared with the test project and stays game-free.
///
/// Inputs are the same per-visit float samples the UI already collects via
/// <c>SellValueAtCurrentTrader</c> on a once-per-second cadence; spacing is
/// passed in as <c>secondsBetweenSamples</c> rather than assumed so the
/// helper stays useful if the cadence is ever retuned.
///
/// Slope is reported in <c>currency / minute</c> for parity with the other
/// per-minute rates surfaced on the Good tab (flow, runway). The fitted
/// "now" value is the regression line evaluated at the last sample's x — a
/// noise-tolerant alternative to using the raw last sample as the projection
/// origin.
/// </summary>
public static class PriceTrend
{
    /// <summary>Outcome of a regression fit. <see cref="SampleCount"/> mirrors
    /// the input length so callers can decide whether the estimate is worth
    /// rendering.</summary>
    public sealed record Estimate(
        double SlopePerMin,
        double FittedNow,
        double LastValue,
        int    SampleCount);

    /// <summary>
    /// Fits a least-squares line over <paramref name="samples"/> (treated as
    /// y-values at x = 0, dx, 2·dx, ... where dx = <paramref name="secondsBetweenSamples"/>).
    /// Returns null when there are fewer than 3 samples, when spacing is
    /// non-positive, or when the regression denominator is degenerate.
    /// </summary>
    public static Estimate? Compute(
        IReadOnlyList<float> samples, double secondsBetweenSamples)
    {
        if (samples is null) return null;
        if (samples.Count < 3) return null;
        if (secondsBetweenSamples <= 0) return null;

        var n = samples.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            double x = i * secondsBetweenSamples;
            double y = samples[i];
            sumX  += x;
            sumY  += y;
            sumXY += x * y;
            sumXX += x * x;
        }
        var meanX = sumX / n;
        var meanY = sumY / n;
        var denom = sumXX - n * meanX * meanX;
        if (System.Math.Abs(denom) < 1e-9) return null;

        var slopePerSec = (sumXY - n * meanX * meanY) / denom;
        var intercept   = meanY - slopePerSec * meanX;
        var lastX       = (n - 1) * secondsBetweenSamples;
        var fittedNow   = intercept + slopePerSec * lastX;
        var slopePerMin = slopePerSec * 60.0;
        return new Estimate(slopePerMin, fittedNow, samples[n - 1], n);
    }

    /// <summary>
    /// Projects the regression line forward by <paramref name="minutesAhead"/>
    /// minutes from the fitted "now" value. Negative inputs project backwards
    /// (rarely useful but kept for symmetry).
    /// </summary>
    public static double Project(Estimate estimate, double minutesAhead)
    {
        if (estimate is null) return 0;
        return estimate.FittedNow + estimate.SlopePerMin * minutesAhead;
    }
}
