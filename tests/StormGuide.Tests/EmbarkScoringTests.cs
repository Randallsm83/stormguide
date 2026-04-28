using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class EmbarkScoringTests
{
    [Fact]
    public void TopStartingGoods_RanksByOverlapTimesValue()
    {
        var cat = new Catalog
        {
            Goods = new Dictionary<string, GoodInfo>
            {
                ["Wood"]  = MakeGood("Wood",  value: 4),
                ["Tools"] = MakeGood("Tools", value: 10),
            },
            Races = new Dictionary<string, RaceInfo>
            {
                ["A"] = MakeRace("A", "Wood", "Tools"),
                ["B"] = MakeRace("B", "Wood"),
                ["C"] = MakeRace("C", "Wood"),
            },
        };
        var top = EmbarkScoring.TopStartingGoods(cat, take: 5);

        // Wood: needed by 3 races × value 4 = 12
        // Tools: needed by 1 race × value 10 = 10
        // Wood beats Tools because overlap multiplies harder than raw value.
        Assert.Equal(2, top.Count);
        Assert.Equal("Wood",  top[0].GoodModel);
        Assert.Equal(3,       top[0].RaceCount);
        Assert.Equal(12.0,    top[0].TotalScore);
        Assert.Equal("Tools", top[1].GoodModel);
        Assert.Equal(1,       top[1].RaceCount);
        Assert.Equal(10.0,    top[1].TotalScore);
    }

    [Fact]
    public void TopStartingGoods_FloorsValueAtOne_ForFreeGoods()
    {
        // A good with TradingBuyValue=0 still scores >0 so it competes on
        // overlap alone rather than getting silently zeroed.
        var cat = new Catalog
        {
            Goods = new Dictionary<string, GoodInfo>
            {
                ["Forage"] = MakeGood("Forage", value: 0),
            },
            Races = new Dictionary<string, RaceInfo>
            {
                ["A"] = MakeRace("A", "Forage"),
                ["B"] = MakeRace("B", "Forage"),
            },
        };
        var top = EmbarkScoring.TopStartingGoods(cat, take: 5);
        Assert.Single(top);
        Assert.Equal("Forage", top[0].GoodModel);
        Assert.Equal(2,        top[0].RaceCount);
        // 2 races × max(1, 0) = 2.
        Assert.Equal(2.0, top[0].TotalScore);
    }

    [Fact]
    public void TopStartingGoods_HandlesMissingCatalogEntry_FallsBackToModelName()
    {
        // Race needs a good not in catalog.Goods. We still tally the overlap;
        // DisplayName falls back to the model name and the value defaults to 1.
        var cat = new Catalog
        {
            Goods = new Dictionary<string, GoodInfo>(),
            Races = new Dictionary<string, RaceInfo>
            {
                ["A"] = MakeRace("A", "Mystery"),
            },
        };
        var top = EmbarkScoring.TopStartingGoods(cat, take: 5);
        Assert.Single(top);
        Assert.Equal("Mystery", top[0].GoodModel);
        Assert.Equal("Mystery", top[0].DisplayName);
        Assert.Equal(1,         top[0].RaceCount);
        Assert.Equal(1.0,       top[0].TotalScore);
    }

    [Fact]
    public void TopStartingGoods_RespectsTake()
    {
        var cat = new Catalog
        {
            Goods = new Dictionary<string, GoodInfo>
            {
                ["A"] = MakeGood("A", value: 1),
                ["B"] = MakeGood("B", value: 2),
                ["C"] = MakeGood("C", value: 3),
            },
            Races = new Dictionary<string, RaceInfo>
            {
                ["R"] = MakeRace("R", "A", "B", "C"),
            },
        };
        Assert.Equal(2, EmbarkScoring.TopStartingGoods(cat, take: 2).Count);
        Assert.Empty(EmbarkScoring.TopStartingGoods(cat, take: 0));
        Assert.Empty(EmbarkScoring.TopStartingGoods(cat, take: -5));
    }

    [Fact]
    public void TopStartingGoods_EmptyCatalog_ReturnsEmpty()
    {
        Assert.Empty(EmbarkScoring.TopStartingGoods(Catalog.Empty, take: 5));
    }

    [Fact]
    public void TopCornerstoneTags_RanksByTotalBuildingHits()
    {
        // Tag "Wood" is on 3 buildings; tag "Stone" is on 1.
        // Both races reference both tags (so the per-race counts add up).
        var cat = new Catalog
        {
            Buildings = new Dictionary<string, BuildingInfo>
            {
                ["B1"] = MakeBuilding("B1", "Wood"),
                ["B2"] = MakeBuilding("B2", "Wood"),
                ["B3"] = MakeBuilding("B3", "Wood"),
                ["B4"] = MakeBuilding("B4", "Stone"),
            },
            Races = new Dictionary<string, RaceInfo>
            {
                ["R1"] = MakeRaceWithTags("R1", "Wood", "Stone"),
                ["R2"] = MakeRaceWithTags("R2", "Wood"),
            },
        };
        var top = EmbarkScoring.TopCornerstoneTags(cat, take: 5);
        Assert.Equal(2, top.Count);
        Assert.Equal("Wood",  top[0].Tag);
        Assert.Equal(6,       top[0].BuildingHits); // 3 buildings × 2 races referencing tag
        Assert.Equal("Stone", top[1].Tag);
        Assert.Equal(1,       top[1].BuildingHits); // 1 building × 1 race
    }

    [Fact]
    public void TopCornerstoneTags_SkipsTagsWithNoBuildings()
    {
        var cat = new Catalog
        {
            Buildings = new Dictionary<string, BuildingInfo>
            {
                ["B1"] = MakeBuilding("B1", "Wood"),
            },
            Races = new Dictionary<string, RaceInfo>
            {
                ["R"] = MakeRaceWithTags("R", "Wood", "PhantomTag"),
            },
        };
        var top = EmbarkScoring.TopCornerstoneTags(cat, take: 5);
        Assert.Single(top);
        Assert.Equal("Wood", top[0].Tag);
    }

    [Fact]
    public void TopCornerstoneTags_RespectsTake()
    {
        var cat = new Catalog
        {
            Buildings = new Dictionary<string, BuildingInfo>
            {
                ["B"] = MakeBuilding("B", "A", "B", "C"),
            },
            Races = new Dictionary<string, RaceInfo>
            {
                ["R"] = MakeRaceWithTags("R", "A", "B", "C"),
            },
        };
        Assert.Equal(2, EmbarkScoring.TopCornerstoneTags(cat, take: 2).Count);
        Assert.Empty(EmbarkScoring.TopCornerstoneTags(cat, take: 0));
        Assert.Empty(EmbarkScoring.TopCornerstoneTags(cat, take: -1));
    }

    private static GoodInfo MakeGood(string name, double value) => new(
        Name: name, DisplayName: name, Category: "x",
        IsEatable: false, EatingFullness: 0,
        CanBeBurned: false, BurningTime: 0,
        TradingBuyValue: value, TradingSellValue: value,
        TradersBuying: [], TradersSelling: [], Tags: []);

    private static RaceInfo MakeRace(string name, params string[] needs) => new(
        Name: name, DisplayName: name,
        BaseSpeed: 1, InitialResolve: 0, MinResolve: -100, MaxResolve: 100,
        ResolvePositiveChangePerSec: 0, ResolveNegativeChangePerSec: 0,
        HungerTolerance: 0, Needs: needs, Characteristics: []);

    private static RaceInfo MakeRaceWithTags(string name, params string[] buildingTags) => new(
        Name: name, DisplayName: name,
        BaseSpeed: 1, InitialResolve: 0, MinResolve: -100, MaxResolve: 100,
        ResolvePositiveChangePerSec: 0, ResolveNegativeChangePerSec: 0,
        HungerTolerance: 0,
        Needs: [],
        Characteristics: buildingTags.Select(t =>
            new RaceCharacteristic(t, "perk", "global", "buildingPerk")).ToList());

    private static BuildingInfo MakeBuilding(string name, params string[] tags) => new(
        Name: name, DisplayName: name,
        Kind: BuildingKind.Workshop, Category: "x", Profession: "x",
        MaxBuilders: 1, Tags: tags, Recipes: []);
}
