namespace StormGuide.Domain;

/// <summary>One starting-good recommendation row.</summary>
public sealed record EmbarkGoodScore(
    string GoodModel,
    string DisplayName,
    int    RaceCount,
    double TotalScore);

/// <summary>One cornerstone-tag leverage row.</summary>
public sealed record EmbarkTagScore(
    string Tag,
    int    BuildingHits);

/// <summary>
/// Pure pre-settlement scoring used by the Embark tab. No game state, no
/// Unity, no BepInEx - just the static catalog. Lives in <c>Domain/</c> so
/// the test project picks it up via the existing <c>Compile Include</c> glob
/// and the dependency direction (Resources -&gt; Domain -&gt; Providers -&gt; UI)
/// stays enforceable.
///
/// Both rankers are deterministic given the same catalog, which is what makes
/// them unit-testable. The Embark tab wraps these results with display niceties
/// (display-name lookups, tooltips, jump-to-Villagers buttons).
/// </summary>
public static class EmbarkScoring
{
    /// <summary>
    /// Top starting goods, ranked by <c>(race_need_overlap × trade_value)</c>.
    /// A good needed by 3 races at trade value 4 outranks a good needed by 1
    /// race at trade value 10 (12 vs 10) - overlap matters more than raw value
    /// because day-1 starvation is a multi-race problem.
    /// </summary>
    /// <param name="catalog">Source catalog. Must not be null.</param>
    /// <param name="take">Max number of rows to return (>= 0). Smaller = shorter list.</param>
    public static IReadOnlyList<EmbarkGoodScore> TopStartingGoods(Catalog catalog, int take)
    {
        if (take <= 0) return Array.Empty<EmbarkGoodScore>();

        // Tally how many distinct races include each good in their Needs.
        var hits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in catalog.Races.Values)
        {
            foreach (var n in race.Needs)
            {
                if (string.IsNullOrEmpty(n)) continue;
                hits.TryGetValue(n, out var h);
                hits[n] = h + 1;
            }
        }

        return hits
            .Select(kv =>
            {
                var info = catalog.Goods.TryGetValue(kv.Key, out var gi) ? gi : null;
                var disp = info?.DisplayName ?? kv.Key;
                // Floor the value at 1 so a good with TradingBuyValue=0 still
                // contributes a non-zero score proportional to race overlap;
                // otherwise critical free goods (e.g. raw forage) sink to 0.
                var val  = info is null ? 1.0 : Math.Max(1.0, info.TradingBuyValue);
                return new EmbarkGoodScore(kv.Key, disp, kv.Value, kv.Value * val);
            })
            .OrderByDescending(s => s.TotalScore)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Top building tags by total catalog-building hits across all race
    /// characteristics. Higher = more buildings carry that tag, so a
    /// cornerstone hitting that tag has more leverage on this race set.
    /// </summary>
    /// <param name="catalog">Source catalog. Must not be null.</param>
    /// <param name="take">Max number of rows to return (>= 0).</param>
    public static IReadOnlyList<EmbarkTagScore> TopCornerstoneTags(Catalog catalog, int take)
    {
        if (take <= 0) return Array.Empty<EmbarkTagScore>();

        var tagScore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in catalog.Races.Values)
        {
            foreach (var c in race.Characteristics)
            {
                var tag = c.BuildingTag;
                if (string.IsNullOrEmpty(tag)) continue;
                var hits = catalog.Buildings.Values.Count(b =>
                    b.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
                if (hits == 0) continue;
                tagScore.TryGetValue(tag, out var s);
                tagScore[tag] = s + hits;
            }
        }

        return tagScore
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(kv => new EmbarkTagScore(kv.Key, kv.Value))
            .ToList();
    }
}
