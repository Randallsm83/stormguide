using StormGuide.Domain;

namespace StormGuide.Providers;

/// <summary>
/// Computes a <see cref="GoodViewModel"/> for a good by name.
/// Static-only for now; live joins (current stockpile, net rate, current trader
/// rotation) arrive once we have settlement state access.
/// </summary>
public static class GoodProvider
{
    public sealed class TraderLiveQuery
    {
        public string DisplayName { get; init; } = "";
        public bool   IsInVillage { get; init; }
        public IReadOnlyCollection<string> Buys  { get; init; } = Array.Empty<string>();
        public IReadOnlyCollection<string> Sells { get; init; } = Array.Empty<string>();
    }

    public static GoodViewModel For(
        Catalog catalog,
        string goodName,
        Func<TraderLiveQuery?>? currentTrader = null,
        Func<TraderLiveQuery?>? nextTrader    = null,
        Func<string, GoodFlowSnapshot?>? flowLookup = null)
    {
        if (!catalog.Goods.TryGetValue(goodName, out var good))
            return GoodViewModel.Missing(goodName);

        var producers = catalog.RecipesProducing(goodName)
            .Select(r => ScoreProductionPath(catalog, good, r))
            .OrderBy(p => p.cost.Value)
            .ToList();

        var paths = new List<ProductionPath>(producers.Count);
        var rank = 0; var lastCost = double.NaN;
        for (var i = 0; i < producers.Count; i++)
        {
            var (recipe, building, cost) = producers[i];
            if (i == 0 || Math.Abs(cost.Value - lastCost) > 1e-9) rank = i + 1;
            lastCost = cost.Value;
            paths.Add(new ProductionPath(recipe, building, cost, rank, IsCheapest: rank == 1));
        }

        var consumers = catalog.RecipesConsuming(goodName).ToList();
        var racesNeed = catalog.RacesNeeding(goodName).ToList();

        TraderSnapshot? cur = SnapshotFor(currentTrader?.Invoke(), goodName);
        TraderSnapshot? nxt = SnapshotFor(nextTrader?.Invoke(),    goodName);
        var flow = flowLookup?.Invoke(goodName);

        return new GoodViewModel(good, paths, consumers, racesNeed, cur, nxt, flow);
    }

    private static TraderSnapshot? SnapshotFor(TraderLiveQuery? q, string goodName)
    {
        if (q is null) return null;
        return new TraderSnapshot(
            DisplayName:    q.DisplayName,
            IsInVillage:    q.IsInVillage,
            BuysThisGood:   q.Buys.Contains(goodName,  StringComparer.OrdinalIgnoreCase),
            SellsThisGood:  q.Sells.Contains(goodName, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Cost = sum of input good values (TradingSellValue per unit) divided by
    /// produced amount, normalized per unit of output. Lower = cheaper to make.
    /// Components surface the per-input cost so the player sees the math.
    /// </summary>
    private static (RecipeInfo recipe, BuildingInfo? building, Score cost)
        ScoreProductionPath(Catalog catalog, GoodInfo produced, RecipeInfo recipe)
    {
        // Cheapest-pick per slot (using catalog TradingSellValue as a common unit).
        // If a slot has multiple options, we pick the option with the smallest unit cost.
        var components = new List<ScoreComponent>();
        double totalInputCost = 0;
        foreach (var slot in recipe.RequiredGoods)
        {
            ProductionPathSlotPick? best = null;
            foreach (var opt in slot.Options)
            {
                var unit = catalog.Goods.TryGetValue(opt.Good, out var g) ? g.TradingSellValue : 0;
                var slotCost = unit * opt.Amount;
                if (best is null || slotCost < best.SlotCost)
                    best = new ProductionPathSlotPick(opt.Good, opt.Amount, unit, slotCost);
            }
            if (best is null) continue;
            totalInputCost += best.SlotCost;
            components.Add(new ScoreComponent(
                $"{best.Good} \u00d7{best.Amount}",
                best.SlotCost,
                $"@ {best.UnitValue:0.##}/u"));
        }

        var perUnitOut = recipe.ProducedAmount > 0 ? totalInputCost / recipe.ProducedAmount : totalInputCost;
        components.Add(new ScoreComponent(
            "Per output",
            perUnitOut,
            $"= total input cost \u00f7 {recipe.ProducedAmount} produced"));

        var firstBuilding = recipe.Buildings.Count > 0 && catalog.Buildings.TryGetValue(recipe.Buildings[0], out var b)
            ? b : null;

        return (recipe, firstBuilding, new Score(perUnitOut, components, Unit: $"per {produced.DisplayName}"));
    }

    private sealed record ProductionPathSlotPick(string Good, int Amount, double UnitValue, double SlotCost);
}
