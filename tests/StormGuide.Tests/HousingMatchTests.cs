using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

public class HousingMatchTests
{
    private static BuildingInfo HousingBuilding(string name)
        => new(name, name, BuildingKind.Other, "Housing", "", 2,
               Array.Empty<string>(), Array.Empty<string>());

    private static Catalog CatalogWith(params BuildingInfo[] buildings)
    {
        var dict = new Dictionary<string, BuildingInfo>();
        foreach (var b in buildings) dict[b.Name] = b;
        return new Catalog { Buildings = dict };
    }

    private static readonly string[] BatNeeds =
    {
        "Any Housing", "Bat Housing", "Biscuits", "Paste",
    };

    [Fact]
    public void NoCatalog_Returns_Unknown()
    {
        var r = HousingMatch.Compute(BatNeeds, null!, new Dictionary<string, int>(), homeless: 0, alive: 5);
        Assert.Equal(HousingMatchLevel.Unknown, r.Level);
    }

    [Fact]
    public void Preferred_Built_Returns_Preferred()
    {
        var cat = CatalogWith(
            HousingBuilding("Bat House"),
            HousingBuilding("Shelter"));
        var built = new Dictionary<string, int> { ["Bat House"] = 3 };
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 0, alive: 8);
        Assert.Equal(HousingMatchLevel.Preferred, r.Level);
        Assert.Equal(3, r.PreferredCount);
        Assert.Equal(0, r.ShelterCount);
        Assert.Equal("Bat House", r.PreferredHouseDisplayName);
    }

    [Fact]
    public void ShelterOnly_When_NoPreferred_But_ShelterBuilt()
    {
        var cat = CatalogWith(
            HousingBuilding("Bat House"),
            HousingBuilding("Shelter"),
            HousingBuilding("Big Shelter"));
        var built = new Dictionary<string, int>
        {
            ["Shelter"]     = 2,
            ["Big Shelter"] = 1,
        };
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 4, alive: 8);
        Assert.Equal(HousingMatchLevel.ShelterOnly, r.Level);
        Assert.Equal(0, r.PreferredCount);
        Assert.Equal(3, r.ShelterCount);
        Assert.Equal(4, r.Homeless);
        Assert.Equal("Bat House", r.PreferredHouseDisplayName);
    }

    [Fact]
    public void NoHousing_When_NothingBuilt_AndAlive()
    {
        var cat = CatalogWith(HousingBuilding("Bat House"), HousingBuilding("Shelter"));
        var built = new Dictionary<string, int>();
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 2, alive: 5);
        Assert.Equal(HousingMatchLevel.NoHousing, r.Level);
        Assert.Equal(0, r.PreferredCount);
        Assert.Equal(0, r.ShelterCount);
    }

    [Fact]
    public void Unknown_When_NoBuiltAndNoVillagers()
    {
        var cat = CatalogWith(HousingBuilding("Bat House"), HousingBuilding("Shelter"));
        var built = new Dictionary<string, int>();
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 0, alive: 0);
        Assert.Equal(HousingMatchLevel.Unknown, r.Level);
    }

    [Fact]
    public void PurgedVariant_CountsTowardPreferred()
    {
        var cat = CatalogWith(
            HousingBuilding("Bat House"),
            HousingBuilding("Purged Bat House"));
        var built = new Dictionary<string, int>
        {
            ["Purged Bat House"] = 2,
        };
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 0, alive: 4);
        Assert.Equal(HousingMatchLevel.Preferred, r.Level);
        Assert.Equal(2, r.PreferredCount);
        // Display name prefers the non-purged form when it exists in the catalog.
        Assert.Equal("Bat House", r.PreferredHouseDisplayName);
    }

    [Fact]
    public void Race_With_Only_AnyHousing_Falls_Through_To_Shelter_Or_NoHousing()
    {
        var cat = CatalogWith(HousingBuilding("Shelter"));
        var built = new Dictionary<string, int> { ["Shelter"] = 1 };
        var generic = new[] { "Any Housing", "Biscuits" };
        var r = HousingMatch.Compute(generic, cat, built, homeless: 0, alive: 4);
        // No preferred root → ShelterCount surfaces; level is Unknown
        // because we can't classify "preferred vs shelter only".
        Assert.Equal(HousingMatchLevel.Unknown, r.Level);
        Assert.Equal(1, r.ShelterCount);
        Assert.Null(r.PreferredHouseDisplayName);
    }

    [Fact]
    public void FoxRoot_Matches_FoxHouse_Even_Though_RaceName_Is_Foxes()
    {
        // The catalog uses the singular "Fox House" while the race display
        // name is plural "Foxes". The need item "Fox Housing" is the
        // canonical join key, not the race name.
        var cat = CatalogWith(HousingBuilding("Fox House"));
        var built = new Dictionary<string, int> { ["Fox House"] = 1 };
        var foxNeeds = new[] { "Any Housing", "Fox Housing", "Porridge" };
        var r = HousingMatch.Compute(foxNeeds, cat, built, homeless: 0, alive: 4);
        Assert.Equal(HousingMatchLevel.Preferred, r.Level);
        Assert.Equal("Fox House", r.PreferredHouseDisplayName);
    }

    [Fact]
    public void CaseInsensitive_Matching()
    {
        var cat = CatalogWith(HousingBuilding("BAT HOUSE"));
        var built = new Dictionary<string, int> { ["BAT HOUSE"] = 2 };
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 0, alive: 3);
        Assert.Equal(HousingMatchLevel.Preferred, r.Level);
        Assert.Equal(2, r.PreferredCount);
    }

    [Fact]
    public void Preferred_Wins_When_Both_Preferred_And_Shelter_Built()
    {
        var cat = CatalogWith(
            HousingBuilding("Bat House"),
            HousingBuilding("Shelter"));
        var built = new Dictionary<string, int>
        {
            ["Bat House"] = 1,
            ["Shelter"]   = 5,
        };
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 0, alive: 10);
        Assert.Equal(HousingMatchLevel.Preferred, r.Level);
        Assert.Equal(1, r.PreferredCount);
        Assert.Equal(5, r.ShelterCount);
    }

    [Fact]
    public void Homeless_Surfaced_Regardless_Of_Level()
    {
        var cat = CatalogWith(HousingBuilding("Bat House"));
        var built = new Dictionary<string, int> { ["Bat House"] = 5 };
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 7, alive: 12);
        Assert.Equal(HousingMatchLevel.Preferred, r.Level);
        Assert.Equal(7, r.Homeless);
    }

    [Fact]
    public void RootMatching_Avoids_Spurious_Substring_Hits()
    {
        // "Bat" should match "Bat House" but not a hypothetical "Bathysphere".
        var cat = CatalogWith(
            HousingBuilding("Bat House"),
            HousingBuilding("Bathysphere"));
        var built = new Dictionary<string, int> { ["Bathysphere"] = 4 };
        var r = HousingMatch.Compute(BatNeeds, cat, built, homeless: 0, alive: 6);
        Assert.Equal(HousingMatchLevel.NoHousing, r.Level);
        Assert.Equal(0, r.PreferredCount);
    }
}
