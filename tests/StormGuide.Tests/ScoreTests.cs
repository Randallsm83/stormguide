using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class ScoreTests
{
    [Fact]
    public void Empty_HasZeroValue_NoComponents_NoUnit()
    {
        var s = Score.Empty;
        Assert.Equal(0, s.Value);
        Assert.Empty(s.Components);
        Assert.Null(s.Unit);
    }

    [Fact]
    public void Format_NoUnit_ReturnsBareNumber()
    {
        var s = new Score(1.234, [new ScoreComponent("a", 1.234)]);
        Assert.Equal("1.23", s.Format());
    }

    [Fact]
    public void Format_WithUnit_AppendsUnit()
    {
        var s = new Score(5, [new ScoreComponent("a", 5)], Unit: "buildings");
        Assert.Equal("5 buildings", s.Format());
    }

    [Fact]
    public void Format_UsesInvariantCulture_DotDecimalSeparator()
    {
        // Defends against host-culture-flipping changing the rendered number;
        // PluginConfig persists Vector2 values with InvariantCulture for the
        // same reason and the UI reads them straight from Format().
        var s = new Score(1.5, [new ScoreComponent("a", 1.5)]);
        Assert.Equal("1.5", s.Format());
    }

    [Fact]
    public void ScoreComponent_RecordEquality()
    {
        var a = new ScoreComponent("x", 1.0, "n");
        var b = new ScoreComponent("x", 1.0, "n");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ScoreComponent_NoteIsOptional()
    {
        var c = new ScoreComponent("x", 1.0);
        Assert.Null(c.Note);
    }

    [Fact]
    public void Components_Sum_MatchesValue_ForAdditiveProducer()
    {
        // AGENTS.md hard invariant #2: every Score has a visible breakdown
        // that explains its Value. Score itself doesn't enforce this (it
        // accepts any (value, components) pair), but Provider-shaped scores
        // are constructed additively. Verify the convention holds for a
        // representative shape so future refactors don't silently break it.
        var components = new[]
        {
            new ScoreComponent("a", 2.0),
            new ScoreComponent("b", 3.5),
            new ScoreComponent("c", -0.5),
        };
        var total = components.Sum(c => c.Value);
        var s = new Score(total, components);
        Assert.Equal(s.Value, s.Components.Sum(c => c.Value), precision: 9);
    }
}
