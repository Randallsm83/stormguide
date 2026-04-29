namespace StormGuide.Domain;

/// <summary>
/// One row in a <see cref="CornerstoneOverlap.Compute"/> result: the
/// owned cornerstone's display name plus the tags it shares with the draft
/// option being inspected.
/// </summary>
public sealed record OwnedTagOverlap(
    string OwnedDisplayName,
    IReadOnlyList<string> SharedTags);

/// <summary>
/// Pure heuristic: for a draft option's <c>usabilityTags</c>, list every
/// currently-owned cornerstone that shares at least one tag, along with the
/// shared tag names. Drives the Draft tab's "stacks with" inline list so the
/// player can see exactly which existing cornerstone each tag overlap is
/// coming from \u2014 the score breakdown only carries per-tag counts.
///
/// Lives in <c>Domain/</c> so it compiles under both <c>netstandard2.0</c>
/// and <c>net10.0</c> and is unit-tested without any game references.
/// Inputs are deliberately interface-typed so the test fixtures can pass
/// plain tuples without constructing game-flavoured records.
/// </summary>
public static class CornerstoneOverlap
{
    /// <summary>
    /// Returns at most one row per owned cornerstone. Owned cornerstones with
    /// zero shared tags are filtered out so the caller can render the result
    /// directly without re-checking emptiness. Tag matching is
    /// case-insensitive; duplicate tag names within a single owned cornerstone
    /// are collapsed.
    /// </summary>
    public static IReadOnlyList<OwnedTagOverlap> Compute(
        IEnumerable<string>? optionTags,
        IEnumerable<(string DisplayName, IReadOnlyList<string> Tags)>? ownedWithTags)
    {
        var result = new List<OwnedTagOverlap>();
        if (optionTags is null || ownedWithTags is null) return result;

        var optSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in optionTags)
            if (!string.IsNullOrEmpty(t)) optSet.Add(t);
        if (optSet.Count == 0) return result;

        foreach (var (display, tags) in ownedWithTags)
        {
            if (string.IsNullOrEmpty(display) || tags is null) continue;
            var shared = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (!optSet.Contains(t)) continue;
                if (!seen.Add(t)) continue;
                shared.Add(t);
            }
            if (shared.Count > 0)
                result.Add(new OwnedTagOverlap(display, shared));
        }
        return result;
    }
}
