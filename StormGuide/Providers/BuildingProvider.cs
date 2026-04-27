using StormGuide.Domain;

namespace StormGuide.Providers;

/// <summary>
/// Computes a <see cref="BuildingViewModel"/> for a building chosen by name.
/// Optional <paramref name="stockpileLookup"/> joins live settlement state
/// (current stockpile per input good) when supplied.
/// </summary>
public static class BuildingProvider
{
    public static BuildingViewModel For(
        Catalog catalog,
        string buildingName,
        Func<string, int>? stockpileLookup = null,
        Func<string, WorkerStatus?>? workersLookup = null,
        Func<string, double>? netFlowLookup = null)
    {
        if (!catalog.Buildings.TryGetValue(buildingName, out var building))
            return BuildingViewModel.Missing(buildingName);

        var isLive = stockpileLookup is not null;
        var workers = workersLookup?.Invoke(buildingName);

        var ranked = building.Recipes
            .Select(name => catalog.Recipes.TryGetValue(name, out var r) ? r : null)
            .Where(r => r is not null)
            .Select(r => ScoreRecipe(r!, stockpileLookup, netFlowLookup))
            .OrderByDescending(t => t.score.Value)
            .ToList();

        var rankings = new List<RecipeRanking>(ranked.Count);
        var rank = 0;
        var lastValue = double.NaN;
        for (var i = 0; i < ranked.Count; i++)
        {
            var (recipe, score, inputs) = ranked[i];
            if (i == 0 || Math.Abs(score.Value - lastValue) > 1e-9) rank = i + 1;
            lastValue = score.Value;
            rankings.Add(new RecipeRanking(recipe, score, rank, IsTopRanked: rank == 1, inputs));
        }

        var raceFits = ComputeRaceFits(catalog, building);

        return new BuildingViewModel(building, rankings, IsLive: isLive,
            Workers: workers, RaceFits: raceFits);
    }

    /// <summary>
    /// For each race, find the strongest characteristic that targets one of
    /// this building's tags. Returns races with at least one matching
    /// characteristic, ranked by the shared <see cref="EffectWeights"/> map.
    /// </summary>
    private static IReadOnlyList<RaceFit> ComputeRaceFits(Catalog catalog, BuildingInfo building)
    {
        if (building.Tags.Count == 0 || catalog.Races.Count == 0)
            return Array.Empty<RaceFit>();

        var buildingTags = new HashSet<string>(building.Tags, StringComparer.OrdinalIgnoreCase);

        var fits = new List<(RaceInfo race, string tag, string effect, int weight)>();
        foreach (var race in catalog.Races.Values)
        {
            (string tag, string effect, int weight)? best = null;
            foreach (var c in race.Characteristics)
            {
                if (string.IsNullOrEmpty(c.BuildingTag) || string.IsNullOrEmpty(c.VillagerPerkEffect)) continue;
                if (!buildingTags.Contains(c.BuildingTag)) continue;
                var w = EffectWeights.For(c.VillagerPerkEffect);
                if (best is null || w > best.Value.weight)
                    best = (c.BuildingTag, c.VillagerPerkEffect, w);
            }
            if (best is not null)
                fits.Add((race, best.Value.tag, best.Value.effect, best.Value.weight));
        }

        var sorted = fits
            .OrderByDescending(f => f.weight)
            .ThenBy(f => f.race.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ranked = new List<RaceFit>(sorted.Count);
        var rank = 0; var lastWeight = int.MinValue;
        for (var i = 0; i < sorted.Count; i++)
        {
            var f = sorted[i];
            if (i == 0 || f.weight != lastWeight) rank = i + 1;
            lastWeight = f.weight;
            ranked.Add(new RaceFit(
                Race:        f.race.Name,
                DisplayName: f.race.DisplayName,
                MatchingTag: f.tag,
                Effect:      f.effect,
                Weight:      f.weight,
                Rank:        rank,
                IsTopRanked: rank == 1));
        }
        return ranked;
    }

    private static (RecipeInfo recipe, Score score, IReadOnlyList<InputAvailability> inputs)
        ScoreRecipe(RecipeInfo r,
                    Func<string, int>? stockpileLookup,
                    Func<string, double>? netFlowLookup = null)
    {
        var perMin = r.ProductionTime > 0 ? (60.0 * r.ProducedAmount) / r.ProductionTime : 0;
        var components = new List<ScoreComponent>
        {
            new("Produced",       r.ProducedAmount, "units / cycle"),
            new("Cycle time",     r.ProductionTime, "sec"),
            new("Throughput",     perMin,           "= 60 × produced ÷ cycle time"),
        };

        var inputs = new List<InputAvailability>();
        if (stockpileLookup is not null)
        {
            // For each input slot, pick the option with the largest stockpile.
            // "At risk" = best option's stockpile is less than 2 cycles' worth.
            foreach (var slot in r.RequiredGoods)
            {
                InputAvailability? best = null;
                foreach (var opt in slot.Options)
                {
                    var stock = stockpileLookup(opt.Good);
                    var atRisk = stock < opt.Amount * 2;
                    var net = netFlowLookup?.Invoke(opt.Good) ?? 0.0;
                    var netNegative = net < -1e-6;
                    var ia = new InputAvailability(opt.Good, opt.Amount, stock, atRisk, netNegative);
                    if (best is null || stock > best.InStock) best = ia;
                }
                if (best is not null) inputs.Add(best);
            }
            if (inputs.Count > 0)
            {
                var risky = inputs.Count(i => i.AtRisk);
                if (risky > 0)
                    components.Add(new ScoreComponent(
                        "Inputs at risk", risky, "stockpile < 2 cycles"));
                var draining = inputs.Count(i => i.NetNegative);
                if (draining > 0)
                    components.Add(new ScoreComponent(
                        "Inputs draining", draining, "net flow < 0 across settlement"));
            }
        }

        return (r, new Score(perMin, components, Unit: "/min"), inputs);
    }
}
