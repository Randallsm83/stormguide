using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class PerfRingTests
{
    [Fact]
    public void Empty_AllPercentilesReturnZero()
    {
        var ring = new PerfRing(capacity: 10);
        Assert.Equal(0, ring.Count);
        Assert.Equal(0, ring.P50);
        Assert.Equal(0, ring.P95);
        Assert.Equal(0, ring.Percentile(0));
        Assert.Equal(0, ring.Percentile(1));
    }

    [Fact]
    public void SingleSample_AllPercentilesReturnSample()
    {
        var ring = new PerfRing(capacity: 10);
        ring.Push(7.5);
        Assert.Equal(1, ring.Count);
        Assert.Equal(7.5, ring.P50);
        Assert.Equal(7.5, ring.P95);
    }

    [Fact]
    public void Push_DropsOldestWhenCapacityExceeded()
    {
        var ring = new PerfRing(capacity: 3);
        ring.Push(1); ring.Push(2); ring.Push(3);
        Assert.Equal(3, ring.Count);

        // Pushing a 4th sample drops the oldest (1). Percentile(0) becomes 2.
        ring.Push(4);
        Assert.Equal(3, ring.Count);
        Assert.Equal(2, ring.Percentile(0));   // smallest now
        Assert.Equal(4, ring.Percentile(1));   // largest now
    }

    [Fact]
    public void Percentile_OnSequentialDistribution_HitsExpectedRank()
    {
        // 1..100. Nearest-rank percentile: idx = floor((n-1) * p).
        // p=0.50 -> floor(99 * 0.5) = 49 -> arr[49] = 50
        // p=0.95 -> floor(99 * 0.95) = 94 -> arr[94] = 95
        var ring = new PerfRing(capacity: 200);
        for (var i = 1; i <= 100; i++) ring.Push(i);
        Assert.Equal(50, ring.P50);
        Assert.Equal(95, ring.P95);
    }

    [Fact]
    public void Percentile_OrdersIndependentOfPushOrder()
    {
        // Same multiset, different push order -> same percentile output.
        var ascending = new PerfRing(capacity: 5);
        var descending = new PerfRing(capacity: 5);
        foreach (var v in new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }) ascending.Push(v);
        foreach (var v in new[] { 5.0, 4.0, 3.0, 2.0, 1.0 }) descending.Push(v);
        Assert.Equal(ascending.P50, descending.P50);
        Assert.Equal(ascending.P95, descending.P95);
    }

    [Fact]
    public void Capacity_PropertyReflectsConstructor()
    {
        var ring = new PerfRing(capacity: 7);
        Assert.Equal(7, ring.Capacity);
    }

    [Fact]
    public void Constructor_RejectsZeroCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerfRing(0));
    }

    [Fact]
    public void Constructor_RejectsNegativeCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerfRing(-1));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Percentile_RejectsOutOfRange(double p)
    {
        var ring = new PerfRing(capacity: 5);
        ring.Push(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Percentile(p));
    }

    [Fact]
    public void Percentile_AtBoundaries_ReturnsMinAndMax()
    {
        var ring = new PerfRing(capacity: 5);
        foreach (var v in new[] { 5.0, 1.0, 3.0, 2.0, 4.0 }) ring.Push(v);
        Assert.Equal(1, ring.Percentile(0));   // p=0  -> floor(4*0) = 0  -> arr[0]
        Assert.Equal(5, ring.Percentile(1));   // p=1  -> floor(4*1) = 4  -> arr[4]
    }
}
