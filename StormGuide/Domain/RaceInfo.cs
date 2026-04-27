namespace StormGuide.Domain;

public sealed record RaceCharacteristic(
    string BuildingTag,
    string VillagerPerkEffect,
    string GlobalEffect,
    string BuildingPerk);

public sealed record RaceInfo(
    string Name,
    string DisplayName,
    double BaseSpeed,
    double InitialResolve,
    double MinResolve,
    double MaxResolve,
    double ResolvePositiveChangePerSec,
    double ResolveNegativeChangePerSec,
    int    HungerTolerance,
    IReadOnlyList<string>              Needs,
    IReadOnlyList<RaceCharacteristic>  Characteristics);
