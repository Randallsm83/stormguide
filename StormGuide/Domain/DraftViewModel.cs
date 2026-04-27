namespace StormGuide.Domain;

public sealed record CornerstoneOption(
    string EffectId,
    string DisplayName,
    string Description,
    Score  Synergy,
    int    Rank,
    bool   IsTopRanked);

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
