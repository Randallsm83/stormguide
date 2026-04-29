namespace StormGuide.Domain;

/// <summary>
/// Outcome of <see cref="GladeClearTime.Estimate"/>: the inputs (so the
/// renderer can spell them out) plus the projected minutes-to-clear at the
/// stated rate.
/// </summary>
public sealed record GladeClearTimeEstimate(
    int    RemainingGlades,
    int    ScoutCount,
    double SecondsPerScoutPerGlade,
    double MinutesToClear);

/// <summary>
/// Pure heuristic: how long would it take the player to discover every
/// remaining undiscovered glade at the current "scout" worker count? The
/// game's actual scouting service isn't exposed cleanly, so the renderer
/// supplies a worker-count proxy (Resource Gathering category by default)
/// and a per-scout-per-glade time constant.
///
/// Lives in <c>Domain/</c> so it compiles under both <c>netstandard2.0</c>
/// (plugin) and <c>net10.0</c> (test project) and is unit-tested without
/// any game references.
/// </summary>
public static class GladeClearTime
{
    /// <summary>
    /// Default seconds one scout takes to discover one glade. 90s is a
    /// conservative midpoint between the fast-clearance and storm-slowed
    /// rates a typical settlement actually achieves; the value is exposed
    /// as a parameter so the UI can tune later if better telemetry lands.
    /// </summary>
    public const double DefaultSecondsPerScoutPerGlade = 90.0;

    /// <summary>
    /// Returns a clear-time estimate, or null when nothing meaningful can
    /// be reported (no remaining glades, no scouts, or a non-positive
    /// per-scout-per-glade rate). Total time is
    /// <c>remaining * secondsPerScoutPerGlade / scoutCount</c>; the result
    /// is converted to minutes for the renderer.
    /// </summary>
    public static GladeClearTimeEstimate? Estimate(
        int    remainingGlades,
        int    scoutCount,
        double secondsPerScoutPerGlade = DefaultSecondsPerScoutPerGlade)
    {
        if (remainingGlades <= 0) return null;
        if (scoutCount <= 0) return null;
        if (secondsPerScoutPerGlade <= 0) return null;

        var totalSeconds = (double)remainingGlades * secondsPerScoutPerGlade / scoutCount;
        var minutes = totalSeconds / 60.0;
        return new GladeClearTimeEstimate(
            remainingGlades, scoutCount, secondsPerScoutPerGlade, minutes);
    }
}
