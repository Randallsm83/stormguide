using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class DietaryVarietyTests
{
    [Fact]
    public void NullNeeds_ReturnsNull()
    {
        Assert.Null(DietaryVariety.Compute(null, _ => 0));
    }

    [Fact]
    public void NullLookup_ReturnsNull()
    {
        Assert.Null(DietaryVariety.Compute(new[] { "berries" }, null));
    }

    [Fact]
    public void EmptyNeeds_ReturnsNull()
    {
        Assert.Null(DietaryVariety.Compute(System.Array.Empty<string>(), _ => 5));
    }

    [Fact]
    public void OnlyWhitespaceNeeds_ReturnsNull()
    {
        Assert.Null(DietaryVariety.Compute(new[] { "", "   ", "\t" }, _ => 5));
    }

    [Fact]
    public void AllSupplied_ReturnsHundredPercent()
    {
        var r = DietaryVariety.Compute(
            new[] { "berries", "biscuits", "ale" },
            _ => 12);
        Assert.NotNull(r);
        Assert.Equal(3, r!.TotalNeeds);
        Assert.Equal(3, r.SuppliedCount);
        Assert.Equal(100, r.ScorePercent);
        Assert.Empty(r.MissingNeeds);
    }

    [Fact]
    public void NoneSupplied_ReturnsZeroPercent()
    {
        var r = DietaryVariety.Compute(
            new[] { "berries", "biscuits", "ale" },
            _ => 0);
        Assert.NotNull(r);
        Assert.Equal(0, r!.SuppliedCount);
        Assert.Equal(0, r.ScorePercent);
        Assert.Equal(new[] { "berries", "biscuits", "ale" }, r.MissingNeeds);
    }

    [Fact]
    public void PartialSupply_ScoresProportionally()
    {
        // 2 of 4 supplied → 50%
        var stock = new System.Collections.Generic.Dictionary<string, int>
        {
            ["berries"]  = 10,
            ["biscuits"] = 0,
            ["ale"]      = 3,
            ["jerky"]    = 0,
        };
        var r = DietaryVariety.Compute(
            new[] { "berries", "biscuits", "ale", "jerky" },
            n => stock.TryGetValue(n, out var v) ? v : 0);
        Assert.NotNull(r);
        Assert.Equal(4, r!.TotalNeeds);
        Assert.Equal(2, r.SuppliedCount);
        Assert.Equal(50, r.ScorePercent);
        Assert.Equal(new[] { "biscuits", "jerky" }, r.MissingNeeds);
    }

    [Fact]
    public void RoundingIsAwayFromZero()
    {
        // 1 of 3 supplied → 33.33% rounds to 33; 2 of 3 → 66.67% rounds to 67.
        var stock1 = new System.Collections.Generic.Dictionary<string, int>
            { ["a"] = 1, ["b"] = 0, ["c"] = 0 };
        var r1 = DietaryVariety.Compute(
            new[] { "a", "b", "c" },
            n => stock1[n]);
        Assert.NotNull(r1);
        Assert.Equal(33, r1!.ScorePercent);

        var stock2 = new System.Collections.Generic.Dictionary<string, int>
            { ["a"] = 1, ["b"] = 1, ["c"] = 0 };
        var r2 = DietaryVariety.Compute(
            new[] { "a", "b", "c" },
            n => stock2[n]);
        Assert.NotNull(r2);
        Assert.Equal(67, r2!.ScorePercent);
    }

    [Fact]
    public void WhitespaceEntriesSkippedDoNotDilute()
    {
        // Two whitespace entries shouldn't drag down a 1/1 supplied score.
        var stock = new System.Collections.Generic.Dictionary<string, int>
            { ["berries"] = 5 };
        var r = DietaryVariety.Compute(
            new[] { "berries", "", "  " },
            n => stock.TryGetValue(n, out var v) ? v : 0);
        Assert.NotNull(r);
        Assert.Equal(1, r!.TotalNeeds);
        Assert.Equal(1, r.SuppliedCount);
        Assert.Equal(100, r.ScorePercent);
        Assert.Empty(r.MissingNeeds);
    }

    [Fact]
    public void ThrowingLookupIsTreatedAsZeroStock()
    {
        // A throw inside the lookup must not propagate \u2014 it counts as 0.
        var r = DietaryVariety.Compute(
            new[] { "berries", "biscuits" },
            n => n == "berries"
                ? throw new System.InvalidOperationException("boom")
                : 4);
        Assert.NotNull(r);
        Assert.Equal(2, r!.TotalNeeds);
        Assert.Equal(1, r.SuppliedCount);
        Assert.Equal(50, r.ScorePercent);
        Assert.Equal(new[] { "berries" }, r.MissingNeeds);
    }

    [Fact]
    public void NegativeStockCountsAsMissing()
    {
        // Defensive: a negative stockpile (shouldn't happen in-game) must
        // not be treated as supplied.
        var r = DietaryVariety.Compute(
            new[] { "a", "b" },
            n => n == "a" ? -3 : 7);
        Assert.NotNull(r);
        Assert.Equal(1, r!.SuppliedCount);
        Assert.Equal(50, r.ScorePercent);
        Assert.Equal(new[] { "a" }, r.MissingNeeds);
    }

    [Fact]
    public void MissingNeedsPreserveInputOrder()
    {
        var stock = new System.Collections.Generic.Dictionary<string, int>
        {
            ["a"] = 0,
            ["b"] = 5,
            ["c"] = 0,
            ["d"] = 0,
        };
        var r = DietaryVariety.Compute(
            new[] { "a", "b", "c", "d" },
            n => stock[n]);
        Assert.NotNull(r);
        Assert.Equal(new[] { "a", "c", "d" }, r!.MissingNeeds);
    }
}
