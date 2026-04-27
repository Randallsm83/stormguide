using Eremite.Controller;
using Eremite.Model;
using StormGuide.Domain;

namespace StormGuide.Providers;

/// <summary>
/// Reads the active cornerstone pick from the game and ranks the options by
/// structural synergy: how many of the player's currently-built buildings
/// carry a tag that this effect explicitly targets via <c>usabilityTags</c>.
/// All math is exposed as <see cref="ScoreComponent"/>s row-by-row.
/// </summary>
public static class CornerstoneDraftProvider
{
    public static DraftViewModel Current()
    {
        var gc = GameController.Instance;
        if (gc?.GameServices is not { Loaded: true } services)
            return DraftViewModel.Idle;

        var owned = StormGuide.Data.LiveGameState.OwnedCornerstones()
            .Select(o => new OwnedCornerstoneInfo(o.Id, o.DisplayName, o.Description))
            .ToList();

        var pick = services.CornerstonesService?.GetCurrentPick();
        if (pick == null || pick.options == null || pick.options.Count == 0)
            return new DraftViewModel(
                Array.Empty<CornerstoneOption>(),
                IsActive: false,
                Owned:   owned,
                Note:    "No cornerstone pick is currently open.");

        // Tally: how many built buildings carry each tag (by tag name).
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalBuildings = 0;
        try
        {
            var buildings = services.BuildingsService?.Buildings;
            if (buildings != null)
                foreach (var b in buildings.Values)
                {
                    if (b?.BuildingModel == null) continue;
                    totalBuildings++;
                    var tags = b.BuildingModel.tags;
                    if (tags == null) continue;
                    foreach (var t in tags)
                    {
                        if (t == null || string.IsNullOrEmpty(t.Name)) continue;
                        tagCounts.TryGetValue(t.Name, out var n);
                        tagCounts[t.Name] = n + 1;
                    }
                }
        }
        catch { /* fall through with whatever we tallied */ }

        var modelService = services.GameModelService;
        var ownedTagCounts = StormGuide.Data.LiveGameState.OwnedCornerstoneUsabilityTags();
        var options = new List<(EffectModel? effect, string id, Score score)>(pick.options.Count);
        foreach (var id in pick.options)
        {
            EffectModel? eff = null;
            try { eff = modelService?.GetEffect(id); } catch { }
            options.Add((eff, id, ScoreOption(eff, id, tagCounts, totalBuildings, ownedTagCounts)));
        }

        var sorted = options
            .OrderByDescending(o => o.score.Value)
            .ToList();

        var built = new List<CornerstoneOption>(sorted.Count);
        var rank = 0; var lastValue = double.NaN;
        for (var i = 0; i < sorted.Count; i++)
        {
            var (eff, id, score) = sorted[i];
            if (i == 0 || Math.Abs(score.Value - lastValue) > 1e-9) rank = i + 1;
            lastValue = score.Value;
            var name = eff?.DisplayName ?? id;
            var desc = SafeDescription(eff);
            built.Add(new CornerstoneOption(id, name, desc, score, rank, IsTopRanked: rank == 1));
        }

        return new DraftViewModel(built, IsActive: true, Owned: owned);
    }

    /// <summary>
    /// Synergy score = sum of (buildings carrying tag) per declared usabilityTag,
    /// plus a smaller bonus for stacking with currently-owned cornerstones that
    /// share any usability tag. Per-component contributions are exposed so the
    /// player can verify what drove the score.
    /// </summary>
    private static Score ScoreOption(
        EffectModel? eff, string id,
        Dictionary<string, int> tagCounts, int totalBuildings,
        IReadOnlyDictionary<string, int> ownedTagCounts)
    {
        var components = new List<ScoreComponent>();
        ModelTag[]? usability = null;
        try { usability = eff?.usabilityTags; } catch { }

        double total = 0;
        if (usability is { Length: > 0 })
        {
            foreach (var tag in usability)
            {
                if (tag == null || string.IsNullOrEmpty(tag.Name)) continue;
                var count = tagCounts.TryGetValue(tag.Name, out var n) ? n : 0;
                total += count;
                components.Add(new ScoreComponent(
                    $"tag {tag.Name}", count, "buildings carrying this tag"));

                if (ownedTagCounts.TryGetValue(tag.Name, out var owned) && owned > 0)
                {
                    // Half-weight: owned cornerstones don't add new buildings,
                    // but do compound effect-shape so it's still a meaningful tie
                    // breaker between equally-targeted options.
                    var bonus = owned * 0.5;
                    total += bonus;
                    components.Add(new ScoreComponent(
                        $"stacks with owned ({tag.Name})", bonus,
                        $"{owned} owned cornerstone(s) share this tag"));
                }
            }
        }
        else
        {
            // Give untargeted effects a small floor so they don't all collide at
            // zero and look indistinguishable. Rationale stays explicit.
            total = 0.5;
            components.Add(new ScoreComponent(
                "untargeted", total, "effect does not declare usability tags"));
        }

        components.Add(new ScoreComponent(
            "total buildings", totalBuildings, "in current settlement"));

        return new Score(total, components, Unit: "buildings");
    }

    private static string SafeDescription(EffectModel? eff)
    {
        if (eff == null) return "";
        try { return eff.Description ?? ""; }
        catch { return ""; }
    }
}
