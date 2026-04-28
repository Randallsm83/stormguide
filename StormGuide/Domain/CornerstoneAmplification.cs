namespace StormGuide.Domain;

/// <summary>
/// Pure heuristic mapping owned cornerstones to the
/// <c>OrderRewardEntry.Category</c> values they amplify. Used by the Orders
/// tab to flag picks whose reward shape synergises with the active
/// cornerstone build (the <c>\u2665 best for me</c> badge).
///
/// Lookup is a case-insensitive substring scan over each cornerstone's
/// display name + description. Categorisation is deliberately conservative:
/// we only add a category when the cornerstone text contains a fairly direct
/// mention of an amplified reward shape, so spurious matches stay rare.
///
/// Lives in <c>Domain/</c> so the test project's <c>&lt;Compile Include&gt;</c>
/// glob picks it up; the shared <c>netstandard2.0</c> + <c>net10.0</c> build
/// constrains us to <c>HashSet&lt;string&gt;</c> as a return type rather than
/// <c>IReadOnlySet&lt;T&gt;</c> (which is .NET 5+).
/// </summary>
public static class CornerstoneAmplification
{
    // Keyword map: each (substring, category) entry adds the category to the
    // result when the substring appears in any owned cornerstone's
    // name+description. Substrings are case-insensitive and matched against
    // a lowercased haystack so the table stays compact.
    //
    // Categories mirror LiveGameState.CategoriseRewardEffect: "reputation",
    // "cornerstone", "goods", "resolve". "other" is intentionally omitted
    // because its reward shape is too vague to amplify meaningfully.
    private static readonly (string Needle, string Category)[] KeywordMap = new[]
    {
        // Reputation / fame / win-condition shape.
        ("reputation",  "reputation"),
        ("fame",        "reputation"),
        ("gold star",   "reputation"),
        ("repute",      "reputation"),
        // Cornerstone / blueprint / perk-pick shape.
        ("cornerstone", "cornerstone"),
        ("blueprint",   "cornerstone"),
        ("perk pick",   "cornerstone"),
        // Resolve / morale shape.
        ("resolve",     "resolve"),
        ("morale",      "resolve"),
        ("happiness",   "resolve"),
        // Trade good / production / yield shape.
        ("trade good",  "goods"),
        ("stockpile",   "goods"),
        ("production",  "goods"),
        ("yield",       "goods"),
        ("output",      "goods"),
    };

    /// <summary>
    /// Returns the set of reward categories that the given cornerstones
    /// appear to amplify. Each tuple is <c>(display name, description)</c>.
    /// Empty / null entries are ignored. The result uses the same string
    /// values as <c>LiveGameState.OrderPickOption.RewardCategories</c> so
    /// callers can intersect directly.
    /// </summary>
    public static HashSet<string> AmplifiedCategoriesFrom(
        IEnumerable<(string Name, string Description)> cornerstones)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cornerstones is null) return result;
        foreach (var (name, description) in cornerstones)
        {
            var combined = (name ?? "") + " " + (description ?? "");
            if (combined.Length <= 1) continue;
            var lower = combined.ToLowerInvariant();
            foreach (var (needle, category) in KeywordMap)
            {
                if (lower.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    result.Add(category);
            }
        }
        return result;
    }
}
