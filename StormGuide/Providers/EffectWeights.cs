namespace StormGuide.Providers;

/// <summary>
/// Shared weight map for villager perk effects. Higher = better worker fit.
/// Used by <see cref="VillagerProvider"/> (race → best workplaces) and
/// <see cref="BuildingProvider"/> (building → best races) so both directions
/// rank using the same numbers.
/// </summary>
internal static class EffectWeights
{
    public static readonly IReadOnlyDictionary<string, int> ByVillagerPerk =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Proficiency"]       = 3,
            ["Comfortable_Job"]   = 2,
            ["Smart_Worker"]      = 2,
            ["Faster_Woocutters"] = 2,
            ["Leisure_Worker"]    = 1,
        };

    public static int For(string villagerPerkEffect)
        => ByVillagerPerk.TryGetValue(villagerPerkEffect, out var w) ? w : 1;
}
