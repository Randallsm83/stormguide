namespace StormGuide.Data;

/// <summary>
/// Single source of truth for the TTLs feeding <see cref="TtlCache{T}"/>
/// instances in the UI, plus the rolling-window size for the Diagnostics
/// per-section frame-cost ring.
///
/// Editing one constant rebalances the whole UI: shorter TTLs surface state
/// changes faster but spend more CPU on each redraw; longer TTLs are cheaper
/// but make data feel laggy. Tracked in the StormGuide 1.0 plan
/// (<c>503ea85f-d552-4ae2-baae-2b894fb2bc18</c>) Phase C - per-tab p50/p95
/// budgets are still pending empirical measurement; the values here are
/// hand-tuned defaults that keep IMGUI redraws cheap on a representative
/// mid-game settlement without making any field stale enough to mislead the
/// player.
///
/// All values are seconds (frame-counts for ring sizes).
/// </summary>
internal static class CacheBudget
{
    /// <summary>Settlement alerts strip (idle workshops, at-risk goods, low resolve).</summary>
    public const float AlertsTtlSec = 0.5f;

    /// <summary>Village summary header (population, homeless, per-race resolve).</summary>
    public const float SummaryTtlSec = 0.5f;

    /// <summary>Owned-cornerstone snapshot. Slower-moving than alerts.</summary>
    public const float OwnedCornerstonesTtlSec = 1.0f;

    /// <summary>Current-trader and next-trader desire rankings.</summary>
    public const float TraderDesiresTtlSec = 0.5f;

    /// <summary>
    /// Number of frame-cost samples each Diagnostics PerfRing holds. At ~60 FPS
    /// this is about two seconds of history, which is enough for p95 to be
    /// stable but short enough that a code change shows up in seconds, not
    /// minutes.
    /// </summary>
    public const int PerfRingFrames = 120;
}
