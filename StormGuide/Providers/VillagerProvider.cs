using StormGuide.Domain;

namespace StormGuide.Providers;

/// <summary>
/// Computes a <see cref="RaceViewModel"/> for a race name. Surfaces the race's
/// best-fit workplaces by joining its characteristics against the building
/// catalog's tags. "Best" = positive perk effect (Proficiency / Comfortable_Job
/// / etc.) on a tag that the building carries.
/// </summary>
public static class VillagerProvider
{
    // Weights live in the shared EffectWeights map so both directions agree.

    public static RaceViewModel For(
        Catalog catalog,
        string raceName,
        Func<string, (float Current, int Target)?>? resolveLookup = null,
        Func<string, int, IReadOnlyList<(string Name, int Resolve, int Stacks)>>? topEffectsLookup = null)
    {
        if (!catalog.Races.TryGetValue(raceName, out var race))
            return RaceViewModel.Missing(raceName);

        // Map: tag → (effect, weight) for this race.
        var raceTagEffects = race.Characteristics
            .Where(c => !string.IsNullOrEmpty(c.BuildingTag) &&
                        !string.IsNullOrEmpty(c.VillagerPerkEffect))
            .Select(c => new
            {
                Tag    = c.BuildingTag,
                Effect = c.VillagerPerkEffect,
                Weight = EffectWeights.For(c.VillagerPerkEffect),
            })
            .ToList();

        if (raceTagEffects.Count == 0)
            return new RaceViewModel(race, Array.Empty<WorkplaceFit>());

        // For each catalog building, see if any of its tags matches a race tag effect.
        var fits = new List<(BuildingInfo b, string tag, string effect, int weight)>();
        foreach (var b in catalog.Buildings.Values)
        {
            if (b.Tags.Count == 0) continue;
            foreach (var t in b.Tags)
            {
                var match = raceTagEffects.FirstOrDefault(x =>
                    string.Equals(x.Tag, t, StringComparison.OrdinalIgnoreCase));
                if (match is null) continue;
                fits.Add((b, t, match.Effect, match.Weight));
                break; // one fit per building
            }
        }

        var sorted = fits
            .OrderByDescending(f => f.weight)
            .ThenBy(f => f.b.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = new List<WorkplaceFit>(sorted.Count);
        var rank = 0; var lastWeight = int.MinValue;
        for (var i = 0; i < sorted.Count; i++)
        {
            var f = sorted[i];
            if (i == 0 || f.weight != lastWeight) rank = i + 1;
            lastWeight = f.weight;
            ranked.Add(new WorkplaceFit(f.b, f.tag, f.effect, rank, IsTopRanked: rank == 1));
        }

        ResolveSnapshot? snapshot = null;
        if (resolveLookup is not null && resolveLookup(raceName) is { } r)
        {
            var topRaw = topEffectsLookup?.Invoke(raceName, 6) ?? Array.Empty<(string, int, int)>();
            var top = topRaw
                .Select(t => new ResolveEffectEntry(t.Name, t.Resolve, t.Stacks))
                .ToList();
            snapshot = new ResolveSnapshot(
                Current:  r.Current,
                Target:   r.Target,
                Min:      (int)race.MinResolve,
                Max:      (int)race.MaxResolve,
                TopEffects: top);
        }

        return new RaceViewModel(race, ranked, snapshot);
    }
}
