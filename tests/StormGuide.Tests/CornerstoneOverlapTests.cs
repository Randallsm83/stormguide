using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class CornerstoneOverlapTests
{
    private static (string DisplayName, IReadOnlyList<string> Tags) Owned(
        string display, params string[] tags) => (display, tags);

    [Fact]
    public void NullInputs_ReturnEmpty()
    {
        Assert.Empty(CornerstoneOverlap.Compute(null, null));
        Assert.Empty(CornerstoneOverlap.Compute(new[] { "Wood" }, null));
        Assert.Empty(CornerstoneOverlap.Compute(null,
            new[] { Owned("X", "Wood") }));
    }

    [Fact]
    public void EmptyOptionTags_ReturnEmpty()
    {
        var owned = new[] { Owned("X", "Wood") };
        Assert.Empty(CornerstoneOverlap.Compute(Array.Empty<string>(), owned));
    }

    [Fact]
    public void NoOwned_ReturnEmpty()
    {
        Assert.Empty(CornerstoneOverlap.Compute(
            new[] { "Wood" },
            Array.Empty<(string, IReadOnlyList<string>)>()));
    }

    [Fact]
    public void NoOverlap_ReturnEmpty()
    {
        var owned = new[] { Owned("Stone Sponsor", "Stone", "Tech") };
        Assert.Empty(CornerstoneOverlap.Compute(new[] { "Wood" }, owned));
    }

    [Fact]
    public void SingleOverlap_ReportsTag()
    {
        var owned = new[] { Owned("Lumberjack", "Wood", "Forest") };
        var result = CornerstoneOverlap.Compute(new[] { "Wood", "Cloth" }, owned);
        var row = Assert.Single(result);
        Assert.Equal("Lumberjack", row.OwnedDisplayName);
        Assert.Equal(new[] { "Wood" }, row.SharedTags);
    }

    [Fact]
    public void MultipleSharedTags_AllSurfaced()
    {
        var owned = new[] { Owned("Sawmill Boss", "Wood", "Forest", "Tech") };
        var result = CornerstoneOverlap.Compute(
            new[] { "Wood", "Tech", "Cloth" }, owned);
        var row = Assert.Single(result);
        Assert.Equal(new[] { "Wood", "Tech" }, row.SharedTags);
    }

    [Fact]
    public void MultipleOwned_OnlyOverlappingReturned()
    {
        var owned = new[]
        {
            Owned("Lumberjack",   "Wood",  "Forest"),
            Owned("Stone Sponsor", "Stone", "Tech"),
            Owned("Cloth Master",  "Cloth"),
        };
        var result = CornerstoneOverlap.Compute(new[] { "Wood", "Cloth" }, owned);
        Assert.Equal(2, result.Count);
        Assert.Equal("Lumberjack", result[0].OwnedDisplayName);
        Assert.Equal(new[] { "Wood" }, result[0].SharedTags);
        Assert.Equal("Cloth Master", result[1].OwnedDisplayName);
        Assert.Equal(new[] { "Cloth" }, result[1].SharedTags);
    }

    [Fact]
    public void CaseInsensitive_Matching()
    {
        var owned = new[] { Owned("Lumberjack", "wood", "FOREST") };
        var result = CornerstoneOverlap.Compute(new[] { "WOOD", "forest" }, owned);
        var row = Assert.Single(result);
        Assert.Equal(2, row.SharedTags.Count);
    }

    [Fact]
    public void DuplicateOwnedTags_Collapsed()
    {
        var owned = new[] { Owned("Lumberjack", "Wood", "wood", "Wood") };
        var result = CornerstoneOverlap.Compute(new[] { "Wood" }, owned);
        var row = Assert.Single(result);
        Assert.Single(row.SharedTags);
    }

    [Fact]
    public void EmptyAndNullTagEntries_Skipped()
    {
        var owned = new[] { Owned("Lumberjack", "", "Wood", null!) };
        var result = CornerstoneOverlap.Compute(
            new string?[] { "Wood", "", null! }!, owned);
        var row = Assert.Single(result);
        Assert.Equal(new[] { "Wood" }, row.SharedTags);
    }

    [Fact]
    public void EmptyDisplayName_OwnedSkipped()
    {
        var owned = new[] { Owned("", "Wood") };
        Assert.Empty(CornerstoneOverlap.Compute(new[] { "Wood" }, owned));
    }

    [Fact]
    public void OwnedWithNullTagsList_Skipped()
    {
        var owned = new[]
        {
            ("NullTags", (IReadOnlyList<string>)null!),
            ("RealOne",  (IReadOnlyList<string>)new[] { "Wood" }),
        };
        var result = CornerstoneOverlap.Compute(new[] { "Wood" }, owned);
        var row = Assert.Single(result);
        Assert.Equal("RealOne", row.OwnedDisplayName);
    }
}
