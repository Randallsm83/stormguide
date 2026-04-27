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
        // Cross-run pick history: read once and turn into a HashSet for O(1)
        // membership checks during scoring.
        var pickHistory = ReadPickHistory();
        var options = new List<(EffectModel? effect, string id, Score score, List<string> newTags, int hits)>(pick.options.Count);
        foreach (var id in pick.options)
        {
            EffectModel? eff = null;
            try { eff = modelService?.GetEffect(id); } catch { }
            var (newTags, hits) = ComputeDiff(eff, tagCounts, ownedTagCounts);
            var score = ScoreOption(eff, id, tagCounts, totalBuildings, ownedTagCounts);
            // Tiebreaker bonus: small score nudge for cornerstones the player
            // has historically picked. Doesn't override real synergy, but
            // lifts familiar choices when the math is otherwise tied.
            if (pickHistory.Contains(id))
            {
                var bonus = 0.25;
                score = new Score(score.Value + bonus,
                    score.Components
                        .Concat(new[] { new ScoreComponent(
                            "familiar pick", bonus, "present in cross-run pick history") })
                        .ToList(),
                    score.Unit);
            }
            options.Add((eff, id, score, newTags, hits));
        }

        var sorted = options
            .OrderByDescending(o => o.score.Value)
            .ToList();

        var built = new List<CornerstoneOption>(sorted.Count);
        var rank = 0; var lastValue = double.NaN;
        for (var i = 0; i < sorted.Count; i++)
        {
            var (eff, id, score, newTags, hits) = sorted[i];
            if (i == 0 || Math.Abs(score.Value - lastValue) > 1e-9) rank = i + 1;
            lastValue = score.Value;
            var name = eff?.DisplayName ?? id;
            var desc = SafeDescription(eff);
            built.Add(new CornerstoneOption(
                id, name, desc, score, rank,
                IsTopRanked: rank == 1,
                NewlyTargetedTags: newTags,
                AffectedBuildings: hits));
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

        // Resolve-shaped cornerstone effects (e.g. RaceResolveEffectEffectModel,
        // GlobalResolveEffectEffectModel) get a flat bonus so they rank above
        // tag-equivalent options that don't actually move the resolve needle.
        // We can't statically pattern-match the family because the resolve
        // sub-effects don't share a common base type, so the typename heuristic
        // is the cheapest reliable signal.
        if (eff != null)
        {
            var typeName = eff.GetType().Name;
            if (typeName.IndexOf("Resolve", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                const double bonus = 1.0;
                total += bonus;
                components.Add(new ScoreComponent(
                    "resolve-shaped effect", bonus,
                    $"effect type {typeName}"));
            }
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

    /// <summary>
    /// Reads the cross-run pick history from the BepInEx config file directly
    /// (so the provider stays decoupled from the SidePanel instance). The
    /// PluginConfig instance is owned by the plugin singleton; we look it up
    /// via reflection to avoid tightening the dependency graph.
    /// </summary>
    private static HashSet<string> ReadPickHistory()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var cfg = StormGuidePlugin.Cfg;
            if (cfg is null) return set;
            foreach (var s in (cfg.CornerstonePickHistory.Value ?? "")
                         .Split(';'))
                if (!string.IsNullOrEmpty(s)) set.Add(s);
        }
        catch { }
        return set;
    }

    /// <summary>
    /// Diff: returns (tags this option introduces that no owned cornerstone
    /// already touches, total currently-built buildings affected by any tag
    /// this option targets). Empty/0 if the option declares no usability tags.
    /// </summary>
    private static (List<string> NewTags, int AffectedBuildings) ComputeDiff(
        EffectModel? eff,
        Dictionary<string, int> tagCounts,
        IReadOnlyDictionary<string, int> ownedTagCounts)
    {
        var newTags = new List<string>();
        var affected = 0;
        ModelTag[]? usability = null;
        try { usability = eff?.usabilityTags; } catch { }
        if (usability == null) return (newTags, 0);
        foreach (var t in usability)
        {
            if (t == null || string.IsNullOrEmpty(t.Name)) continue;
            if (tagCounts.TryGetValue(t.Name, out var c)) affected += c;
            if (!ownedTagCounts.ContainsKey(t.Name)) newTags.Add(t.Name);
        }
        return (newTags, affected);
    }
}
