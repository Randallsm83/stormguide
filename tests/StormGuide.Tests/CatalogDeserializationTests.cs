using System.IO;
using Newtonsoft.Json;
using StormGuide.Domain;
using Xunit;

namespace StormGuide.Tests;

/// <summary>
/// Exercises the real embedded catalog JSON files using the same Newtonsoft
/// settings <c>StormGuide.Data.StaticCatalog</c> uses at runtime. The test
/// project deliberately doesn't share <c>StaticCatalog.cs</c> (it depends on
/// BepInEx logging + the running plugin assembly), so we mirror its settings
/// here. If <c>StaticCatalog.JsonSettings</c> drifts, update <c>JsonSettings</c>
/// in this file to match - they must stay in lockstep.
/// </summary>
public class CatalogDeserializationTests
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling     = NullValueHandling.Ignore,
    };

    private static string CatalogDir() => Path.Combine(AppContext.BaseDirectory, "catalog");

    [Theory]
    [InlineData("goods.json")]
    [InlineData("races.json")]
    [InlineData("buildings.json")]
    [InlineData("recipes.json")]
    public void CatalogFile_Exists(string fileName)
    {
        var path = Path.Combine(CatalogDir(), fileName);
        Assert.True(File.Exists(path), $"Missing {path}. Did the csproj <None Copy> step fail?");
    }

    [Fact]
    public void Goods_DeserializeAndAreNamed()
    {
        var items = LoadList<GoodInfo>("goods.json");
        Assert.NotEmpty(items);
        Assert.All(items, g => Assert.False(string.IsNullOrEmpty(g.Name)));
    }

    [Fact]
    public void Races_DeserializeAndAreNamed()
    {
        var items = LoadList<RaceInfo>("races.json");
        Assert.NotEmpty(items);
        Assert.All(items, r => Assert.False(string.IsNullOrEmpty(r.Name)));
    }

    [Fact]
    public void Buildings_DeserializeAndAreNamed()
    {
        var items = LoadList<BuildingInfo>("buildings.json");
        Assert.NotEmpty(items);
        Assert.All(items, b => Assert.False(string.IsNullOrEmpty(b.Name)));
    }

    [Fact]
    public void Recipes_DeserializeAndAreNamed()
    {
        var items = LoadList<RecipeInfo>("recipes.json");
        Assert.NotEmpty(items);
        Assert.All(items, r => Assert.False(string.IsNullOrEmpty(r.Name)));
        // Note: ProducedGood is intentionally allowed to be empty - gathering
        // camps and institutional recipes don't produce a named good (they
        // yield raw materials at the source or grant effects). At least most
        // recipes should still produce something.
        var producing = items.Count(r => !string.IsNullOrEmpty(r.ProducedGood));
        Assert.True(producing > items.Count / 2,
            $"Only {producing}/{items.Count} recipes have a ProducedGood; expected the majority to produce.");
    }

    [Fact]
    public void Recipes_RoundTrip_PreservesIdentityFields()
    {
        // Spot-check a stable invariant rather than deep-equality on
        // records-of-lists. If the producer round-trip preserves Name,
        // ProducedGood, and ProducedAmount, the rest of the field set is
        // mechanically a list-of-records reflection at the Newtonsoft level.
        var items     = LoadList<RecipeInfo>("recipes.json");
        var serial    = JsonConvert.SerializeObject(items, JsonSettings);
        var reparsed  = JsonConvert.DeserializeObject<List<RecipeInfo>>(serial, JsonSettings)!;

        Assert.Equal(items.Count, reparsed.Count);
        for (var i = 0; i < items.Count; i++)
        {
            Assert.Equal(items[i].Name,           reparsed[i].Name);
            Assert.Equal(items[i].ProducedGood,   reparsed[i].ProducedGood);
            Assert.Equal(items[i].ProducedAmount, reparsed[i].ProducedAmount);
            Assert.Equal(items[i].ProductionTime, reparsed[i].ProductionTime);
        }
    }

    [Fact]
    public void Catalog_HasReasonableShape()
    {
        // Ratios documented in README.md: 74 goods / 7 races / 186 buildings /
        // 243 recipes. Use ranges so a future catalog regen doesn't break the
        // build, but flag any wholesale loss of data.
        var goods     = LoadList<GoodInfo>("goods.json");
        var races     = LoadList<RaceInfo>("races.json");
        var buildings = LoadList<BuildingInfo>("buildings.json");
        var recipes   = LoadList<RecipeInfo>("recipes.json");

        Assert.InRange(goods.Count,     30, 500);
        Assert.InRange(races.Count,      3,  50);
        Assert.InRange(buildings.Count, 50, 500);
        Assert.InRange(recipes.Count,   50, 800);
        // Recipes outnumber buildings (multiple recipes per workshop is the rule).
        Assert.True(recipes.Count > buildings.Count,
            $"Expected recipes ({recipes.Count}) > buildings ({buildings.Count})");
    }

    private static List<T> LoadList<T>(string fileName)
    {
        var path = Path.Combine(CatalogDir(), fileName);
        var json = File.ReadAllText(path);
        return JsonConvert.DeserializeObject<List<T>>(json, JsonSettings) ?? new List<T>();
    }
}
