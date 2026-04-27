namespace StormGuide.Domain;

public sealed record WorkplaceFit(
    BuildingInfo Building,
    string       BuildingTag,
    string       Effect,        // VillagerPerkEffect (e.g. "Proficiency", "Comfortable_Job")
    int          Rank,
    bool         IsTopRanked);

public sealed record ResolveEffectEntry(
    string Name,
    int    ResolvePerStack,
    int    Stacks)
{
    public int TotalImpact => ResolvePerStack * Stacks;
}

public sealed record ResolveSnapshot(
    float                              Current,
    int                                Target,
    int                                Min,
    int                                Max,
    IReadOnlyList<ResolveEffectEntry>  TopEffects);

public sealed record RaceViewModel(
    RaceInfo                       Race,
    IReadOnlyList<WorkplaceFit>    BestWorkplaces,
    ResolveSnapshot?               Resolve = null,
    string?                        Note = null)
{
    public static RaceViewModel Missing(string name) =>
        new(new RaceInfo(name, name, 0, 0, 0, 0, 0, 0, 0,
                Array.Empty<string>(), Array.Empty<RaceCharacteristic>()),
            Array.Empty<WorkplaceFit>(),
            Note: "Not in catalog.");
}
