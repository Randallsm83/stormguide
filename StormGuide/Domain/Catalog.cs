namespace StormGuide.Domain;

public sealed class Catalog
{
    public string GameVersion  { get; init; } = "unknown";
    public string ExportedAtUtc { get; init; } = "";

    public IReadOnlyDictionary<string, GoodInfo>     Goods     { get; init; } = new Dictionary<string, GoodInfo>();
    public IReadOnlyDictionary<string, RaceInfo>     Races     { get; init; } = new Dictionary<string, RaceInfo>();
    public IReadOnlyDictionary<string, BuildingInfo> Buildings { get; init; } = new Dictionary<string, BuildingInfo>();
    public IReadOnlyDictionary<string, RecipeInfo>   Recipes   { get; init; } = new Dictionary<string, RecipeInfo>();

    public bool IsEmpty => Goods.Count == 0 && Races.Count == 0 && Buildings.Count == 0 && Recipes.Count == 0;

    public static Catalog Empty { get; } = new Catalog();

    /// <summary>All recipes that produce a particular good.</summary>
    public IEnumerable<RecipeInfo> RecipesProducing(string goodName)
        => Recipes.Values.Where(r => r.ProducedGood == goodName);

    /// <summary>All recipes that consume a particular good (in any input slot).</summary>
    public IEnumerable<RecipeInfo> RecipesConsuming(string goodName)
        => Recipes.Values.Where(r => r.RequiredGoods.Any(slot => slot.Options.Any(o => o.Good == goodName)));

    /// <summary>All races that include this good in their needs list.</summary>
    public IEnumerable<RaceInfo> RacesNeeding(string needName)
        => Races.Values.Where(r => r.Needs.Contains(needName));
}
