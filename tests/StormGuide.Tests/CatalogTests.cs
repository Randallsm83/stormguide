using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class CatalogTests
{
    [Fact]
    public void Empty_IsActuallyEmpty()
    {
        Assert.True(Catalog.Empty.IsEmpty);
    }

    [Fact]
    public void NonEmpty_IsNotEmpty()
    {
        var cat = new Catalog
        {
            Goods = new Dictionary<string, GoodInfo>
            {
                ["Wood"] = MakeGood("Wood"),
            },
        };
        Assert.False(cat.IsEmpty);
    }

    [Fact]
    public void RecipesProducing_FiltersByProducedGood()
    {
        var cat = MakeCatalog();
        var producers = cat.RecipesProducing("Planks").ToList();
        Assert.Single(producers);
        Assert.Equal("PlanksRecipe", producers[0].Name);
    }

    [Fact]
    public void RecipesProducing_ReturnsEmpty_ForUnknownGood()
    {
        var cat = MakeCatalog();
        Assert.Empty(cat.RecipesProducing("NotARealGood"));
    }

    [Fact]
    public void RecipesConsuming_FindsAcrossSlotsAndOptions()
    {
        var cat = MakeCatalog();
        // PlanksRecipe directly consumes Wood. BiscuitRecipe consumes "Wood OR
        // Coal" in its fuel slot. Both must surface as Wood-consumers; the
        // method must walk every option in every slot, not just the first.
        var consumers = cat.RecipesConsuming("Wood").Select(r => r.Name).ToHashSet();
        Assert.Contains("PlanksRecipe",  consumers);
        Assert.Contains("BiscuitRecipe", consumers);
        Assert.Equal(2, consumers.Count);
    }

    [Fact]
    public void RecipesConsuming_ReturnsEmpty_ForGoodNobodyUses()
    {
        var cat = MakeCatalog();
        Assert.Empty(cat.RecipesConsuming("Tools"));
    }

    [Fact]
    public void RacesNeeding_FiltersByNeed()
    {
        var cat = MakeCatalog();
        var needers = cat.RacesNeeding("Tools").Select(r => r.Name).ToList();
        Assert.Single(needers);
        Assert.Equal("Beaver", needers[0]);
    }

    [Fact]
    public void RacesNeeding_ReturnsEmpty_ForUnneededGood()
    {
        var cat = MakeCatalog();
        Assert.Empty(cat.RacesNeeding("Caviar"));
    }

    private static GoodInfo MakeGood(string name) => new(
        Name: name, DisplayName: name, Category: "x",
        IsEatable: false, EatingFullness: 0,
        CanBeBurned: false, BurningTime: 0,
        TradingBuyValue: 0, TradingSellValue: 0,
        TradersBuying: [], TradersSelling: [], Tags: []);

    private static RecipeInfo MakeRecipe(string name, string produced, params RecipeInputSlot[] inputs) => new(
        Name: name, DisplayName: name, Grade: "I",
        ProducedGood: produced, ProducedAmount: 1,
        ProductionTime: 60,
        Tags: [], RequiredGoods: inputs, Buildings: []);

    private static RaceInfo MakeRace(string name, params string[] needs) => new(
        Name: name, DisplayName: name,
        BaseSpeed: 1, InitialResolve: 0, MinResolve: -100, MaxResolve: 100,
        ResolvePositiveChangePerSec: 0, ResolveNegativeChangePerSec: 0,
        HungerTolerance: 0, Needs: needs, Characteristics: []);

    private static Catalog MakeCatalog() => new()
    {
        Goods = new Dictionary<string, GoodInfo>
        {
            ["Wood"]   = MakeGood("Wood"),
            ["Planks"] = MakeGood("Planks"),
            ["Coal"]   = MakeGood("Coal"),
            ["Tools"]  = MakeGood("Tools"),
        },
        Recipes = new Dictionary<string, RecipeInfo>
        {
            ["PlanksRecipe"]  = MakeRecipe("PlanksRecipe", "Planks",
                new RecipeInputSlot([new GoodAmount("Wood", 1)])),
            ["BiscuitRecipe"] = MakeRecipe("BiscuitRecipe", "Biscuits",
                new RecipeInputSlot([new GoodAmount("Flour", 1)]),
                new RecipeInputSlot([new GoodAmount("Wood", 1), new GoodAmount("Coal", 1)])),
        },
        Races = new Dictionary<string, RaceInfo>
        {
            ["Beaver"] = MakeRace("Beaver", "Wood", "Tools"),
            ["Human"]  = MakeRace("Human",  "Bread"),
        },
    };
}
