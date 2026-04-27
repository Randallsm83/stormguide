namespace StormGuide.Domain;

public enum BuildingKind
{
    Workshop,
    Camp,
    Farm,
    Mine,
    Institution,
    House,
    Decoration,
    Other
}

public sealed record BuildingInfo(
    string       Name,
    string       DisplayName,
    BuildingKind Kind,
    string       Category,
    string       Profession,
    int          MaxBuilders,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Recipes);
