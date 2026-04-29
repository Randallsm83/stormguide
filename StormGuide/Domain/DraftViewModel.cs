namespace StormGuide.Domain;

public sealed record CornerstoneOption(
    string EffectId,
    string DisplayName,
    string Description,
    Score  Synergy,
    int    Rank,
    bool   IsTopRanked,
    // Tag names that this option targets but no currently-owned cornerstone
    // already touches. Drives the "what changes if you pick this" hint.
    IReadOnlyList<string>? NewlyTargetedTags = null,
    // Total buildings currently affected by tags this option targets.
    int    AffectedBuildings = 0,
    // Per-owned-cornerstone overlap with this option's usability tags.
    // Each entry names an owned cornerstone plus the tags it shares with
    // this option, so the renderer can spell out "stacks with X [tag],
    // Y [tag2]" instead of just a count. Null/empty when nothing overlaps.
    IReadOnlyList<OwnedTagOverlap>? OwnedOverlap = null);

public sealed record OwnedCornerstoneInfo(
    string Id,
    string DisplayName,
    string Description);

public sealed record DraftViewModel(
    IReadOnlyList<CornerstoneOption>     Options,
    bool                                  IsActive,
    IReadOnlyList<OwnedCornerstoneInfo>   Owned,
    string?                               Note = null)
{
    public static DraftViewModel Idle { get; } =
        new(Array.Empty<CornerstoneOption>(),
            IsActive: false,
            Owned: Array.Empty<OwnedCornerstoneInfo>(),
            Note: "No cornerstone pick is currently open.");
}
