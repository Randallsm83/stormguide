namespace StormGuide.Domain;

public sealed record ProductionPath(
    RecipeInfo  Recipe,
    BuildingInfo? Building,    // first building that runs this recipe (may be null)
    Score       Cost,          // lower = better
    int         Rank,
    bool        IsCheapest);

public sealed record TraderSnapshot(
    string DisplayName,
    bool   IsInVillage,
    bool   BuysThisGood,
    bool   SellsThisGood);

public sealed record FlowRow(
    string  BuildingName,
    string  RecipeName,
    double  PerMin,
    bool    IsProducer);

public sealed record GoodFlowSnapshot(
    double ProducedPerMin,
    double ConsumedPerMin,
    int    Stockpile,
    double? RunwaySeconds,                  // null if net is non-negative or stockpile is 0
    IReadOnlyList<FlowRow> Contributions)
{
    public double Net => ProducedPerMin - ConsumedPerMin;
    public bool   IsNetNegative => Net < -1e-6;
}

public sealed record GoodViewModel(
    GoodInfo                       Good,
    IReadOnlyList<ProductionPath>  Producers,
    IReadOnlyList<RecipeInfo>      Consumers,
    IReadOnlyList<RaceInfo>        NeededBy,
    TraderSnapshot?                CurrentTrader = null,
    TraderSnapshot?                NextTrader    = null,
    GoodFlowSnapshot?              Flow          = null,
    string?                        Note          = null)
{
    public static GoodViewModel Missing(string name) =>
        new(new GoodInfo(name, name, "", false, 0, false, 0, 0, 0,
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            Array.Empty<ProductionPath>(),
            Array.Empty<RecipeInfo>(),
            Array.Empty<RaceInfo>(),
            Note: "Not in catalog.");
}
