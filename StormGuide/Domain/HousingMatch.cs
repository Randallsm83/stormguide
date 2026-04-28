namespace StormGuide.Domain;

/// <summary>How well does the settlement's built housing match a race's preferred housing?</summary>
public enum HousingMatchLevel
{
    /// <summary>Catalog has no race-specific housing entry we can match against — render a dash.</summary>
    Unknown = 0,
    /// <summary>No race-specific house and no generic shelter built, but villagers exist.</summary>
    NoHousing = 1,
    /// <summary>Only generic shelter built; race-specific housing is missing.</summary>
    ShelterOnly = 2,
    /// <summary>At least one race-specific house is built (regardless of whether shelter also exists).</summary>
    Preferred = 3,
}

/// <summary>
/// Outcome of <see cref="HousingMatch.Compute"/>. Carries enough detail for
/// the Villagers tab to render a one-line indicator with the matching house
/// model name (when known) and the homeless count surfaced from
/// <c>VillagersService.GetHomelessAmount</c>.
/// </summary>
public sealed record HousingMatchResult(
    HousingMatchLevel Level,
    int               PreferredCount,
    int               ShelterCount,
    string?           PreferredHouseDisplayName,
    int               Homeless);

/// <summary>
/// Pure heuristic mapping a race's needs + the live built-building count map
/// to a <see cref="HousingMatchResult"/>. Lives in <c>Domain/</c> so it
/// compiles under both <c>netstandard2.0</c> (plugin) and <c>net10.0</c>
/// (test project) and is unit-tested without any game references.
///
/// The race's preferred housing is identified via its needs list: the entry
/// ending in <c>" Housing"</c> that isn't <c>"Any Housing"</c> (e.g.
/// <c>"Bat Housing"</c> → root <c>"Bat"</c>). We then scan the catalog for
/// buildings with <c>Category == "Housing"</c> whose name (after stripping
/// any <c>"Purged "</c> prefix) starts with the root + <c>" House"</c>. The
/// built-count for each matching building model is summed.
///
/// Generic shelters (<c>Shelter</c> / <c>Big Shelter</c>) are recognised by
/// the literal display name. Their built count is reported separately so the
/// caller can word the indicator as "shelter only" when the race-specific
/// build is zero.
/// </summary>
public static class HousingMatch
{
    private static readonly string[] ShelterDisplayNames =
    {
        "Shelter",
        "Big Shelter",
    };

    /// <summary>
    /// Computes the housing match for a race. Returns <see cref="HousingMatchLevel.Unknown"/>
    /// when the race has no <c>"X Housing"</c> need (heuristic can't pick a
    /// preferred building) and there are zero villagers — i.e. nothing to
    /// indicate. Callers are expected to skip rendering when level is
    /// Unknown and Homeless == 0.
    /// </summary>
    public static HousingMatchResult Compute(
        IReadOnlyList<string> raceNeeds,
        Catalog catalog,
        IReadOnlyDictionary<string, int> builtCounts,
        int homeless,
        int alive)
    {
        if (catalog is null) return new HousingMatchResult(HousingMatchLevel.Unknown, 0, 0, null, homeless);
        builtCounts ??= new Dictionary<string, int>();

        // Pull the race's preferred housing root: any need ending in
        // " Housing" that isn't the generic "Any Housing" placeholder.
        string? preferredRoot = null;
        if (raceNeeds is not null)
        {
            foreach (var need in raceNeeds)
            {
                if (string.IsNullOrEmpty(need)) continue;
                if (string.Equals(need, "Any Housing", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (!need.EndsWith(" Housing", System.StringComparison.OrdinalIgnoreCase)) continue;
                preferredRoot = need.Substring(0, need.Length - " Housing".Length).Trim();
                if (preferredRoot.Length > 0) break;
            }
        }

        // Sum shelter counts up-front; they're useful regardless of whether
        // a preferred root exists.
        var shelterCount = 0;
        foreach (var b in catalog.Buildings.Values)
        {
            if (!IsHousingCategory(b.Category)) continue;
            if (!IsGenericShelter(b.DisplayName)) continue;
            if (builtCounts.TryGetValue(b.Name, out var n)) shelterCount += n;
        }

        // No preferred root → unknown shape, but still report shelter +
        // homeless so the caller can render a meaningful line if needed.
        if (string.IsNullOrEmpty(preferredRoot))
        {
            var noRootLevel = (alive > 0 || homeless > 0) && shelterCount == 0
                ? HousingMatchLevel.NoHousing
                : HousingMatchLevel.Unknown;
            return new HousingMatchResult(noRootLevel, 0, shelterCount, null, homeless);
        }

        // Walk the catalog for race-specific housing matching the root.
        var preferredCount = 0;
        string? preferredDisplay = null;
        foreach (var b in catalog.Buildings.Values)
        {
            if (!IsHousingCategory(b.Category)) continue;
            if (IsGenericShelter(b.DisplayName)) continue;
            if (!MatchesRoot(b.DisplayName, preferredRoot!)) continue;
            if (builtCounts.TryGetValue(b.Name, out var n) && n > 0)
            {
                preferredCount += n;
                // Prefer the non-Purged display name for the indicator label.
                if (preferredDisplay is null ||
                    (preferredDisplay.StartsWith("Purged ", System.StringComparison.OrdinalIgnoreCase) &&
                     !b.DisplayName.StartsWith("Purged ", System.StringComparison.OrdinalIgnoreCase)))
                {
                    preferredDisplay = b.DisplayName;
                }
            }
            else
            {
                // Even when no instance is built, remember the first non-
                // Purged catalog name so we can say "no Bat House built".
                preferredDisplay ??= b.DisplayName.StartsWith("Purged ", System.StringComparison.OrdinalIgnoreCase)
                    ? null : b.DisplayName;
            }
        }

        HousingMatchLevel level;
        if (preferredCount > 0) level = HousingMatchLevel.Preferred;
        else if (shelterCount > 0) level = HousingMatchLevel.ShelterOnly;
        else if (alive > 0 || homeless > 0) level = HousingMatchLevel.NoHousing;
        else level = HousingMatchLevel.Unknown;

        return new HousingMatchResult(level, preferredCount, shelterCount, preferredDisplay, homeless);
    }

    private static bool IsHousingCategory(string? category)
        => string.Equals(category, "Housing", System.StringComparison.OrdinalIgnoreCase);

    private static bool IsGenericShelter(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return false;
        foreach (var s in ShelterDisplayNames)
            if (string.Equals(displayName, s, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>
    /// Match a housing display name against the race's preferred root. We
    /// strip an optional <c>"Purged "</c> prefix so the indicator counts
    /// purged variants alongside the regular building, then check that the
    /// remainder starts with <c>root + " House"</c> (case-insensitive). The
    /// trailing space-prefix lets <c>"Bat"</c> match <c>"Bat House"</c>
    /// without also matching unrelated names like <c>"Batavia"</c>.
    /// </summary>
    private static bool MatchesRoot(string displayName, string root)
    {
        if (string.IsNullOrEmpty(displayName)) return false;
        var name = displayName;
        const string purgedPrefix = "Purged ";
        if (name.StartsWith(purgedPrefix, System.StringComparison.OrdinalIgnoreCase))
            name = name.Substring(purgedPrefix.Length);
        var prefix = root + " House";
        return name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);
    }
}
