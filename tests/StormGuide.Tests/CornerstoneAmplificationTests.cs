using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class CornerstoneAmplificationTests
{
    [Fact]
    public void Empty_Returns_EmptySet()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(
            Array.Empty<(string, string)>());
        Assert.Empty(result);
    }

    [Fact]
    public void Null_Input_Returns_EmptySet()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void EmptyTuple_IsIgnored()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(
            new[] { ("", ""), (" ", " ") });
        Assert.Empty(result);
    }

    [Fact]
    public void ReputationKeywords_MapTo_Reputation()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Famous Sponsor", "Reputation rewards from orders are doubled."),
        });
        Assert.Contains("reputation", result);
        Assert.Single(result);
    }

    [Fact]
    public void CornerstoneKeywords_MapTo_Cornerstone()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Architect's Plan", "Each cornerstone you pick grants extra blueprint options."),
        });
        Assert.Contains("cornerstone", result);
    }

    [Fact]
    public void ResolveKeywords_MapTo_Resolve()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Hearth Cheer", "All villagers gain +5 morale during storms."),
        });
        Assert.Contains("resolve", result);
    }

    [Fact]
    public void GoodsKeywords_MapTo_Goods()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Stockpiler", "Increase production output by 20%."),
        });
        Assert.Contains("goods", result);
    }

    [Fact]
    public void Mixed_Cornerstones_AggregateMultipleCategories()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Reputation Boost",   "Reputation from orders gains 10%."),
            ("Cheerful Hearth",    "Villagers' resolve grows faster."),
            ("Trade Goods Surge",  "Trade goods produced are doubled."),
        });
        Assert.Contains("reputation", result);
        Assert.Contains("resolve",    result);
        Assert.Contains("goods",      result);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void CaseInsensitive_Match()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Loud Hailer", "REPUTATION points double on completion."),
        });
        Assert.Contains("reputation", result);
    }

    [Fact]
    public void DescriptionAlone_TriggersMatch_WhenNameIsBlank()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("", "+10% yield from every workshop."),
        });
        Assert.Contains("goods", result);
    }

    [Fact]
    public void NameAlone_TriggersMatch_WhenDescriptionIsBlank()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Cornerstone Collector", ""),
        });
        Assert.Contains("cornerstone", result);
    }

    [Fact]
    public void NoKeywords_ReturnsEmpty()
    {
        var result = CornerstoneAmplification.AmplifiedCategoriesFrom(new[]
        {
            ("Calm Wanderer", "A peaceful traveller passes through your settlement."),
        });
        Assert.Empty(result);
    }
}
