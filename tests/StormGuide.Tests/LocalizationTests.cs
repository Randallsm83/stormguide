using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

/// <summary>
/// Covers the <see cref="Localization"/> resolution chain:
/// LiveLookup → catalog DisplayName → raw model key.
///
/// <see cref="Localization.LiveLookup"/> is a static delegate, so each test
/// resets it via <see cref="IDisposable.Dispose"/> to keep the suite order-
/// independent. xUnit constructs a fresh instance per test, so Dispose runs
/// after every Fact regardless of pass/fail.
/// </summary>
public class LocalizationTests : IDisposable
{
    public LocalizationTests() => Localization.LiveLookup = null;
    public void Dispose()      => Localization.LiveLookup = null;

    [Fact]
    public void GoodName_EmptyKey_ReturnsEmpty()
    {
        Assert.Equal("", Localization.GoodName("", MakeCatalog()));
    }

    [Fact]
    public void GoodName_CatalogMiss_FallsBackToModelKey()
    {
        // No LiveLookup, good not in catalog → raw model key wins.
        Assert.Equal("Mystery", Localization.GoodName("Mystery", MakeCatalog()));
    }

    [Fact]
    public void GoodName_CatalogHit_UsesDisplayName_WhenNoLiveLookup()
    {
        var cat = MakeCatalog();
        Assert.Equal("Wood (display)", Localization.GoodName("Wood", cat));
    }

    [Fact]
    public void GoodName_LiveLookupWins_OverCatalogDisplayName()
    {
        Localization.LiveLookup = key => key == "Wood" ? "Holz" : null;
        Assert.Equal("Holz", Localization.GoodName("Wood", MakeCatalog()));
    }

    [Fact]
    public void GoodName_LiveLookupReturnsNull_FallsThroughToCatalog()
    {
        Localization.LiveLookup = _ => null;
        Assert.Equal("Wood (display)", Localization.GoodName("Wood", MakeCatalog()));
    }

    [Fact]
    public void GoodName_LiveLookupReturnsWhitespace_FallsThroughToCatalog()
    {
        Localization.LiveLookup = _ => "   ";
        Assert.Equal("Wood (display)", Localization.GoodName("Wood", MakeCatalog()));
    }

    [Fact]
    public void GoodName_LiveLookupThrows_FallsThroughGracefully()
    {
        // Translation must never crash the UI: a throwing lookup should
        // be swallowed and the catalog DisplayName returned instead.
        Localization.LiveLookup = _ => throw new InvalidOperationException("boom");
        Assert.Equal("Wood (display)", Localization.GoodName("Wood", MakeCatalog()));
    }

    [Fact]
    public void GoodName_LiveLookupThrows_AndCatalogMisses_ReturnsModelKey()
    {
        Localization.LiveLookup = _ => throw new InvalidOperationException("boom");
        Assert.Equal("Phantom", Localization.GoodName("Phantom", MakeCatalog()));
    }

    [Fact]
    public void RaceName_UsesRaceCatalog()
    {
        Assert.Equal("Beaver (display)",  Localization.RaceName("Beaver",  MakeCatalog()));
        Assert.Equal("UnknownRace",       Localization.RaceName("UnknownRace", MakeCatalog()));
    }

    [Fact]
    public void BuildingName_UsesBuildingCatalog()
    {
        Assert.Equal("Sawmill (display)", Localization.BuildingName("Sawmill", MakeCatalog()));
        Assert.Equal("Phantom",           Localization.BuildingName("Phantom", MakeCatalog()));
    }

    [Fact]
    public void RecipeName_UsesRecipeCatalog()
    {
        Assert.Equal("PlanksRecipe (display)",
            Localization.RecipeName("PlanksRecipe", MakeCatalog()));
        Assert.Equal("Phantom", Localization.RecipeName("Phantom", MakeCatalog()));
    }

    private static Catalog MakeCatalog() => new()
    {
        Goods = new Dictionary<string, GoodInfo>
        {
            ["Wood"] = MakeGood("Wood"),
        },
        Buildings = new Dictionary<string, BuildingInfo>
        {
            ["Sawmill"] = MakeBuilding("Sawmill"),
        },
        Races = new Dictionary<string, RaceInfo>
        {
            ["Beaver"] = MakeRace("Beaver"),
        },
        Recipes = new Dictionary<string, RecipeInfo>
        {
            ["PlanksRecipe"] = MakeRecipe("PlanksRecipe"),
        },
    };

    private static GoodInfo MakeGood(string name) => new(
        Name: name, DisplayName: name + " (display)", Category: "x",
        IsEatable: false, EatingFullness: 0,
        CanBeBurned: false, BurningTime: 0,
        TradingBuyValue: 0, TradingSellValue: 0,
        TradersBuying: [], TradersSelling: [], Tags: []);

    private static BuildingInfo MakeBuilding(string name) => new(
        Name: name, DisplayName: name + " (display)",
        Kind: BuildingKind.Workshop, Category: "x", Profession: "x",
        MaxBuilders: 1, Tags: [], Recipes: []);

    private static RaceInfo MakeRace(string name) => new(
        Name: name, DisplayName: name + " (display)",
        BaseSpeed: 1, InitialResolve: 0, MinResolve: -100, MaxResolve: 100,
        ResolvePositiveChangePerSec: 0, ResolveNegativeChangePerSec: 0,
        HungerTolerance: 0, Needs: [], Characteristics: []);

    private static RecipeInfo MakeRecipe(string name) => new(
        Name: name, DisplayName: name + " (display)", Grade: "I",
        ProducedGood: "Wood", ProducedAmount: 1,
        ProductionTime: 60,
        Tags: [], RequiredGoods: [], Buildings: []);
}
