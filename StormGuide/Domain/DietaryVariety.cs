namespace StormGuide.Domain;

/// <summary>
/// Outcome of <see cref="DietaryVariety.Compute"/>: the inputs the renderer
/// needs to spell the result out, plus the pre-computed score so the UI
/// branches on a single number.
/// </summary>
/// <param name="TotalNeeds">Count of catalog needs considered (post-filter).</param>
/// <param name="SuppliedCount">Count of those needs whose stockpile is &gt; 0.</param>
/// <param name="ScorePercent">SuppliedCount * 100 / TotalNeeds, rounded to nearest int. 0 when TotalNeeds == 0.</param>
/// <param name="MissingNeeds">Subset of needs with zero stockpile, in input order.</param>
public sealed record DietaryVarietyResult(
    int                          TotalNeeds,
    int                          SuppliedCount,
    int                          ScorePercent,
    IReadOnlyList<string>        MissingNeeds);

/// <summary>
/// Pure metric: how varied is the food/need supply currently in stock for a
/// given race? Counts the race's catalog needs whose stockpile is &gt; 0 and
/// returns the ratio as a 0..100 score plus the missing entries so the UI can
/// nudge the player toward what's still empty.
///
/// Lives in <c>Domain/</c> so it compiles under both <c>netstandard2.0</c>
/// (plugin) and <c>net10.0</c> (test project) and is unit-tested without any
/// game references.
/// </summary>
public static class DietaryVariety
{
    /// <summary>
    /// Returns a variety result, or null when there's nothing meaningful to
    /// report (null inputs, or zero non-empty needs after filtering). Empty
    /// or whitespace need entries are skipped silently so a malformed catalog
    /// row doesn't drag the score down.
    /// </summary>
    public static DietaryVarietyResult? Compute(
        IReadOnlyList<string>? needs,
        Func<string, int>?     stockLookup)
    {
        if (needs is null || stockLookup is null) return null;
        var filtered = new List<string>(needs.Count);
        foreach (var n in needs)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            filtered.Add(n);
        }
        if (filtered.Count == 0) return null;

        var supplied = 0;
        var missing  = new List<string>();
        foreach (var n in filtered)
        {
            int stock;
            try { stock = stockLookup(n); }
            catch { stock = 0; }
            if (stock > 0) supplied++;
            else           missing.Add(n);
        }
        // Round to the nearest integer percent. Use a manual round so the
        // helper stays free of any culture-sensitive formatting.
        var raw = (supplied * 100.0) / filtered.Count;
        var pct = (int)System.Math.Round(raw, System.MidpointRounding.AwayFromZero);
        return new DietaryVarietyResult(filtered.Count, supplied, pct, missing);
    }
}
