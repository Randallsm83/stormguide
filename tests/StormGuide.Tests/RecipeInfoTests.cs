using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class RecipeInfoTests
{
    [Fact]
    public void BaseGoodsPerSec_DivByProductionTime()
    {
        var r = MakeRecipe(producedAmount: 2, productionTime: 60);
        Assert.Equal(2.0 / 60.0, r.BaseGoodsPerSec, precision: 9);
    }

    [Fact]
    public void BaseGoodsPerSec_ZeroWhenNoProductionTime()
    {
        // Defensive: a malformed/zero-time recipe must not divide-by-zero. The
        // Provider layer relies on this returning 0 to flag the recipe rather
        // than producing Infinity in the UI.
        var r = MakeRecipe(producedAmount: 2, productionTime: 0);
        Assert.Equal(0, r.BaseGoodsPerSec);
    }

    [Fact]
    public void BaseGoodsPerSec_ZeroWhenNegativeProductionTime()
    {
        var r = MakeRecipe(producedAmount: 2, productionTime: -1);
        Assert.Equal(0, r.BaseGoodsPerSec);
    }

    [Fact]
    public void BaseGoodsPerSec_ScalesLinearlyWithProducedAmount()
    {
        var single = MakeRecipe(producedAmount: 1, productionTime: 30);
        var triple = MakeRecipe(producedAmount: 3, productionTime: 30);
        Assert.Equal(triple.BaseGoodsPerSec, single.BaseGoodsPerSec * 3, precision: 9);
    }

    private static RecipeInfo MakeRecipe(int producedAmount, double productionTime) => new(
        Name: "x", DisplayName: "x", Grade: "I",
        ProducedGood: "g",
        ProducedAmount: producedAmount,
        ProductionTime: productionTime,
        Tags: [], RequiredGoods: [], Buildings: []);
}
