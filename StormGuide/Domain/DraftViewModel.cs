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
    int    AffectedBuildings = 0);

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
