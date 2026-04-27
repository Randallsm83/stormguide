namespace StormGuide.Domain;

public sealed record InputAvailability(
    string Good,
    int    Required,
    int    InStock,
    bool   AtRisk,
    bool   NetNegative = false);   // net flow < 0 across the settlement

public sealed record RecipeRanking(
    RecipeInfo Recipe,
    Score      Throughput,
    int        Rank,        // 1-based; ties allowed
    bool       IsTopRanked,
    IReadOnlyList<InputAvailability> Inputs);

public sealed record WorkerStatus(
    int  Assigned,
    int  Capacity,
    bool Idle);

public sealed record RaceFit(
    string Race,
    string DisplayName,
    string MatchingTag,    // first building tag the race has a perk for
    string Effect,         // VillagerPerkEffect (e.g. "Proficiency")
    int    Weight,
    int    Rank,
    bool   IsTopRanked);

public sealed record BuildingViewModel(
    BuildingInfo                       Building,
    IReadOnlyList<RecipeRanking>       Recipes,
    bool                               IsLive,
    WorkerStatus?                      Workers = null,
    IReadOnlyList<RaceFit>?            RaceFits = null,
    string?                            Note = null)
{
    public static BuildingViewModel Missing(string name) =>
        new(new BuildingInfo(name, name, BuildingKind.Other, "", "", 0,
                Array.Empty<string>(), Array.Empty<string>()),
            Array.Empty<RecipeRanking>(),
            IsLive: false,
            Note: "Not in catalog.");
}
