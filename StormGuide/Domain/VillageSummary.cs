namespace StormGuide.Domain;

public sealed record RacePresence(
    string Race,
    string DisplayName,
    int    Alive,
    int    Total,
    int    Homeless,
    float  CurrentResolve,
    int    TargetResolve);

public sealed record VillageSummary(
    int                            TotalVillagers,
    IReadOnlyList<RacePresence>    Races);
