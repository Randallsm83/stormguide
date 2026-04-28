using System;
using System.Collections.Generic;
using System.Linq;
using Eremite;
using Eremite.Buildings;
using Eremite.Controller;
using Eremite.Services;
using UniRx;

namespace StormGuide.Data;

/// <summary>
/// Defensive read-only wrapper over the game's static service entry points.
/// Every accessor is null-safe; if the game isn't loaded yet, you get a
/// sentinel value (null / 0 / empty) rather than an NRE.
/// </summary>
internal static class LiveGameState
{
    public static bool IsReady
        => GameController.Instance is { GameServices: { Loaded: true } };

    public static IGameServices? Services
        => GameController.Instance?.GameServices;

    public static IGameInputService? Input
        => Services?.GameInputService;

    public static IStorageService? Storage
        => Services?.StorageService;

    /// <summary>
    /// Subscribes to the picked-object stream. Returns an IDisposable so the
    /// caller can unsubscribe on teardown. Does nothing (and returns a no-op
    /// disposable) if the game isn't ready.
    /// </summary>
    public static IDisposable SubscribeToPicked(Action<IMapObject?> onPicked)
    {
        var input = Input;
        if (input is null) return Disposable.Empty;
        return ObservableExtensions.Subscribe(input.PickedObject, onPicked);
    }

    /// <summary>Current stockpile of <paramref name="goodName"/>; 0 if unavailable.</summary>
    public static int StockpileOf(string goodName)
    {
        try { return Storage?.GetStorage()?.GetAmount(goodName) ?? 0; }
        catch { return 0; }
    }

    /// <summary>If the picked object is a Building, returns its catalog model name.</summary>
    public static string? AsBuildingModelName(IMapObject? obj) =>
        obj is Building b ? b.ModelName : null;

    public sealed record WorkersStatus(int Assigned, int Capacity, bool Idle);

    /// <summary>Live worker assignment + idle status for a building model name; null if not a production building.</summary>
    public static WorkersStatus? WorkersFor(string buildingModelName)
    {
        try
        {
            var bs = Services?.BuildingsService;
            if (bs == null) return null;
            // Find any built ProductionBuilding matching the model name.
            foreach (var kv in bs.Buildings)
            {
                var b = kv.Value;
                if (b == null || b.ModelName != buildingModelName) continue;
                if (b is Eremite.Buildings.ProductionBuilding pb)
                {
                    var workers = pb.Workers;
                    var assigned = workers == null ? 0 : workers.Count(w => w > 0);
                    var capacity = workers?.Length ?? 0;
                    return new WorkersStatus(assigned, capacity, pb.IsIdle);
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Count of every built building, grouped by catalog model name. Used by
    /// the Villagers tab housing indicator (catalog-only Domain helper joins
    /// the result against the race's preferred-housing need). Empty when the
    /// game services aren't loaded.
    /// </summary>
    public static IReadOnlyDictionary<string, int> BuiltBuildingCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var bs = Services?.BuildingsService;
            if (bs?.Buildings == null) return counts;
            foreach (var kv in bs.Buildings)
            {
                var b = kv.Value;
                if (b == null || string.IsNullOrEmpty(b.ModelName)) continue;
                counts.TryGetValue(b.ModelName, out var n);
                counts[b.ModelName] = n + 1;
            }
        }
        catch { }
        return counts;
    }

    /// <summary>List of (modelName, displayName) for production buildings currently flagged idle.</summary>
    public static IReadOnlyList<(string ModelName, string DisplayName)> IdleBuildings()
    {
        var list = new List<(string, string)>();
        try
        {
            var bs = Services?.BuildingsService;
            if (bs == null) return list;
            foreach (var kv in bs.Buildings)
            {
                var b = kv.Value;
                if (b is Eremite.Buildings.ProductionBuilding pb && pb.IsIdle)
                    list.Add((b.ModelName, b.DisplayName ?? b.ModelName));
            }
        }
        catch { }
        return list;
    }

    public sealed record FlowContribution(
        string  BuildingName,    // display name
        string  RecipeName,      // display name
        double  PerMin,
        bool    IsProducer);     // false = consumer

    public sealed record GoodFlow(
        double ProducedPerMin,
        double ConsumedPerMin,
        IReadOnlyList<FlowContribution> Contributions)
    {
        public double Net => ProducedPerMin - ConsumedPerMin;
        public bool   IsNetNegative => Net < -1e-6;
    }

    /// <summary>
    /// Approximate good-flow rate from currently-active recipes across all
    /// production buildings. Uses our trimmed catalog for recipe input/output
    /// shape; the live data is the per-workplace assignment.
    /// </summary>
    public static GoodFlow FlowFor(string goodName, StormGuide.Domain.Catalog catalog)
    {
        double produced = 0, consumed = 0;
        var contribs = new Dictionary<(string, string, bool), double>();
        try
        {
            var bs = Services?.BuildingsService;
            if (bs == null) return new GoodFlow(0, 0, Array.Empty<FlowContribution>());
            foreach (var kv in bs.Buildings)
            {
                if (kv.Value is not Eremite.Buildings.ProductionBuilding pb) continue;
                var workers = pb.Workers;
                if (workers == null) continue;
                var bDisplay = pb.DisplayName ?? pb.ModelName;
                for (var i = 0; i < workers.Length; i++)
                {
                    if (workers[i] <= 0) continue;
                    Eremite.Buildings.RecipeModel? recipe = null;
                    try { recipe = pb.GetCurrentRecipeFor(i); } catch { }
                    if (recipe is null) continue;
                    if (!catalog.Recipes.TryGetValue(recipe.Name, out var info)) continue;
                    var perMin = info.ProductionTime > 0
                        ? (60.0 * info.ProducedAmount) / info.ProductionTime
                        : 0;
                    if (string.Equals(info.ProducedGood, goodName, StringComparison.Ordinal))
                    {
                        produced += perMin;
                        var key = (bDisplay, info.DisplayName, true);
                        contribs.TryGetValue(key, out var prev);
                        contribs[key] = prev + perMin;
                    }
                    foreach (var slot in info.RequiredGoods)
                    {
                        if (!slot.Options.Any(o => o.Good == goodName)) continue;
                        var amt = slot.Options.First(o => o.Good == goodName).Amount;
                        var ratePerMin = info.ProductionTime > 0
                            ? (60.0 * amt) / info.ProductionTime
                            : 0;
                        var slotContribution = ratePerMin / Math.Max(1, slot.Options.Count);
                        consumed += slotContribution;
                        var key = (bDisplay, info.DisplayName, false);
                        contribs.TryGetValue(key, out var prev);
                        contribs[key] = prev + slotContribution;
                    }
                }
            }
        }
        catch { }

        var rows = contribs
            .Select(kv => new FlowContribution(kv.Key.Item1, kv.Key.Item2, kv.Value, kv.Key.Item3))
            .OrderByDescending(c => c.IsProducer)
            .ThenByDescending(c => c.PerMin)
            .ToList();

        return new GoodFlow(produced, consumed, rows);
    }

    public sealed record SettlementAlerts(
        int IdleWorkshops,
        int RacesBelowTarget,
        IReadOnlyList<(string Good, double RunwayMinutes)> GoodsAtRisk);

    /// <summary>
    /// Single-pass aggregate of the most pressing live signals: idle
    /// workshops, races whose resolve is below their reputation target, and
    /// goods whose stockpile would deplete in &lt; <paramref name="thresholdMinutes"/>
    /// at the current burn rate. Defaults to 5 minutes.
    /// </summary>
    public static SettlementAlerts? AlertsFor(
        StormGuide.Domain.Catalog catalog, double thresholdMinutes = 5.0)
    {
        var services = Services;
        if (services is not { Loaded: true }) return null;

        // Idle workshops + flow accumulation in a single building-pass.
        var idleCount = 0;
        var produced = new Dictionary<string, double>(StringComparer.Ordinal);
        var consumed = new Dictionary<string, double>(StringComparer.Ordinal);
        try
        {
            var buildings = services.BuildingsService?.Buildings;
            if (buildings != null)
                foreach (var kv in buildings)
                {
                    if (kv.Value is not Eremite.Buildings.ProductionBuilding pb) continue;
                    if (pb.IsIdle) idleCount++;
                    var workers = pb.Workers;
                    if (workers == null) continue;
                    for (var i = 0; i < workers.Length; i++)
                    {
                        if (workers[i] <= 0) continue;
                        Eremite.Buildings.RecipeModel? recipe = null;
                        try { recipe = pb.GetCurrentRecipeFor(i); } catch { }
                        if (recipe == null) continue;
                        if (!catalog.Recipes.TryGetValue(recipe.Name, out var info)) continue;
                        var perMin = info.ProductionTime > 0
                            ? (60.0 * info.ProducedAmount) / info.ProductionTime
                            : 0;
                        if (!string.IsNullOrEmpty(info.ProducedGood))
                        {
                            produced.TryGetValue(info.ProducedGood, out var pv);
                            produced[info.ProducedGood] = pv + perMin;
                        }
                        foreach (var slot in info.RequiredGoods)
                        {
                            if (slot.Options.Count == 0) continue;
                            // Distribute consumption across multi-option slots
                            // (we don't know the worker's actual choice).
                            foreach (var opt in slot.Options)
                            {
                                var rate = info.ProductionTime > 0
                                    ? (60.0 * opt.Amount) / info.ProductionTime
                                    : 0;
                                consumed.TryGetValue(opt.Good, out var cv);
                                consumed[opt.Good] = cv + rate / slot.Options.Count;
                            }
                        }
                    }
                }
        }
        catch { }

        // Goods at risk: net negative AND stockpile would last < 5 minutes.
        var risks = new List<(string Good, double Runway)>();
        var allGoods = new HashSet<string>(produced.Keys, StringComparer.Ordinal);
        foreach (var k in consumed.Keys) allGoods.Add(k);
        foreach (var name in allGoods)
        {
            produced.TryGetValue(name, out var p);
            consumed.TryGetValue(name, out var c);
            var net = p - c;
            if (net >= -1e-6) continue;
            var stock = StockpileOf(name);
            if (stock <= 0) continue;
            var runwayMin = stock / -net;
            if (runwayMin < thresholdMinutes)
                risks.Add((name, runwayMin));
        }
        risks.Sort((a, b) => a.Runway.CompareTo(b.Runway));

        // Races below target resolve.
        var racesBelow = 0;
        try
        {
            var vs = services.VillagersService;
            var rs = services.ResolveService;
            if (vs?.Races != null && rs != null)
            {
                foreach (var raceName in vs.Races.Keys)
                {
                    if (vs.GetAliveRaceAmount(raceName) <= 0) continue;
                    var current = rs.GetResolveFor(raceName);
                    var target  = rs.GetTargetResolveFor(raceName);
                    if (current < target) racesBelow++;
                }
            }
        }
        catch { }

        return new SettlementAlerts(idleCount, racesBelow, risks);
    }

    /// <summary>
    /// Current in-game time as reported by <see cref="IGameTimeService.Time"/>.
    /// This is the same clock that drives <c>GladeState.rewardChaseStart/End</c>,
    /// so it can be used for countdowns. Null if unavailable.
    /// </summary>
    public static float? GameTimeNow()
    {
        try { return Services?.GameTimeService?.Time; }
        catch { return null; }
    }

    /// <summary>Travel progress (0..1) of the current main trader, or null if unavailable.</summary>
    public static float? CurrentTraderTravelProgress()
    {
        try
        {
            var visit = Services?.TradeService?.GetCurrentMainVisit();
            if (visit == null) return null;
            return UnityEngine.Mathf.Clamp01(visit.travelProgress);
        }
        catch { return null; }
    }

    public sealed record OwnedCornerstone(string Id, string DisplayName, string Description);

    /// <summary>
    /// Currently-active cornerstones the player owns, resolved into display
    /// info via the game's model service. Empty when unavailable.
    /// </summary>
    public static IReadOnlyList<OwnedCornerstone> OwnedCornerstones()
    {
        var list = new List<OwnedCornerstone>();
        try
        {
            var state = Services?.StateService?.Cornerstones;
            var ids = state?.activeCornerstones;
            if (ids == null || ids.Count == 0) return list;
            var ms = Services?.GameModelService;
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                Eremite.Model.EffectModel? eff = null;
                try { eff = ms?.GetEffect(id); } catch { }
                var name = eff?.DisplayName ?? id;
                var desc = "";
                try { desc = eff?.Description ?? ""; } catch { }
                list.Add(new OwnedCornerstone(id, name, desc));
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Subscribes to the cornerstone-popup event. <paramref name="onShown"/> is
    /// invoked each time the popup is requested. No-op if the game isn't ready.
    /// </summary>
    public static IDisposable SubscribeToCornerstonePopup(Action onShown)
    {
        var bb = Services?.GameBlackboardService;
        if (bb == null) return Disposable.Empty;
        return ObservableExtensions.Subscribe(bb.OnRewardsPopupRequested, _ => onShown());
    }

    /// <summary>(current, target) resolve for a race, or null if unavailable.</summary>
    public static (float Current, int Target)? ResolveFor(string raceName)
    {
        var rs = Services?.ResolveService;
        if (rs == null) return null;
        try { return (rs.GetResolveFor(raceName), rs.GetTargetResolveFor(raceName)); }
        catch { return null; }
    }

    public sealed class TraderInfo
    {
        public string DisplayName { get; init; } = "";
        public bool   IsInVillage { get; init; }
        public IReadOnlyCollection<string> Buys  { get; init; } = Array.Empty<string>();
        public IReadOnlyCollection<string> Sells { get; init; } = Array.Empty<string>();
    }

    /// <summary>The current main trader (whether arrived or en-route). Null if unavailable.</summary>
    public static TraderInfo? CurrentTrader()
    {
        var ts = Services?.TradeService;
        if (ts == null) return null;
        try
        {
            var trader = ts.GetCurrentMainTrader();
            if (trader == null) return null;
            return new TraderInfo
            {
                DisplayName = SafeText(trader.displayName),
                IsInVillage = ts.IsMainTraderInTheVillage(),
                Buys  = NamesOf(trader.desiredGoods),
                Sells = NamesOfOfferedGoods(trader),
            };
        }
        catch { return null; }
    }

    /// <summary>The next main trader scheduled to arrive. Null if unavailable.</summary>
    public static TraderInfo? NextTrader()
    {
        var ts = Services?.TradeService;
        if (ts == null) return null;
        try
        {
            var trader = ts.GetNextMainTrader();
            if (trader == null) return null;
            return new TraderInfo
            {
                DisplayName = SafeText(trader.displayName),
                IsInVillage = false,
                Buys  = NamesOf(trader.desiredGoods),
                Sells = NamesOfOfferedGoods(trader),
            };
        }
        catch { return null; }
    }

    private static string SafeText(Eremite.Model.LocaText? lt)
    {
        if (lt is null) return "";
        try { return lt.Text ?? ""; } catch { return ""; }
    }

    private static HashSet<string> NamesOf(Eremite.Model.GoodModel[]? goods)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (goods == null) return s;
        foreach (var g in goods)
            if (g != null && !string.IsNullOrEmpty(g.Name)) s.Add(g.Name);
        return s;
    }

    private static HashSet<string> NamesOfOfferedGoods(Eremite.Model.Trade.TraderModel trader)
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (trader.guaranteedOfferedGoods != null)
            foreach (var r in trader.guaranteedOfferedGoods)
                if (r != null && !string.IsNullOrEmpty(r.Name)) s.Add(r.Name);
        if (trader.offeredGoods != null)
            foreach (var w in trader.offeredGoods)
                if (w != null && !string.IsNullOrEmpty(w.Name)) s.Add(w.Name);
        return s;
    }

    /// <summary>
    /// Per-unit currency value the player gets selling <paramref name="goodName"/>
    /// at the current trader (uses live multipliers). 0 if unavailable.
    /// </summary>
    public static float SellValueAtCurrentTrader(string goodName)
    {
        var ts = Services?.TradeService;
        if (ts == null) return 0f;
        try { return ts.GetValueInCurrency(goodName, 1); }
        catch { return 0f; }
    }

    /// <summary>
    /// Per-unit currency cost when buying <paramref name="goodName"/> from the
    /// current trader. 0 if unavailable.
    /// </summary>
    public static float BuyValueAtCurrentTrader(string goodName)
    {
        var ts = Services?.TradeService;
        if (ts == null) return 0f;
        try { return ts.GetBuyValueInCurrency(goodName, 1); }
        catch { return 0f; }
    }

    /// <summary>
    /// Build a snapshot of village race composition for the Villagers tab.
    /// Returns null if the village isn't loaded yet.
    /// </summary>
    public static StormGuide.Domain.VillageSummary? VillageSummary(
        Func<string, string> displayNameOf)
    {
        var vs = Services?.VillagersService;
        if (vs == null) return null;
        try
        {
            var races = vs.Races;
            if (races == null || races.Count == 0) return null;
            var list = new List<StormGuide.Domain.RacePresence>(races.Count);
            foreach (var kv in races)
            {
                var raceName = kv.Key;
                var alive = vs.GetAliveRaceAmount(raceName);
                var total = vs.GetRaceAmount(raceName);
                var homeless = 0;
                try { homeless = vs.GetHomelessAmount(raceName); } catch { }
                var resolve = ResolveFor(raceName) ?? (0f, 0);
                list.Add(new StormGuide.Domain.RacePresence(
                    raceName,
                    displayNameOf(raceName) ?? raceName,
                    alive, total, homeless,
                    resolve.Item1, resolve.Item2));
            }
            list.Sort((a, b) => b.Alive.CompareTo(a.Alive));
            return new StormGuide.Domain.VillageSummary(vs.VillagerCount, list);
        }
        catch { return null; }
    }

    /// <summary>
    /// Top resolve effects for a race, sorted by absolute impact descending.
    /// Each tuple = (display name, resolve per stack, stack count).
    /// </summary>
    public static IReadOnlyList<(string Name, int Resolve, int Stacks)> TopResolveEffectsFor(
        string raceName, int max = 6)
    {
        var rs = Services?.ResolveService;
        if (rs?.Effects == null) return Array.Empty<(string, int, int)>();
        try
        {
            if (!rs.Effects.TryGetValue(raceName, out var effects) || effects == null)
                return Array.Empty<(string, int, int)>();

            return effects
                .Where(kv => kv.Key != null && kv.Value > 0)
                .Select(kv =>
                {
                    string name;
                    try { name = kv.Key.displayName?.Text ?? kv.Key.name ?? "(unnamed)"; }
                    catch { name = "(unnamed)"; }
                    return (name, kv.Key.resolve, kv.Value);
                })
                .OrderByDescending(t => Math.Abs(t.resolve * t.Value))
                .Take(max)
                .ToList();
        }
        catch { return Array.Empty<(string, int, int)>(); }
    }

    // ---- Trader desires ---------------------------------------------------

    /// <summary>
    /// One row in a trader desires ranking: per-unit price, current stockpile,
    /// and the implied total value if you sold every unit at this trader.
    /// </summary>
    public sealed record TraderDesire(
        string Good,
        string DisplayName,
        float  PricePerUnit,   // live multiplier when current+in-village; else catalog base
        int    Stockpile,
        float  TotalValue);    // = PricePerUnit × Stockpile

    /// <summary>
    /// Ranks the goods a trader wants by total settlement value (price × stockpile).
    /// For the current trader (in-village), uses live currency multipliers via
    /// <see cref="ITradeService.GetValueInCurrency"/>. Otherwise falls back to the
    /// catalog's base <c>TradingBuyValue</c>.
    /// </summary>
    public static IReadOnlyList<TraderDesire> RankTraderDesires(
        TraderInfo? trader, StormGuide.Domain.Catalog catalog, bool isCurrent)
    {
        if (trader is null || trader.Buys.Count == 0)
            return Array.Empty<TraderDesire>();
        var ts = Services?.TradeService;
        var canQueryLive = ts != null && isCurrent && trader.IsInVillage;
        var list = new List<TraderDesire>(trader.Buys.Count);
        foreach (var name in trader.Buys)
        {
            float price = 0f;
            if (canQueryLive)
            {
                try { price = ts!.GetValueInCurrency(name, 1); } catch { }
            }
            if (price <= 0f && catalog.Goods.TryGetValue(name, out var gi))
                price = (float)gi.TradingBuyValue;
            var stock = StockpileOf(name);
            var disp  = catalog.Goods.TryGetValue(name, out var info) ? info.DisplayName : name;
            list.Add(new TraderDesire(name, disp, price, stock, price * stock));
        }
        return list
            .OrderByDescending(d => d.TotalValue)
            .ThenByDescending(d => d.PricePerUnit)
            .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---- Active orders ----------------------------------------------------

    public sealed record OrderObjective(
        string Description,
        int    Amount,        // current progress count from ObjectiveState
        bool   Completed);

    public sealed record OrderRewardEntry(
        string Id,
        string DisplayName,
        string Category,      // "reputation" | "cornerstone" | "goods" | "resolve" | "other"
        double Weight);       // contributes to OrderInfo.RewardScore

    public sealed record OrderInfo(
        int    Id,
        string ModelName,
        string DisplayName,
        bool   Picked,
        bool   Tracked,
        bool   IsFailed,
        bool   ShouldBeFailable,
        float  TimeLeft,           // seconds; only meaningful if ShouldBeFailable
        string Tier,
        IReadOnlyList<OrderObjective>    Objectives,
        IReadOnlyList<OrderRewardEntry>  Rewards,
        double RewardScore,
        IReadOnlyList<string>            RewardCategories);  // distinct categories present

    /// <summary>
    /// In-flight orders the player can complete. Failed orders are filtered.
    /// Resolves model + reward display names defensively via
    /// <c>MainController.Settings.GetOrder</c> and <c>GameModelService.GetEffect</c>.
    /// </summary>
    public static IReadOnlyList<OrderInfo> ActiveOrders()
    {
        var list = new List<OrderInfo>();
        try
        {
            var os = Services?.OrdersService;
            if (os?.Orders == null) return list;
            Eremite.Model.Settings? settings = null;
            try { settings = MainController.Instance?.Settings; } catch { }
            var ms = Services?.GameModelService;

            foreach (var s in os.Orders)
            {
                if (s == null || s.isFailed) continue;

                Eremite.Model.Orders.OrderModel? model = null;
                try { model = settings?.GetOrder(s.model); } catch { }
                var disp = model is not null ? SafeText(model.displayName) : (s.model ?? "");
                if (string.IsNullOrEmpty(disp)) disp = s.model ?? "(unnamed)";

                // Objectives: pair OrderModel.logics[i] with OrderState.objectives[i].
                var objs = new List<OrderObjective>();
                try
                {
                    var logics = model?.GetLogics(s.setIndex);
                    var states = s.objectives;
                    if (logics != null)
                    {
                        for (var i = 0; i < logics.Length; i++)
                        {
                            var l = logics[i];
                            if (l == null) continue;
                            var st = (states != null && i < states.Length) ? states[i] : null;

                            // Try the game's own rich-text renderer first; it
                            // produces "5/10" or "DONE" depending on logic type.
                            string? rich = null;
                            if (st != null)
                            {
                                try { rich = l.GetObjectiveText(st); } catch { }
                            }
                            string? name = null;
                            try { name = l.DisplayName; } catch { }

                            var description = !string.IsNullOrEmpty(rich)
                                ? rich!
                                : (name ?? "(unnamed objective)");
                            objs.Add(new OrderObjective(
                                Description: description,
                                Amount:      st?.amount ?? 0,
                                Completed:   st?.completed ?? false));
                        }
                    }
                }
                catch { }

                // Rewards: resolve effect display name + categorise by typename.
                var rewards = new List<OrderRewardEntry>();
                var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                double rewardScore = 0;
                if (s.rewards != null)
                {
                    foreach (var rid in s.rewards)
                    {
                        if (string.IsNullOrEmpty(rid)) continue;
                        string? rname = null;
                        Eremite.Model.EffectModel? eff = null;
                        try { eff = ms?.GetEffect(rid); } catch { }
                        try { rname = eff?.DisplayName; } catch { }
                        var (category, weight) = CategoriseRewardEffect(eff);
                        rewardScore += weight;
                        categories.Add(category);
                        rewards.Add(new OrderRewardEntry(
                            Id:          rid,
                            DisplayName: string.IsNullOrEmpty(rname) ? rid : rname!,
                            Category:    category,
                            Weight:      weight));
                    }
                }

                list.Add(new OrderInfo(
                    Id:               s.id,
                    ModelName:        s.model ?? "",
                    DisplayName:      disp,
                    Picked:           s.picked,
                    Tracked:          s.tracked,
                    IsFailed:         s.isFailed,
                    ShouldBeFailable: s.shouldBeFailable,
                    TimeLeft:         s.timeLeft,
                    Tier:             s.tierModel ?? "",
                    Objectives:       objs,
                    Rewards:          rewards,
                    RewardScore:      rewardScore,
                    RewardCategories: categories.ToList()));
            }
        }
        catch { }
        return list;
    }

    public sealed record OrderPickOption(
        string SetIndexLabel,                  // "set 0", "set 1", …
        bool   Failed,                         // already picked or otherwise locked
        IReadOnlyList<OrderRewardEntry> Rewards,
        double RewardScore,
        IReadOnlyList<string> RewardCategories,
        bool   IsTopRanked);                   // best non-failed pick by RewardScore

    /// <summary>
    /// Resolves the pick options the player can choose between for an unpicked
    /// order. Each option's rewards are categorised and scored using the same
    /// <see cref="CategoriseRewardEffect"/> heuristics as the order itself, and
    /// the highest-scored non-failed option is flagged <c>IsTopRanked</c>.
    /// </summary>
    public static IReadOnlyList<OrderPickOption> PickOptionsFor(int orderId)
    {
        var list = new List<OrderPickOption>();
        try
        {
            var os = Services?.OrdersService;
            if (os?.Orders == null) return list;
            Eremite.Model.Orders.OrderState? state = null;
            foreach (var o in os.Orders) if (o != null && o.id == orderId) { state = o; break; }
            if (state?.picks == null || state.picks.Count == 0) return list;
            var ms = Services?.GameModelService;

            // First pass: build entries.
            var working = new List<(int idx, bool failed, List<OrderRewardEntry> r, double s, HashSet<string> cats)>(state.picks.Count);
            for (var i = 0; i < state.picks.Count; i++)
            {
                var p = state.picks[i];
                var rewards = new List<OrderRewardEntry>();
                var cats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                double score = 0;
                if (p?.rewards != null)
                {
                    foreach (var rid in p.rewards)
                    {
                        if (string.IsNullOrEmpty(rid)) continue;
                        Eremite.Model.EffectModel? eff = null;
                        string? rname = null;
                        try { eff = ms?.GetEffect(rid); } catch { }
                        try { rname = eff?.DisplayName; } catch { }
                        var (category, weight) = CategoriseRewardEffect(eff);
                        score += weight;
                        cats.Add(category);
                        rewards.Add(new OrderRewardEntry(
                            Id:          rid,
                            DisplayName: string.IsNullOrEmpty(rname) ? rid : rname!,
                            Category:    category,
                            Weight:      weight));
                    }
                }
                working.Add((i, p?.failed ?? false, rewards, score, cats));
            }

            // Top rank: highest score among non-failed picks.
            var topScore = working.Where(w => !w.failed).Select(w => w.s)
                                  .DefaultIfEmpty(double.NaN).Max();
            foreach (var w in working)
            {
                var isTop = !w.failed && !double.IsNaN(topScore) &&
                            Math.Abs(w.s - topScore) < 1e-9;
                list.Add(new OrderPickOption(
                    SetIndexLabel:    $"set {w.idx}",
                    Failed:           w.failed,
                    Rewards:          w.r,
                    RewardScore:      w.s,
                    RewardCategories: w.cats.ToList(),
                    IsTopRanked:      isTop));
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Categorises a reward effect by typename heuristics into a coarse bucket
    /// and assigns a synergy weight. Reputation/cornerstone are valued highest
    /// since they’re the rarest/strongest order rewards; resolve and goods are
    /// medium; everything else falls to a baseline.
    /// </summary>
    private static (string Category, double Weight) CategoriseRewardEffect(
        Eremite.Model.EffectModel? eff)
    {
        if (eff == null) return ("other", 1);
        var typeName = eff.GetType().Name;
        if (typeName.IndexOf("Reputation", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("reputation", 10);
        if (typeName.IndexOf("Cornerstone", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("cornerstone", 10);
        if (typeName.IndexOf("Resolve", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("resolve", 5);
        if (typeName.IndexOf("Goods", StringComparison.OrdinalIgnoreCase) >= 0
            || typeName.IndexOf("Reward", StringComparison.OrdinalIgnoreCase) >= 0)
            return ("goods", 5);
        return ("other", 1);
    }

    // ---- Glade summary ----------------------------------------------------

    public sealed record GladeRewardChase(
        string Model,
        float  Start,
        float  End,
        IReadOnlyList<string> Rewards)
    {
        public float Duration => Math.Max(0f, End - Start);
    }

    public sealed record GladeSummary(
        int Total,
        int Discovered,
        int Dangerous,
        int Forbidden,
        int RewardChasesActive,
        IReadOnlyList<GladeRewardChase> Chases);

    /// <summary>
    /// Aggregate glade counts: total spawned, discovered, dangerous/forbidden
    /// counts, and how many have an active reward-chase timer running. Returns
    /// null when the service isn't ready.
    /// </summary>
    public static GladeSummary? GladeSummaryFor()
    {
        var gs = Services?.GladesService;
        if (gs == null) return null;
        try
        {
            var glades = gs.Glades;
            if (glades == null) return null;
            var total = 0; var discovered = 0; var dangerous = 0;
            var forbidden = 0; var chases = 0;
            var chaseList = new List<GladeRewardChase>();
            foreach (var g in glades)
            {
                if (g == null) continue;
                total++;
                if (g.wasDiscovered) discovered++;
                try { if (gs.IsDangerous(g)) dangerous++; } catch { }
                try { if (gs.IsForbidden(g)) forbidden++; } catch { }
                if (g.hasRewardChase)
                {
                    chases++;
                    // Best-effort: peek `rewards` (string[]) via reflection so
                    // we can preview them without taking a hard dependency on
                    // the field name surviving game updates.
                    var rewards = new List<string>();
                    try
                    {
                        var rf = g.GetType().GetField("rewards");
                        if (rf?.GetValue(g) is System.Collections.IEnumerable raw)
                        {
                            foreach (var r in raw)
                            {
                                if (r is string s && !string.IsNullOrEmpty(s)) rewards.Add(s);
                            }
                        }
                    }
                    catch { }
                    chaseList.Add(new GladeRewardChase(
                        Model:   g.model ?? "glade",
                        Start:   g.rewardChaseStart,
                        End:     g.rewardChaseEnd,
                        Rewards: rewards));
                }
            }
            return new GladeSummary(total, discovered, dangerous, forbidden, chases, chaseList);
        }
        catch { return null; }
    }

    // ---- Embedded docs ----------------------------------------------------

    /// <summary>
    /// Reads a top-level doc embedded into the plugin assembly (README.md /
    /// AGENTS.md). Returns an empty string if the resource is missing.
    /// </summary>
    public static string ReadEmbeddedDoc(string fileName)
    {
        try
        {
            var asm = typeof(LiveGameState).Assembly;
            using var stream = asm.GetManifestResourceStream(
                "StormGuide.Resources.docs." + fileName);
            if (stream == null) return "";
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch { return ""; }
    }

    // ---- Helper: usability tags from owned cornerstones -------------------

    // ---- Hearth fuel runway ----------------------------------------------

    public sealed record FuelRunway(
        int Stockpile,                       // total units of all fuel goods
        IReadOnlyList<(string Good, int Stock)> ByGood,
        // Estimated rate at which fuel is consumed (per minute). Heuristic
        // when no live HearthService.fuelRate is exposed; ~6/min is the
        // typical clear-weather hearth rate (1 unit / 10s).
        double EstimatedBurnPerMinute,
        double RunwayMinutes,
        bool   IsLiveBurnRate);              // true when sourced from live service

    /// <summary>
    /// Aggregates fuel-good stockpile and a best-effort burn-rate estimate.
    /// Returns null if the catalog isn't loaded or the storage service is gone.
    /// </summary>
    public static FuelRunway? FuelRunwayFor(StormGuide.Domain.Catalog catalog)
    {
        if (catalog.IsEmpty) return null;
        if (Storage == null) return null;
        var byGood = new List<(string, int)>();
        var total  = 0;
        try
        {
            foreach (var gi in catalog.Goods.Values)
            {
                if (!gi.CanBeBurned) continue;
                var s = StockpileOf(gi.Name);
                if (s <= 0) continue;
                total += s;
                byGood.Add((gi.DisplayName, s));
            }
        }
        catch { }
        byGood.Sort((a, b) => b.Item2.CompareTo(a.Item2));

        // Try the live hearth service via reflection. The base game's
        // HearthService doesn't expose a public per-minute rate but most
        // hearths burn 1 fuel / 10s in clear weather; storms ~3x that.
        double burnPerMin = 6.0;
        var isLive = false;
        try
        {
            var hs = Services?.GetType().GetProperty("HearthService")?.GetValue(Services);
            var rate = hs?.GetType().GetProperty("FuelPerSecond")?.GetValue(hs);
            if (rate is float f and > 0f) { burnPerMin = f * 60.0; isLive = true; }
        }
        catch { }
        var runway = burnPerMin > 0 ? total / burnPerMin : 0;
        return new FuelRunway(total, byGood, burnPerMin, runway, isLive);
    }

    // ---- Weather / storm phase ------------------------------------------

    /// <summary>
    /// Snapshot of the current weather phase. Reflection-driven because the
    /// game's WeatherService internals aren't part of any documented contract
    /// we depend on; prefer surfacing what we can find and degrade silently.
    /// </summary>
    public sealed record WeatherStatus(
        string  PhaseName,           // "Clearance", "Drizzle", "Storm", or ""
        float?  SecondsLeft);        // null if unknown

    /// <summary>
    /// Best-effort current weather phase + seconds-left. Probes the
    /// <c>IGameServices.WeatherService</c> shape via reflection. Returns null
    /// when the service can't be located.
    /// </summary>
    public static WeatherStatus? Weather()
    {
        try
        {
            var svc = Services?.GetType().GetProperty("WeatherService")?.GetValue(Services);
            if (svc == null) return null;
            var t = svc.GetType();
            string phase = "";
            // Common shapes: enum CurrentSeason, string CurrentPhase, etc.
            foreach (var prop in new[] { "CurrentPhase", "CurrentSeason", "Season", "Phase" })
            {
                var p = t.GetProperty(prop);
                if (p == null) continue;
                var v = p.GetValue(svc);
                if (v != null) { phase = v.ToString() ?? ""; break; }
            }
            float? secsLeft = null;
            foreach (var prop in new[] { "SecondsToNextPhase", "TimeLeft", "PhaseTimeLeft", "SecondsLeft" })
            {
                var p = t.GetProperty(prop);
                if (p == null) continue;
                var v = p.GetValue(svc);
                if (v is float f) { secsLeft = f; break; }
            }
            if (string.IsNullOrEmpty(phase) && secsLeft is null) return null;
            return new WeatherStatus(phase, secsLeft);
        }
        catch { return null; }
    }

    /// <summary>
    /// Best-effort "focus this order" via reflection — newer game versions
    /// expose <c>OrdersService.HighlightOrder</c> or similar. Returns true if
    /// any matching method/field was invoked.
    /// </summary>
    public static bool FocusOrderInGame(int orderId)
    {
        try
        {
            var os = Services?.OrdersService;
            if (os == null) return false;
            var t = os.GetType();
            foreach (var name in new[] { "FocusOrder", "HighlightOrder", "SelectOrder" })
            {
                var m = t.GetMethod(name);
                if (m == null) continue;
                var prm = m.GetParameters();
                if (prm.Length == 1 && prm[0].ParameterType == typeof(int))
                {
                    m.Invoke(os, new object[] { orderId });
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Like <see cref="IdleBuildings"/> but extends to any settled building
    /// with worker slots (woodcutters, haulers, services) that report idle.
    /// Falls back to reflection for non-ProductionBuilding worker buildings.
    /// </summary>
    public static IReadOnlyList<(string ModelName, string DisplayName)> IdleAnyBuildings()
    {
        var list = new List<(string, string)>();
        try
        {
            var bs = Services?.BuildingsService;
            if (bs == null) return list;
            foreach (var kv in bs.Buildings)
            {
                var b = kv.Value;
                if (b == null) continue;
                bool idle = false;
                if (b is Eremite.Buildings.ProductionBuilding pb)
                {
                    idle = pb.IsIdle;
                }
                else
                {
                    // Reflect on common idle/worker shapes for non-production buildings.
                    try
                    {
                        var p = b.GetType().GetProperty("IsIdle");
                        if (p?.GetValue(b) is bool i && i) idle = true;
                    }
                    catch { }
                }
                if (idle) list.Add((b.ModelName, b.DisplayName ?? b.ModelName));
            }
        }
        catch { }
        return list;
    }

    public sealed record IdleBuildingReason(
        string ModelName,
        string DisplayName,
        string Reason);

    /// <summary>
    /// Idle buildings with a best-effort root cause. Distinguishes:
    ///   • "unstaffed"     — worker slots are empty
    ///   • "no inputs"     — current recipe's required goods all at 0 stock
    ///   • "output full"   — produced good's stockpile is high (storage-full guess)
    ///   • "paused"        — game-side pause flag
    ///   • ""              — unknown / no signal
    /// </summary>
    public static IReadOnlyList<IdleBuildingReason> IdleAnyBuildingsWithReasons(
        StormGuide.Domain.Catalog catalog)
    {
        var list = new List<IdleBuildingReason>();
        try
        {
            var bs = Services?.BuildingsService;
            if (bs == null) return list;
            foreach (var kv in bs.Buildings)
            {
                var b = kv.Value;
                if (b == null) continue;
                bool idle = false;
                if (b is Eremite.Buildings.ProductionBuilding pb) idle = pb.IsIdle;
                else
                {
                    try
                    {
                        var p = b.GetType().GetProperty("IsIdle");
                        if (p?.GetValue(b) is bool i && i) idle = true;
                    }
                    catch { }
                }
                if (!idle) continue;
                var reason = DiagnoseIdle(b, catalog);
                list.Add(new IdleBuildingReason(
                    b.ModelName,
                    b.DisplayName ?? b.ModelName,
                    reason));
            }
        }
        catch { }
        return list;
    }

    private static string DiagnoseIdle(
        Eremite.Buildings.Building b, StormGuide.Domain.Catalog catalog)
    {
        // Paused flag (reflection — not all building types expose it).
        try
        {
            var pp = b.GetType().GetProperty("IsPaused");
            if (pp?.GetValue(b) is bool paused && paused) return "paused";
        }
        catch { }
        if (b is Eremite.Buildings.ProductionBuilding pb)
        {
            // Unstaffed: workers array exists but no positive entries.
            try
            {
                var w = pb.Workers;
                if (w != null && w.Length > 0 && !w.Any(x => x > 0)) return "unstaffed";
            }
            catch { }
            Eremite.Buildings.RecipeModel? recipe = null;
            try { recipe = pb.GetCurrentRecipeFor(0); } catch { }
            if (recipe != null && catalog.Recipes.TryGetValue(recipe.Name, out var info))
            {
                // No inputs: every required slot has zero best-of stock.
                if (info.RequiredGoods.Count > 0)
                {
                    var anyEmpty = false;
                    foreach (var slot in info.RequiredGoods)
                    {
                        var best = 0;
                        foreach (var opt in slot.Options)
                            best = Math.Max(best, StockpileOf(opt.Good));
                        if (best == 0) { anyEmpty = true; break; }
                    }
                    if (anyEmpty) return "no inputs";
                }
                // Output full: produced good has > 200 stockpile (heuristic).
                if (!string.IsNullOrEmpty(info.ProducedGood) &&
                    StockpileOf(info.ProducedGood) > 200) return "output full";
            }
        }
        return "";
    }

    /// <summary>
    /// Returns the catalog GoodInfo whose display name appears in the
    /// objective description, longest first. Used by the Orders plan-of-
    /// attack panel to suggest recipes that satisfy the objective.
    /// </summary>
    public static StormGuide.Domain.GoodInfo? MatchedGoodFor(
        OrderObjective ob, StormGuide.Domain.Catalog catalog)
    {
        if (string.IsNullOrEmpty(ob.Description)) return null;
        foreach (var gi in catalog.Goods.Values
                     .OrderByDescending(g => g.DisplayName?.Length ?? 0))
        {
            if (string.IsNullOrEmpty(gi.DisplayName)) continue;
            if (ob.Description.IndexOf(gi.DisplayName,
                    StringComparison.OrdinalIgnoreCase) >= 0) return gi;
        }
        return null;
    }

    /// <summary>Live current resolve for a race, or null if unavailable.</summary>
    public static float? CurrentResolveFor(string raceName)
    {
        var r = ResolveFor(raceName);
        return r is null ? null : r.Value.Item1;
    }

    /// <summary>
    /// Built-building display names whose model carries the given tag. Used
    /// by the cornerstone deep-dive to spell out exactly which buildings a
    /// uniquely-targeted tag would touch. Capped by caller.
    /// </summary>
    public static IReadOnlyList<string> BuildingsCarryingTag(string tagName)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(tagName)) return list;
        try
        {
            var bs = Services?.BuildingsService;
            if (bs?.Buildings == null) return list;
            foreach (var kv in bs.Buildings)
            {
                var b = kv.Value;
                if (b?.BuildingModel?.tags == null) continue;
                foreach (var t in b.BuildingModel.tags)
                {
                    if (t == null || string.IsNullOrEmpty(t.Name)) continue;
                    if (string.Equals(t.Name, tagName, StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(b.DisplayName ?? b.ModelName);
                        break;
                    }
                }
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Best-effort cumulative "cycles completed" counter for a building model,
    /// summed across all instances. Reflects a small set of likely field
    /// names; returns null when no shape matches so callers can render a
    /// dash instead of a misleading zero.
    /// </summary>
    public static long? CyclesCompletedFor(string modelName)
    {
        try
        {
            var bs = Services?.BuildingsService;
            if (bs?.Buildings == null) return null;
            long sum = 0;
            var found = false;
            foreach (var kv in bs.Buildings)
            {
                var b = kv.Value;
                if (b == null || b.ModelName != modelName) continue;
                var t = b.GetType();
                foreach (var name in new[] {
                    "completedCycles", "cyclesCompleted", "completedCount",
                    "finishedCycles", "producedCount" })
                {
                    var f = t.GetField(name);
                    if (f != null && f.GetValue(b) is { } v &&
                        long.TryParse(v.ToString(), out var n))
                    { sum += n; found = true; break; }
                    var p = t.GetProperty(name);
                    if (p != null && p.GetValue(b) is { } pv &&
                        long.TryParse(pv.ToString(), out var pn))
                    { sum += pn; found = true; break; }
                }
            }
            return found ? sum : (long?)null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Cornerstone IDs the player has previously drafted this run (best-effort
    /// via reflection). Returns empty when no history field is exposed.
    /// </summary>
    public static IReadOnlyList<string> CornerstoneHistory()
    {
        var list = new List<string>();
        try
        {
            var state = Services?.StateService?.Cornerstones;
            if (state == null) return list;
            foreach (var name in new[] { "cornerstonesHistory", "history", "pickedCornerstones" })
            {
                var f = state.GetType().GetField(name);
                if (f?.GetValue(state) is System.Collections.IEnumerable raw)
                {
                    foreach (var x in raw)
                        if (x is string s && !string.IsNullOrEmpty(s)) list.Add(s);
                    if (list.Count > 0) return list;
                }
            }
        }
        catch { }
        return list;
    }

    public sealed record WorkerRebalanceHint(
        string FromBuilding,    // display name; over-staffed / running idle
        string ToBuilding,       // display name; could absorb workers
        string Reason);

    /// <summary>
    /// Heuristic worker-rebalance suggestions: pair an idle production
    /// building (no point staffing it) with a building that produces a
    /// currently-draining good and has empty worker slots. We can't move
    /// workers from here — the game doesn't expose that API — so we surface
    /// the recommendation as a text hint.
    /// </summary>
    public static IReadOnlyList<WorkerRebalanceHint> WorkerRebalanceHints(
        StormGuide.Domain.Catalog catalog)
    {
        var hints = new List<WorkerRebalanceHint>();
        try
        {
            var bs = Services?.BuildingsService;
            if (bs == null) return hints;
            // Pre-compute draining goods (Net < 0) once.
            var draining = new HashSet<string>(StringComparer.Ordinal);
            foreach (var gi in catalog.Goods.Values)
            {
                if (FlowFor(gi.Name, catalog).Net < -1e-6) draining.Add(gi.Name);
            }
            if (draining.Count == 0) return hints;

            var idleSources = new List<Eremite.Buildings.ProductionBuilding>();
            var hungryTargets = new List<(Eremite.Buildings.ProductionBuilding pb, string Good)>();
            foreach (var kv in bs.Buildings)
            {
                if (kv.Value is not Eremite.Buildings.ProductionBuilding pb) continue;
                if (pb.Workers == null) continue;
                if (pb.IsIdle) { idleSources.Add(pb); continue; }
                // Free worker slot + produces a draining good = a rebalance target.
                var hasFreeSlot = false;
                for (var i = 0; i < pb.Workers.Length; i++)
                    if (pb.Workers[i] <= 0) { hasFreeSlot = true; break; }
                if (!hasFreeSlot) continue;
                Eremite.Buildings.RecipeModel? recipe = null;
                try { recipe = pb.GetCurrentRecipeFor(0); } catch { }
                if (recipe == null) continue;
                if (!catalog.Recipes.TryGetValue(recipe.Name, out var info)) continue;
                if (string.IsNullOrEmpty(info.ProducedGood)) continue;
                if (draining.Contains(info.ProducedGood))
                    hungryTargets.Add((pb, info.ProducedGood));
            }
            // Pair them up greedily.
            for (var i = 0; i < idleSources.Count && i < hungryTargets.Count; i++)
            {
                var src = idleSources[i];
                var (tgt, good) = hungryTargets[i];
                var goodName = catalog.Goods.TryGetValue(good, out var gi) ? gi.DisplayName : good;
                hints.Add(new WorkerRebalanceHint(
                    FromBuilding: src.DisplayName ?? src.ModelName,
                    ToBuilding:   tgt.DisplayName ?? tgt.ModelName,
                    Reason:       $"{goodName} is draining; move workers off the idle workshop."));
            }
        }
        catch { }
        return hints;
    }

    /// <summary>
    /// SHA-1 of the embedded catalog resources, computed on demand. Used by
    /// the catalog-diff banner so the user notices when a new build ships an
    /// updated trim. Returns an empty string on failure.
    /// </summary>
    public static string CatalogContentHash()
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA1.Create();
            var asm = typeof(LiveGameState).Assembly;
            foreach (var name in asm.GetManifestResourceNames()
                         .Where(n => n.Contains(".Resources.catalog.") &&
                                     n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.Ordinal))
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) continue;
                var buf = new byte[8192];
                int read;
                while ((read = stream.Read(buf, 0, buf.Length)) > 0)
                    sha.TransformBlock(buf, 0, read, buf, 0);
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var hash = sha.Hash;
            return hash == null ? "" : BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        catch { return ""; }
    }

    /// <summary>
    /// Toggle <c>OrderState.tracked</c> for an order id, defensively. Returns
    /// true on success. Used by the in-panel order pin so the player doesn't
    /// have to open the orders popup just to focus an order.
    /// </summary>
    public static bool SetOrderTracked(int orderId, bool tracked)
    {
        try
        {
            var os = Services?.OrdersService;
            if (os?.Orders == null) return false;
            foreach (var o in os.Orders)
            {
                if (o == null || o.id != orderId) continue;
                // The field is public on OrderState; assignment is the
                // simplest API and matches what the game's UI does.
                o.tracked = tracked;
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Best-effort ETA in minutes for a single objective using the live
    /// settlement net flow for whichever catalog good the description names.
    /// Returns null when the objective is already complete, the regex doesn't
    /// match, no good is referenced, or the settlement isn't producing it.
    /// </summary>
    public static double? ObjectiveEtaMinutes(
        OrderObjective ob, StormGuide.Domain.Catalog catalog)
    {
        if (ob.Completed) return null;
        if (string.IsNullOrEmpty(ob.Description) || !IsReady) return null;
        var match = System.Text.RegularExpressions.Regex.Match(
            ob.Description, @"(\d+)\s*/\s*(\d+)");
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, out var have)) return null;
        if (!int.TryParse(match.Groups[2].Value, out var need)) return null;
        if (need <= have) return null;

        StormGuide.Domain.GoodInfo? matched = null;
        foreach (var gi in catalog.Goods.Values
                     .OrderByDescending(g => g.DisplayName.Length))
        {
            if (string.IsNullOrEmpty(gi.DisplayName)) continue;
            if (ob.Description.IndexOf(gi.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
            { matched = gi; break; }
        }
        if (matched is null) return null;
        var net = FlowFor(matched.Name, catalog).Net;
        if (net <= 1e-6) return null;
        var minutes = (need - have) / net;
        if (minutes <= 0 || minutes > 9999) return null;
        return minutes;
    }

    /// <summary>
    /// Tag-name → owned-cornerstone count, joining each currently-active
    /// cornerstone's usabilityTags. Used by the cornerstone synergy ranker to
    /// surface stacking with the player's existing build.
    /// </summary>
    public static IReadOnlyDictionary<string, int> OwnedCornerstoneUsabilityTags()
    {
        var tags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var ms = Services?.GameModelService;
            if (ms == null) return tags;
            foreach (var oc in OwnedCornerstones())
            {
                Eremite.Model.EffectModel? eff = null;
                try { eff = ms.GetEffect(oc.Id); } catch { }
                var ut = eff?.usabilityTags;
                if (ut == null) continue;
                foreach (var t in ut)
                {
                    if (t == null || string.IsNullOrEmpty(t.Name)) continue;
                    tags.TryGetValue(t.Name, out var n);
                    tags[t.Name] = n + 1;
                }
            }
        }
        catch { }
        return tags;
    }
}
