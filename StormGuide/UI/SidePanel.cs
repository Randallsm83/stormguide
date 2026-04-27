using System;
using StormGuide.Configuration;
using StormGuide.Data;
using StormGuide.Domain;
using StormGuide.Providers;
using UnityEngine;

namespace StormGuide.UI;

/// <summary>
/// Draggable IMGUI overlay rendered every frame while visible.
/// Acts as the Phase 2/3 skeleton: hotkey toggle, tab strip, ranked Building data.
/// Will be replaced by a Canvas-cloned-from-prefab implementation later.
/// </summary>
internal sealed class SidePanel : MonoBehaviour
{
    private const int WindowId = 0x57_47_55_44; // "SGUD"

    public PluginConfig Config { get; set; } = null!;
    public Catalog      Catalog { get; set; } = Catalog.Empty;

    private bool   _visible;
    private Rect   _rect;
    private Tab    _activeTab = Tab.Home;
    private GUIStyle? _windowStyle;
    private GUIStyle? _tabStyle;
    private GUIStyle? _tabActiveStyle;
    private GUIStyle? _bodyStyle;
    private GUIStyle? _h1Style;
    private GUIStyle? _badgeStyle;
    private GUIStyle? _mutedStyle;

    // ---- Building tab state ------------------------------------------------
    private string  _buildingSearch = "";
    private string? _selectedBuilding;
    private Vector2 _buildingListScroll;
    private Vector2 _buildingDetailScroll;
    private readonly HashSet<string> _expandedRecipes = new();

    // ---- Good tab state ----------------------------------------------------
    private string  _goodSearch = "";
    private string? _selectedGood;
    private Vector2 _goodListScroll;
    private Vector2 _goodDetailScroll;
    private readonly HashSet<string> _expandedProducers = new();
    private bool     _flowExpanded;

    // ---- Villagers tab state -----------------------------------------------
    private string?  _selectedRace;
    private Vector2  _raceListScroll;
    private Vector2  _raceDetailScroll;

    // ---- Live-state subscription -------------------------------------------
    private IDisposable? _selectionSub;
    private IDisposable? _draftSub;
    private bool         _selectionSubscribed;
    private bool         _draftSubscribed;

    // ---- Resize state ------------------------------------------------------
    private bool    _resizing;
    private Vector2 _resizeStartMouse;
    private Vector2 _resizeStartSize;
    private static readonly Vector2 MinPanelSize    = new(280, 240);
    private static readonly Vector2 DefaultPosition = new(40, 80);
    private static readonly Vector2 DefaultSize     = new(420, 480);

    // ---- Live-state caches (TTL-bounded; settlements change slowly) -------
    private TtlCache<LiveGameState.SettlementAlerts?>? _alertsCache;
    private TtlCache<StormGuide.Domain.VillageSummary?>? _summaryCache;
    private TtlCache<IReadOnlyList<LiveGameState.OwnedCornerstone>>? _ownedCache;

    public enum Tab { Home, Building, Good, Villagers, Orders, Glades, Draft, Settings, Diagnostics, Embark }

    // Tracks last applied compact state so style rebuilds happen exactly once
    // when the toggle flips at runtime.
    private bool? _lastCompactApplied;

    // Selected input good (Building tab recipe filter chip).
    private string? _recipeInputFilter;
    private Vector2 _embarkScroll;

    // Pinned (buildingModel, recipeModel) pairs, restored from config.
    private readonly List<(string Building, string Recipe)> _pinned = new();

    // Lazily-built chip style (smaller padding + lighter bg) used for tag pills.
    private GUIStyle? _chipStyle;
    // Compact list-button style (only used when CompactLists is enabled).
    private GUIStyle? _listButtonStyle;
    // Tracks last applied compact-lists state so EnsureStyles can rebuild on flip.
    private bool? _lastCompactListsApplied;

    // Rolling per-good net-flow samples (sparklines on the at-risk row).
    // Keyed by good catalog name; each value is a small ring of recent values.
    private readonly Dictionary<string, Queue<double>> _flowSamples = new(StringComparer.Ordinal);
    private float _lastSampleTime;

    // Recipe sort modes for the Building tab.
    private enum RecipeSort { Throughput, Profitability, Availability }
    private RecipeSort _recipeSort = RecipeSort.Throughput;

    // Pinned-recipe forecast target: minutes to fill the produced good's
    // stockpile to this many units at current net rate.
    private const int PinForecastTarget = 50;

    // Per-good price history for the current trader visit. Cleared on visit
    // change. Used by the Good tab's price chart.
    private readonly Dictionary<string, List<float>> _priceSamples = new(StringComparer.Ordinal);
    private string? _priceVisitKey;
    private float _lastPriceSampleTime;

    // Order completion lookback: id → first-seen game-time so we can compute
    // a duration when the order disappears from the active list.
    private readonly Dictionary<int, float> _orderFirstSeen = new();
    private readonly List<float> _completedDurations = new();
    private HashSet<int>? _lastSeenOrderIds;
    private float _lastOrderTime;

    // Last-known main-trader village state (for auto-jump on arrival).
    private bool? _lastTraderInVillage;

    // Catalog-diff banner state. "" = no banner, otherwise a short message.
    private string _catalogDiffNotice = "";

    // Session start (game-time) for the Diagnostics stats panel.
    private float? _sessionStartTime;
    private int _cornerstonesDraftedThisSession;

    // Set of cornerstone ids already merged into the persistent pick history
    // this session, so we don't double-record them on every Update tick.
    private readonly HashSet<string> _cornerstonesAlreadyRecorded = new(StringComparer.Ordinal);

    // Per-section frame-time samples for the live-debug overlay (Shift-held).
    private readonly Dictionary<string, double> _sectionMs = new(StringComparer.Ordinal);
    private System.Diagnostics.Stopwatch? _sectionWatch;

    // Last good→displayName "missing translation" warning timestamp; suppresses
    // log spam when the locale resolution is failing in bulk.
    private bool _localeWarned;

    // Per-race rolling resolve samples (60s ring; ~1 sample/s) so the Villagers
    // tab can render a small trajectory sparkline next to the resolve bar.
    private readonly Dictionary<string, Queue<float>> _resolveSamples =
        new(StringComparer.Ordinal);
    private float _lastResolveSampleTime;

    // Cornerstone option that the user clicked "compare" on; null = nobody.
    private string? _compareOption;

    // Free-text filter for the Settings tab (matches against the *property*
    // names so the table-of-contents shrinks as the user types).
    private string _settingsFilter = "";

    // Trader buy-list builder: which sells the user has ticked, plus a cached
    // visit key so the ticks reset on trader change.
    private readonly HashSet<string> _buyListSelections =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _buyListVisitKey;

    // Per-building cumulative cycles ring; (gameTime, count) pairs sampled at
    // ~5s intervals so we can compute "cycles in last 5 min" deltas.
    private readonly Dictionary<string, Queue<(float Time, long Count)>> _cyclesSamples =
        new(StringComparer.Ordinal);
    private float _lastCyclesSampleTime;

    // Trader travel-progress ring used to estimate seconds until arrival.
    private readonly Queue<(float Time, float Progress)> _traderTravelSamples = new();
    private string? _traderTravelKey;

    // Per-section perf ring (last 120 frames) for Diagnostics p50/p95.
    private readonly Dictionary<string, Queue<double>> _perfHistory =
        new(StringComparer.Ordinal);

    // Pin preset name input (Settings tab).
    private string _newPresetName = "";

    // UI-only marker sets for recipes flagged as stopped or haul-priority.
    // Persisted to PluginConfig.MarkedStoppedRecipes / MarkedPriorityRecipes
    // as comma-separated lists of recipe model names.
    private readonly HashSet<string> _markedStopped =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _markedPriority =
        new(StringComparer.Ordinal);

    // Order tier filter chips. Empty set = no filter (show everything).
    private readonly HashSet<string> _orderTierFilter =
        new(StringComparer.OrdinalIgnoreCase);

    // ---- Home tab state ----------------------------------------------------
    private Vector2 _homeScroll;

    // ---- Orders/Glades tab state ------------------------------------------
    private Vector2 _ordersScroll;
    private Vector2 _gladesScroll;
    private Vector2 _settingsScroll;

    // ---- Settings interaction ---------------------------------------------
    private bool   _rebindingHotkey;
    private string _docView = "";       // "", "README.md", "AGENTS.md"
    private Vector2 _docScroll;
    private string _catalogReloadStatus = "";

    // ---- Diagnostics tab state -------------------------------------------
    private Vector2 _diagScroll;

    // ---- Cached match lists (recomputed only when search text changes) ---
    private string?              _cachedBuildingQuery;
    private List<BuildingInfo>?  _cachedBuildingMatches;
    private string?              _cachedGoodQuery;
    private List<GoodInfo>?      _cachedGoodMatches;

    // ---- Producer filter chips (Good tab) -------------------------------
    private bool _filterFuelOnly;
    private bool _filterEatableOnly;
    private bool _filterDrainingOnly;

    // ---- Trader desires hot-cache (recomputed at most every 0.5s) -------
    private TtlCache<IReadOnlyList<LiveGameState.TraderDesire>>? _currentDesiresCache;
    private TtlCache<IReadOnlyList<LiveGameState.TraderDesire>>? _nextDesiresCache;

    // Compiled regex for objective progress ("5 / 10" inside rich text).
    private static readonly System.Text.RegularExpressions.Regex ProgressRegex =
        new(@"(\d+)\s*/\s*(\d+)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Time-pressure styles for failable orders.
    private GUIStyle? _warnStyle;
    private GUIStyle? _critStyle;
    private GUIStyle? _okStyle;

    // Tab order used by Ctrl+1..9 shortcuts; matches Tab enum order.
    private static readonly Tab[] TabOrder = new[]
    {
        Tab.Home, Tab.Building, Tab.Good, Tab.Villagers,
        Tab.Orders, Tab.Glades, Tab.Draft, Tab.Settings, Tab.Diagnostics, Tab.Embark,
    };

    private void Start()
    {
        _visible = Config.VisibleByDefault.Value;
        _rect = new Rect(
            Config.PanelPosition.Value.x,
            Config.PanelPosition.Value.y,
            Config.PanelSize.Value.x,
            Config.PanelSize.Value.y);

        // Restore last-known UI state.
        if (Enum.TryParse<Tab>(Config.ActiveTab.Value, ignoreCase: true, out var savedTab))
            _activeTab = savedTab;
        if (!string.IsNullOrEmpty(Config.LastSelectedBuilding.Value))
            _selectedBuilding = Config.LastSelectedBuilding.Value;
        if (!string.IsNullOrEmpty(Config.LastSelectedGood.Value))
            _selectedGood = Config.LastSelectedGood.Value;
        if (!string.IsNullOrEmpty(Config.LastSelectedRace.Value))
            _selectedRace = Config.LastSelectedRace.Value;
        if (!string.IsNullOrEmpty(Config.LastBuildingSearch.Value))
            _buildingSearch = Config.LastBuildingSearch.Value;
        if (!string.IsNullOrEmpty(Config.LastGoodSearch.Value))
            _goodSearch = Config.LastGoodSearch.Value;

        // Restore pins. Format: "buildingModel|recipeModel;buildingModel|recipeModel".
        if (!string.IsNullOrEmpty(Config.PinnedRecipes.Value))
        {
            foreach (var entry in Config.PinnedRecipes.Value.Split(';'))
            {
                var split = entry.Split('|');
                if (split.Length == 2 &&
                    !string.IsNullOrEmpty(split[0]) && !string.IsNullOrEmpty(split[1]))
                    _pinned.Add((split[0], split[1]));
            }
        }

        // Persisted "why × all" expansions become unbounded sentinels: the
        // rendering layer only checks Contains() so an empty marker keeps the
        // expansion visible until the user toggles back.
        if (Config.WhyAllRecipes.Value)   _expandedRecipes.Add("__all");
        if (Config.WhyAllProducers.Value) _expandedProducers.Add("__all");

        // Restore recipe markers from config (UI-only flags).
        if (!string.IsNullOrEmpty(Config.MarkedStoppedRecipes.Value))
            foreach (var s in Config.MarkedStoppedRecipes.Value.Split(','))
                if (!string.IsNullOrEmpty(s)) _markedStopped.Add(s);
        if (!string.IsNullOrEmpty(Config.MarkedPriorityRecipes.Value))
            foreach (var s in Config.MarkedPriorityRecipes.Value.Split(','))
                if (!string.IsNullOrEmpty(s)) _markedPriority.Add(s);

        // Cache the slow aggregates: 0.5s is well below player perception
        // for these kinds of metrics, but it cuts redundant scans by ~30x at
        // the typical IMGUI redraw rate.
        _alertsCache  = new TtlCache<LiveGameState.SettlementAlerts?>(
            () => LiveGameState.AlertsFor(Catalog,
                Math.Max(1f, Config.GoodsAtRiskThresholdMinutes.Value)), ttlSeconds: 0.5f);
        _summaryCache = new TtlCache<StormGuide.Domain.VillageSummary?>(
            () => LiveGameState.VillageSummary(name =>
                Catalog.Races.TryGetValue(name, out var r) ? r.DisplayName : name),
            ttlSeconds: 0.5f);
        _ownedCache   = new TtlCache<IReadOnlyList<LiveGameState.OwnedCornerstone>>(
            LiveGameState.OwnedCornerstones, ttlSeconds: 1.0f);
        _currentDesiresCache = new TtlCache<IReadOnlyList<LiveGameState.TraderDesire>>(
            () => LiveGameState.RankTraderDesires(
                LiveGameState.CurrentTrader(), Catalog, isCurrent: true),
            ttlSeconds: 0.5f);
        _nextDesiresCache = new TtlCache<IReadOnlyList<LiveGameState.TraderDesire>>(
            () => LiveGameState.RankTraderDesires(
                LiveGameState.NextTrader(), Catalog, isCurrent: false),
            ttlSeconds: 0.5f);

        // Catalog-diff banner: compare the embedded resource hash against the
        // last value seen by this install and post a one-shot Diagnostics
        // notice if it changed (or first run).
        try
        {
            var hash = LiveGameState.CatalogContentHash();
            if (!string.IsNullOrEmpty(hash) && Config.LastCatalogHash.Value != hash)
            {
                _catalogDiffNotice = string.IsNullOrEmpty(Config.LastCatalogHash.Value)
                    ? $"first-run catalog hash {hash.Substring(0, 8)}."
                    : $"catalog updated (was {Config.LastCatalogHash.Value.Substring(0, 8)}, now {hash.Substring(0, 8)}).";
                Config.LastCatalogHash.Value = hash;
                StormGuidePlugin.Log.LogInfo("StormGuide: " + _catalogDiffNotice);
            }
        }
        catch { }
    }

    private void Update()
    {
        if (!_rebindingHotkey && Config.ToggleHotkey.Value.IsDown())
        {
            _visible = !_visible;
            StormGuidePlugin.Log.LogInfo($"StormGuide panel {(_visible ? "shown" : "hidden")}.");
        }

        // Ctrl+1..9 jumps to the corresponding visible tab.
        if (_visible && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            for (var i = 0; i < TabOrder.Length; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && IsTabVisible(TabOrder[i]))
                {
                    _activeTab = TabOrder[i];
                    break;
                }
            }
        }

        // F5 reloads the static catalog (mirrors the Settings button so power
        // users don't have to navigate there after a JSONLoader regen).
        if (_visible && Input.GetKeyDown(KeyCode.F5))
        {
            try { StormGuidePlugin.ReloadCatalog(); _catalogReloadStatus = "reloaded."; }
            catch (Exception ex) { _catalogReloadStatus = "error: " + ex.Message; }
        }

        // Subscribe to in-game selection once GameController is ready.
        if (!_selectionSubscribed && LiveGameState.IsReady)
        {
            _selectionSub = LiveGameState.SubscribeToPicked(OnObjectPicked);
            _selectionSubscribed = true;
            StormGuidePlugin.Log.LogInfo("StormGuide subscribed to in-game selection.");
        }
        // Subscribe to cornerstone popup events so the panel can auto-switch.
        if (!_draftSubscribed && LiveGameState.IsReady)
        {
            _draftSub = LiveGameState.SubscribeToCornerstonePopup(OnCornerstonePopup);
            _draftSubscribed = true;
        }

        // Trader auto-jump: detect transition from "not in village" to "in
        // village" and switch to the Good tab + select the top desire.
        if (LiveGameState.IsReady && Config.ShowGoodTab.Value)
        {
            try
            {
                var cur = LiveGameState.CurrentTrader();
                if (cur is not null)
                {
                    if (_lastTraderInVillage == false && cur.IsInVillage)
                    {
                        _activeTab = Tab.Good;
                        var desires = _currentDesiresCache?.Get();
                        if (desires is { Count: > 0 }) _selectedGood = desires[0].Good;
                        if (!_visible) _visible = true;
                        StormGuidePlugin.Log.LogInfo("StormGuide: auto-jump on trader arrival.");
                    }
                    _lastTraderInVillage = cur.IsInVillage;
                }
            }
            catch { }
        }

        // Order completion lookback: detect orders that disappeared from
        // the active list since last tick and record their durations.
        TickOrderLookback();

        // Capture session start once and merge new owned cornerstones into
        // the rolling pick-history config so the synergy ranker can favour
        // them on future drafts.
        if (LiveGameState.IsReady && _sessionStartTime is null)
            _sessionStartTime = LiveGameState.GameTimeNow();
        if (LiveGameState.IsReady) TickCornerstoneHistory();
        if (LiveGameState.IsReady) TickResolveSamples();
        if (LiveGameState.IsReady) TickCyclesSamples();
        if (LiveGameState.IsReady) TickTraderTravel();
        if (LiveGameState.IsReady) TickPinnedFlows();
    }

    /// <summary>
    /// Ensures the per-good net-flow ring (<see cref="_flowSamples"/>) has
    /// fresh samples for every pinned recipe's produced good, even when those
    /// goods aren't currently flagged as at-risk. Sampled at 1Hz, capped at
    /// 30 samples per good. Used by <see cref="DrawHomePinned"/> to render a
    /// small inline sparkline per pin row.
    /// </summary>
    private void TickPinnedFlows()
    {
        if (_pinned.Count == 0) return;
        var now = LiveGameState.GameTimeNow() ?? Time.unscaledTime;
        if (now - _lastSampleTime < 1f) return;
        var goods = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, recipe) in _pinned)
        {
            if (Catalog.Recipes.TryGetValue(recipe, out var r) &&
                !string.IsNullOrEmpty(r.ProducedGood))
                goods.Add(r.ProducedGood);
        }
        foreach (var g in goods)
        {
            if (!_flowSamples.TryGetValue(g, out var q))
                _flowSamples[g] = q = new Queue<double>(32);
            q.Enqueue(LiveGameState.FlowFor(g, Catalog).Net);
            while (q.Count > 30) q.Dequeue();
        }
        _lastSampleTime = now;
    }

    /// <summary>
    /// Sample cumulative cycle counters for every pinned recipe's building
    /// once every ~5s, so the Building tab can compute a "cycles in last 5
    /// min" delta. Counter source is best-effort reflective; missing data is
    /// silent.
    /// </summary>
    private void TickCyclesSamples()
    {
        var now = LiveGameState.GameTimeNow() ?? Time.unscaledTime;
        if (now - _lastCyclesSampleTime < 5f) return;
        _lastCyclesSampleTime = now;
        try
        {
            // Sample for the currently-selected building plus any pinned ones,
            // since these are the only models whose counter we ever surface.
            var models = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(_selectedBuilding)) models.Add(_selectedBuilding!);
            foreach (var (b, _) in _pinned) models.Add(b);
            foreach (var m in models)
            {
                var c = LiveGameState.CyclesCompletedFor(m);
                if (c is null) continue;
                if (!_cyclesSamples.TryGetValue(m, out var q))
                    _cyclesSamples[m] = q = new Queue<(float, long)>(64);
                q.Enqueue((now, c.Value));
                while (q.Count > 60) q.Dequeue();
            }
        }
        catch { }
    }

    /// <summary>
    /// Sample trader travel progress every second so the Home Trade block can
    /// estimate seconds-until-arrival from the recent rate. Resets on trader
    /// change so old samples don't bias the estimate.
    /// </summary>
    private void TickTraderTravel()
    {
        try
        {
            var cur = LiveGameState.CurrentTrader();
            if (cur is null || cur.IsInVillage) return;
            var prog = LiveGameState.CurrentTraderTravelProgress();
            if (prog is null) return;
            var key = cur.DisplayName ?? "";
            if (_traderTravelKey != key)
            {
                _traderTravelKey = key;
                _traderTravelSamples.Clear();
            }
            var now = LiveGameState.GameTimeNow() ?? Time.unscaledTime;
            if (_traderTravelSamples.Count > 0 &&
                now - _traderTravelSamples.Last().Time < 1f) return;
            _traderTravelSamples.Enqueue((now, prog.Value));
            while (_traderTravelSamples.Count > 30) _traderTravelSamples.Dequeue();
        }
        catch { }
    }

    /// <summary>
    /// Once a second, push the live current-resolve value for each race onto
    /// a 60-sample ring. Used by <see cref="DrawResolveSparkline"/>.
    /// </summary>
    private void TickResolveSamples()
    {
        var now = LiveGameState.GameTimeNow() ?? Time.unscaledTime;
        if (now - _lastResolveSampleTime < 1f) return;
        _lastResolveSampleTime = now;
        try
        {
            foreach (var raceName in Catalog.Races.Keys)
            {
                var v = LiveGameState.CurrentResolveFor(raceName);
                if (v is null) continue;
                if (!_resolveSamples.TryGetValue(raceName, out var q))
                    _resolveSamples[raceName] = q = new Queue<float>(64);
                q.Enqueue(v.Value);
                while (q.Count > 60) q.Dequeue();
            }
        }
        catch { }
    }

    /// <summary>
    /// Append any newly-owned cornerstones to the persistent pick-history
    /// config (rolling 50). Used as a tiebreaker by the synergy ranker.
    /// </summary>
    private void TickCornerstoneHistory()
    {
        try
        {
            var owned = LiveGameState.OwnedCornerstones();
            if (owned.Count == 0) return;
            var history = (Config.CornerstonePickHistory.Value ?? "")
                .Split(';').Where(s => !string.IsNullOrEmpty(s)).ToList();
            var changed = false;
            foreach (var oc in owned)
            {
                if (string.IsNullOrEmpty(oc.Id)) continue;
                if (_cornerstonesAlreadyRecorded.Contains(oc.Id)) continue;
                _cornerstonesAlreadyRecorded.Add(oc.Id);
                if (!history.Contains(oc.Id))
                {
                    history.Add(oc.Id);
                    _cornerstonesDraftedThisSession++;
                    changed = true;
                }
            }
            if (changed)
            {
                while (history.Count > 50) history.RemoveAt(0);
                Config.CornerstonePickHistory.Value = string.Join(";", history);
            }
        }
        catch { }
    }

    /// <summary>
    /// Diffs current active orders against the last seen set; any id that
    /// disappears is treated as "completed or failed" and we record the
    /// duration since first-seen so the Home tab can show an average.
    /// </summary>
    private void TickOrderLookback()
    {
        if (!LiveGameState.IsReady) return;
        var now = LiveGameState.GameTimeNow() ?? Time.unscaledTime;
        // Sample at most every 2 seconds; this is purely informational.
        if (now - _lastOrderTime < 2f) return;
        _lastOrderTime = now;
        try
        {
            var orders = LiveGameState.ActiveOrders();
            var current = new HashSet<int>(orders.Select(o => o.Id));
            foreach (var o in orders)
            {
                if (!_orderFirstSeen.ContainsKey(o.Id)) _orderFirstSeen[o.Id] = now;
            }
            if (_lastSeenOrderIds != null)
            {
                foreach (var prev in _lastSeenOrderIds)
                {
                    if (current.Contains(prev)) continue;
                    if (!_orderFirstSeen.TryGetValue(prev, out var start)) continue;
                    var duration = now - start;
                    if (duration > 0 && duration < 7200) _completedDurations.Add(duration);
                    _orderFirstSeen.Remove(prev);
                    while (_completedDurations.Count > 30) _completedDurations.RemoveAt(0);
                }
            }
            _lastSeenOrderIds = current;
        }
        catch { }
    }

    private void OnCornerstonePopup()
    {
        _activeTab = Tab.Draft;
        if (!_visible) _visible = true;
        StormGuidePlugin.Log.LogInfo("StormGuide: cornerstone draft auto-switch.");
    }

    private void OnObjectPicked(Eremite.IMapObject? obj)
    {
        var modelName = LiveGameState.AsBuildingModelName(obj);
        if (modelName is null) return;
        _selectedBuilding = modelName;
        _activeTab = Tab.Building;
        if (!_visible) _visible = true;
    }

    private void OnDestroy()
    {
        _selectionSub?.Dispose();
        _draftSub?.Dispose();
    }

    private void OnGUI()
    {
        if (!_visible) return;
        EnsureStyles();

        _rect = GUILayout.Window(WindowId, _rect, DrawWindow, "StormGuide", _windowStyle);

        // Persist movement/resize back to config (debounced by BepInEx).
        var topLeft = new Vector2(_rect.x, _rect.y);
        if (Config.PanelPosition.Value != topLeft) Config.PanelPosition.Value = topLeft;
        var size = new Vector2(_rect.width, _rect.height);
        if (Config.PanelSize.Value != size) Config.PanelSize.Value = size;

        // Persist UI state (active tab + selections).
        var tabName = _activeTab.ToString();
        if (Config.ActiveTab.Value != tabName) Config.ActiveTab.Value = tabName;
        if (Config.LastSelectedBuilding.Value != (_selectedBuilding ?? ""))
            Config.LastSelectedBuilding.Value = _selectedBuilding ?? "";
        if (Config.LastSelectedGood.Value != (_selectedGood ?? ""))
            Config.LastSelectedGood.Value = _selectedGood ?? "";
        if (Config.LastSelectedRace.Value != (_selectedRace ?? ""))
            Config.LastSelectedRace.Value = _selectedRace ?? "";
        if (Config.LastBuildingSearch.Value != (_buildingSearch ?? ""))
            Config.LastBuildingSearch.Value = _buildingSearch ?? "";
        if (Config.LastGoodSearch.Value != (_goodSearch ?? ""))
            Config.LastGoodSearch.Value = _goodSearch ?? "";
    }

    private void DrawWindow(int _)
    {
        // Reset section timings each frame; populated by Section() helper.
        _sectionMs.Clear();
        try { DrawWindowInner(); }
        catch (Exception ex)
        {
            // Last-resort safety net: an unhandled exception in OnGUI would
            // tear down the window, so we trap it, log it, and write a small
            // crash dump next to the BepInEx config for later review.
            try { StormGuidePlugin.Log.LogError("StormGuide GUI exception: " + ex); } catch { }
            WriteCrashDump(ex);
        }
        // Live-debug overlay: hold either Shift while the panel is up to see
        // per-section ms breakdown rendered in the corner.
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            DrawDebugOverlay();
    }

    private void DrawWindowInner()
    {
        DrawTitleControls();
        DrawStormHeader();
        DrawTabs();
        DrawAlertsStrip();
        GUILayout.Space(6);

        switch (_activeTab)
        {
            case Tab.Home:        Section("Home",        DrawHomeTab);        break;
            case Tab.Building:    Section("Building",    DrawBuildingTab);    break;
            case Tab.Good:        Section("Good",        DrawGoodTab);        break;
            case Tab.Villagers:   Section("Villagers",   DrawVillagersTab);   break;
            case Tab.Orders:      Section("Orders",      DrawOrdersTab);      break;
            case Tab.Glades:      Section("Glades",      DrawGladesTab);      break;
            case Tab.Draft:       Section("Draft",       DrawDraftTab);       break;
            case Tab.Settings:    Section("Settings",    DrawSettingsTab);    break;
            case Tab.Diagnostics: Section("Diagnostics", DrawDiagnosticsTab); break;
            case Tab.Embark:      Section("Embark",      DrawEmbarkTab);      break;
        }

        GUILayout.FlexibleSpace();
        DrawFooter();

        DrawResizeHandle();

        // Make the title bar (top 22 px) draggable, but exclude the right edge
        // where the reset button lives so clicks there don't initiate drag.
        GUI.DragWindow(new Rect(0, 0, _rect.width - 28, 22));
    }

    /// <summary>
    /// Single-line storm/season header rendered above the tab strip. Uses the
    /// best-effort <see cref="LiveGameState.Weather"/> probe and degrades to
    /// nothing when the weather service can't be reached.
    /// </summary>
    private void DrawStormHeader()
    {
        if (!LiveGameState.IsReady) return;
        var w = LiveGameState.Weather();
        if (w is null) return;
        var label = string.IsNullOrEmpty(w.PhaseName) ? "weather" : w.PhaseName.ToLowerInvariant();
        if (w.SecondsLeft is float sl && sl > 0f)
        {
            var mins = Mathf.Max(0, Mathf.FloorToInt(sl / 60f));
            var secs = Mathf.Max(0, Mathf.FloorToInt(sl % 60f));
            var style = label.Contains("storm") ? (_critStyle ?? _bodyStyle)
                      : label.Contains("drizzle") ? (_warnStyle ?? _bodyStyle)
                      : _bodyStyle;
            GUILayout.Label($"⛅ {label}: {mins}:{secs:00} until next phase", style);
        }
        else
        {
            GUILayout.Label($"⛅ {label}", _mutedStyle);
        }
    }

    private void DrawAlertsStrip()
    {
        if (!LiveGameState.IsReady) return;
        var a = _alertsCache?.Get();
        if (a is null) return;
        if (a.IdleWorkshops == 0 && a.RacesBelowTarget == 0 && a.GoodsAtRisk.Count == 0) return;

        // Render alerts as a horizontal row of clickable mini-buttons. Each one
        // routes to the tab/selection most relevant for acting on the alert.
        GUILayout.BeginHorizontal();
        GUILayout.Label("⚠", _mutedStyle, GUILayout.Width(14));

        if (a.IdleWorkshops > 0)
        {
            var label = $"{a.IdleWorkshops} idle";
            if (GUILayout.Button(new GUIContent(label, "Jump to Building tab"), _tabStyle))
            {
                _activeTab = Tab.Building;
            }
        }
        if (a.RacesBelowTarget > 0)
        {
            var label = $"{a.RacesBelowTarget} below resolve";
            if (GUILayout.Button(new GUIContent(label, "Jump to Villagers tab"), _tabStyle))
            {
                _activeTab = Tab.Villagers;
            }
        }
        foreach (var g in a.GoodsAtRisk.Take(3))
        {
            var displayName = Catalog.Goods.TryGetValue(g.Good, out var gi) ? gi.DisplayName : g.Good;
            var label = $"{displayName} {g.RunwayMinutes:0.#}m";
            if (GUILayout.Button(new GUIContent(label, "Jump to Good tab"), _tabStyle))
            {
                _activeTab = Tab.Good;
                _selectedGood = g.Good;
                _flowExpanded = true; // surface the flow breakdown immediately
            }
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private void DrawTitleControls()
    {
        // Tiny reset button anchored to the top-right of the window.
        var btnRect = new Rect(_rect.width - 26, 4, 22, 16);
        if (GUI.Button(btnRect, new GUIContent("↺", "Reset panel position and size"), _tabStyle))
        {
            _rect.position = DefaultPosition;
            _rect.size     = DefaultSize;
            Config.PanelPosition.Value = DefaultPosition;
            Config.PanelSize.Value     = DefaultSize;
            StormGuidePlugin.Log.LogInfo("StormGuide panel reset to defaults.");
        }
    }

    private void DrawResizeHandle()
    {
        const int gripSize = 14;
        var gripRect = new Rect(
            _rect.width  - gripSize - 2,
            _rect.height - gripSize - 2,
            gripSize, gripSize);
        // Visual marker (a small ◢-like glyph).
        GUI.Label(gripRect, "◢", _mutedStyle);

        var ev = Event.current;
        switch (ev.type)
        {
            case EventType.MouseDown:
                if (gripRect.Contains(ev.mousePosition))
                {
                    _resizing         = true;
                    _resizeStartMouse = GUIUtility.GUIToScreenPoint(ev.mousePosition);
                    _resizeStartSize  = _rect.size;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (_resizing)
                {
                    var delta = GUIUtility.GUIToScreenPoint(ev.mousePosition) - _resizeStartMouse;
                    var newSize = _resizeStartSize + delta;
                    _rect.width  = Mathf.Max(MinPanelSize.x, newSize.x);
                    _rect.height = Mathf.Max(MinPanelSize.y, newSize.y);
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (_resizing) { _resizing = false; ev.Use(); }
                break;
        }
    }

    private void DrawTabs()
    {
        GUILayout.BeginHorizontal();
        if (Config.ShowHomeTab.Value)      DrawTab(Tab.Home,      "Home");
        if (Config.ShowBuildingTab.Value)  DrawTab(Tab.Building,  "Building");
        if (Config.ShowGoodTab.Value)      DrawTab(Tab.Good,      "Good");
        if (Config.ShowVillagersTab.Value) DrawTab(Tab.Villagers, "Villagers");
        if (Config.ShowOrdersTab.Value)    DrawTab(Tab.Orders,    "Orders");
        if (Config.ShowGladesTab.Value)    DrawTab(Tab.Glades,    "Glades");
        if (Config.ShowDraftTab.Value)     DrawTab(Tab.Draft,     "Draft");
        if (Config.ShowSettingsTab.Value)  DrawTab(Tab.Settings,  "⚙");
        if (Config.ShowDiagnosticsTab.Value) DrawTab(Tab.Diagnostics, "⚙?");
        if (Config.ShowEmbarkTab.Value)    DrawTab(Tab.Embark,    "Embark");
        GUILayout.EndHorizontal();

        // Reroute to the first visible tab if the active one is hidden.
        if (!IsTabVisible(_activeTab))
        {
            if      (Config.ShowHomeTab.Value)      _activeTab = Tab.Home;
            else if (Config.ShowBuildingTab.Value)  _activeTab = Tab.Building;
            else if (Config.ShowGoodTab.Value)      _activeTab = Tab.Good;
            else if (Config.ShowVillagersTab.Value) _activeTab = Tab.Villagers;
            else if (Config.ShowOrdersTab.Value)    _activeTab = Tab.Orders;
            else if (Config.ShowGladesTab.Value)    _activeTab = Tab.Glades;
            else if (Config.ShowDraftTab.Value)     _activeTab = Tab.Draft;
            else if (Config.ShowSettingsTab.Value)  _activeTab = Tab.Settings;
            else if (Config.ShowDiagnosticsTab.Value) _activeTab = Tab.Diagnostics;
            else if (Config.ShowEmbarkTab.Value)    _activeTab = Tab.Embark;
        }
    }

    private bool IsTabVisible(Tab tab) => tab switch
    {
        Tab.Home        => Config.ShowHomeTab.Value,
        Tab.Building    => Config.ShowBuildingTab.Value,
        Tab.Good        => Config.ShowGoodTab.Value,
        Tab.Villagers   => Config.ShowVillagersTab.Value,
        Tab.Orders      => Config.ShowOrdersTab.Value,
        Tab.Glades      => Config.ShowGladesTab.Value,
        Tab.Draft       => Config.ShowDraftTab.Value,
        Tab.Settings    => Config.ShowSettingsTab.Value,
        Tab.Diagnostics => Config.ShowDiagnosticsTab.Value,
        Tab.Embark      => Config.ShowEmbarkTab.Value,
        _ => true
    };

    private void DrawTab(Tab tab, string label)
    {
        var style = (_activeTab == tab) ? _tabActiveStyle : _tabStyle;
        // Append a Ctrl+N shortcut hint so power users discover the bindings.
        var idx = Array.IndexOf(TabOrder, tab);
        var hint = idx >= 0 && idx < 9 ? $" \u00b7{idx + 1}" : "";
        if (GUILayout.Button(
                new GUIContent(label + hint,
                    idx >= 0 && idx < 9 ? $"Ctrl+{idx + 1}" : null),
                style))
            _activeTab = tab;
    }

    /// <summary>
    /// Wraps a draw action in a stopwatch so the live-debug overlay can
    /// surface per-section frame cost when the user holds Shift.
    /// </summary>
    private void Section(string name, Action draw)
    {
        _sectionWatch ??= new System.Diagnostics.Stopwatch();
        _sectionWatch.Restart();
        try { draw(); }
        finally
        {
            _sectionWatch.Stop();
            var ms = _sectionWatch.Elapsed.TotalMilliseconds;
            _sectionMs[name] = ms;
            // Also push into the long-running perf ring so Diagnostics can
            // surface p50/p95 across the last 120 frames per section.
            if (!_perfHistory.TryGetValue(name, out var q))
                _perfHistory[name] = q = new Queue<double>(128);
            q.Enqueue(ms);
            while (q.Count > 120) q.Dequeue();
        }
    }

    /// <summary>
    /// Renders per-section ms timings in the bottom-right of the panel while
    /// Shift is held. Strictly diagnostic; no behavioural impact.
    /// </summary>
    private void DrawDebugOverlay()
    {
        if (_sectionMs.Count == 0) return;
        var sb = new System.Text.StringBuilder("debug:\n");
        foreach (var kv in _sectionMs)
            sb.Append(kv.Key).Append(": ").Append(kv.Value.ToString("0.0")).Append("ms\n");
        var rect = new Rect(_rect.width - 140, _rect.height - 90, 132, 80);
        GUI.Label(rect, sb.ToString(), _mutedStyle);
    }

    /// <summary>
    /// Writes a small crash report (last log lines + exception) next to the
    /// BepInEx config. Best-effort; failures here are deliberately silent.
    /// </summary>
    private void WriteCrashDump(Exception ex)
    {
        try
        {
            var dir = BepInEx.Paths.ConfigPath ?? "";
            if (string.IsNullOrEmpty(dir)) return;
            var path = System.IO.Path.Combine(dir,
                $"stormguide-crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"StormGuide crash @ {DateTime.UtcNow:O}");
            sb.AppendLine("---- exception ----");
            sb.AppendLine(ex.ToString());
            var tail = StormGuidePlugin.LogTail?.Snapshot();
            if (tail != null)
            {
                sb.AppendLine();
                sb.AppendLine($"---- last {tail.Count} log lines ----");
                foreach (var e in tail)
                    sb.AppendLine($"{e.UtcAt:O} [{e.Level}] {e.Message}");
            }
            System.IO.File.WriteAllText(path, sb.ToString());
        }
        catch { /* swallow — dumps are best-effort */ }
    }

    private void DrawHomeTab()
    {
        if (!LiveGameState.IsReady)
        {
            GUILayout.Label("Settlement at a glance", _h1Style);
            GUILayout.Label("Waiting for a settlement to load. The Home tab summarises live data once the game is ready.", _mutedStyle);
            return;
        }

        _homeScroll = GUILayout.BeginScrollView(_homeScroll, GUILayout.ExpandHeight(true));
        GUILayout.Label("Settlement at a glance", _h1Style);

        DrawHomePinned();
        DrawHomeFuel();
        DrawHomeVillage();
        DrawHomeTrader();
        DrawHomeIdle();
        DrawHomeRebalance();
        DrawHomeRisks();
        DrawHomeNeeds();
        DrawHomeOrders();
        DrawHomeGlades();
        DrawHomeCornerstones();

        GUILayout.EndScrollView();
    }

    private void DrawHomePinned()
    {
        if (_pinned.Count == 0) return;

        GUILayout.Space(4);
        GUILayout.Label($"☆ Pinned recipes — {_pinned.Count}", _bodyStyle);
        var stale = new List<(string, string)>();
        // Group pins by their building's catalog Kind so long pin lists get
        // visual chapter breaks (Workshops vs Services vs Farms etc).
        string? lastGroup = null;
        foreach (var (bldg, recipe) in _pinned
            .OrderBy(p =>
                Catalog.Buildings.TryGetValue(p.Building, out var bi)
                    ? bi.Kind.ToString()
                    : "",
                StringComparer.OrdinalIgnoreCase))
        {
            // Emit a category header before the first pin of each group.
            var group = Catalog.Buildings.TryGetValue(bldg, out var bgi)
                ? bgi.Kind.ToString() : "(other)";
            if (group != lastGroup)
            {
                GUILayout.Space(2);
                GUILayout.Label($"   · {group}", _mutedStyle);
                lastGroup = group;
            }
            if (!Catalog.Buildings.TryGetValue(bldg, out var b) ||
                !Catalog.Recipes.TryGetValue(recipe, out var r))
            {
                stale.Add((bldg, recipe));
                continue;
            }
            var perMin = r.ProductionTime > 0
                ? (60.0 * r.ProducedAmount) / r.ProductionTime
                : 0;
            var stock = LiveGameState.IsReady ? LiveGameState.StockpileOf(r.ProducedGood) : 0;
            var stockTag = LiveGameState.IsReady ? $" · stock {stock}" : "";
            // ETA forecast: minutes to fill the produced good to a target.
            string forecast = "";
            if (LiveGameState.IsReady && stock < PinForecastTarget)
            {
                var net = LiveGameState.FlowFor(r.ProducedGood, Catalog).Net;
                if (net > 1e-6)
                {
                    var min = (PinForecastTarget - stock) / net;
                    if (min > 0 && min < 999) forecast = $" · → {PinForecastTarget} in ~{min:0.#}m";
                }
            }
            stockTag += forecast;
            // Storm-clock chip: how many cycles will fit before the next
            // weather phase ends? Useful for "is this pin worth running NOW".
            if (LiveGameState.IsReady && r.ProductionTime > 0)
            {
                var w = LiveGameState.Weather();
                if (w?.SecondsLeft is float sl && sl > 0f)
                {
                    var cycles = (int)Math.Floor(sl / r.ProductionTime);
                    if (cycles >= 0 && cycles < 200)
                        stockTag += $" \u00b7 {cycles} cycle(s) before phase";
                }
            }
            // Marker chips: surface UI-only flags inline so the row reads as
            // "this pin is paused / prioritised" without scrolling the recipe
            // back open. Toggled from the Building tab recipe card.
            if (_markedStopped.Contains(recipe))  stockTag += " \u00b7 \u26d4 stopped";
            if (_markedPriority.Contains(recipe)) stockTag += " \u00b7 \u2605 priority";
            // Inputs draining tag: any input good with net flow < 0 across
            // the settlement gets a ⚠ prefix so the row reads as at-risk.
            var draining = false;
            if (LiveGameState.IsReady)
            {
                foreach (var slot in r.RequiredGoods)
                foreach (var opt in slot.Options)
                {
                    if (LiveGameState.FlowFor(opt.Good, Catalog).Net < -1e-6)
                    { draining = true; break; }
                }
            }
            // Cross-recipe upstream rollup (1 level): for each input, find
            // the catalog's primary producer. Surface as a small “needs:”
            // line so the player sees the full pipeline at a glance.
            var upstream = r.RequiredGoods
                .SelectMany(slot => slot.Options)
                .Select(opt => Catalog.Recipes.Values
                    .Where(rr => string.Equals(rr.ProducedGood, opt.Good, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(rr => rr.ProductionTime > 0
                        ? (60.0 * rr.ProducedAmount) / rr.ProductionTime : 0)
                    .Select(rr => rr.DisplayName)
                    .FirstOrDefault())
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            var prefix = draining ? "⚠ " : "   ";
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    new GUIContent($"{prefix}{b.DisplayName} → {r.DisplayName}: {perMin:0.##}/min{stockTag}",
                                   draining ? "At least one input is draining — open in Building tab"
                                            : "Open in Building tab"),
                    draining ? (_warnStyle ?? _tabStyle) : _tabStyle))
            {
                _selectedBuilding = bldg;
                _activeTab = Tab.Building;
            }
            // Inline 60s flow sparkline for the produced good (sampled by
            // TickPinnedFlows even when the good isn't currently at-risk).
            if (LiveGameState.IsReady && !string.IsNullOrEmpty(r.ProducedGood))
                DrawFlowSparkline(r.ProducedGood);
            // Up/down reorder buttons (no-op at the edges).
            var idx = _pinned.IndexOf((bldg, recipe));
            if (GUILayout.Button(new GUIContent("▴", "Move up"),
                                 _tabStyle, GUILayout.Width(24)))
            {
                if (idx > 0)
                {
                    var tmp = _pinned[idx - 1];
                    _pinned[idx - 1] = _pinned[idx];
                    _pinned[idx] = tmp;
                    SavePins();
                }
            }
            if (GUILayout.Button(new GUIContent("▾", "Move down"),
                                 _tabStyle, GUILayout.Width(24)))
            {
                if (idx >= 0 && idx < _pinned.Count - 1)
                {
                    var tmp = _pinned[idx + 1];
                    _pinned[idx + 1] = _pinned[idx];
                    _pinned[idx] = tmp;
                    SavePins();
                }
            }
            if (GUILayout.Button(new GUIContent("unpin", "Remove from Home"),
                                 _tabStyle, GUILayout.Width(60)))
            {
                stale.Add((bldg, recipe));
            }
            GUILayout.EndHorizontal();
            if (upstream.Count > 0)
                GUILayout.Label(
                    "     needs: " + string.Join(" → ", upstream),
                    _mutedStyle);
        }
        if (stale.Count > 0)
        {
            foreach (var s in stale) _pinned.RemoveAll(x => x == s);
            SavePins();
        }
    }

    /// <summary>
    /// Surfaces worker-rebalance suggestions (idle workshop → underfilled
    /// building producing a draining good) so the player has a one-glance
    /// list of "reassign these" actions.
    /// </summary>
    private void DrawHomeRebalance()
    {
        var hints = LiveGameState.WorkerRebalanceHints(Catalog);
        if (hints.Count == 0) return;
        GUILayout.Space(6);
        GUILayout.Label($"⚠ Worker rebalance — {hints.Count} suggestion(s)", _bodyStyle);
        foreach (var h in hints.Take(4))
        {
            GUILayout.Label(
                $"   move from {h.FromBuilding} → {h.ToBuilding}: {h.Reason}",
                _warnStyle ?? _mutedStyle);
        }
        if (hints.Count > 4)
            GUILayout.Label($"   … and {hints.Count - 4} more", _mutedStyle);
    }

    /// <summary>
    /// Hearth fuel runway: total burnable stockpile + estimated minutes left.
    /// Burn rate is sourced live when the game exposes it; otherwise we use a
    /// 6-units/min heuristic and tag the row as "est.".
    /// </summary>
    private void DrawHomeFuel()
    {
        var fr = LiveGameState.FuelRunwayFor(Catalog);
        if (fr is null || fr.Stockpile == 0) return;

        GUILayout.Space(6);
        var srcTag = fr.IsLiveBurnRate ? "live" : "est.";
        GUILayout.Label(
            $"● Fuel — {fr.Stockpile} units · ~{fr.RunwayMinutes:0.#} min runway ({srcTag} {fr.EstimatedBurnPerMinute:0.#}/min)",
            fr.RunwayMinutes < 5 ? (_critStyle ?? _bodyStyle)
            : fr.RunwayMinutes < 15 ? (_warnStyle ?? _bodyStyle)
            : _bodyStyle);
        // Top-3 contributing fuel goods so the player can see what to refill.
        foreach (var (good, n) in fr.ByGood.Take(3))
            GUILayout.Label($"   {good}: {n}", _mutedStyle);
        if (fr.ByGood.Count > 3)
            GUILayout.Label($"   … and {fr.ByGood.Count - 3} more", _mutedStyle);
    }

    /// <summary>Persist <see cref="_pinned"/> back to <see cref="PluginConfig.PinnedRecipes"/>.</summary>
    private void SavePins()
    {
        Config.PinnedRecipes.Value = string.Join(";",
            _pinned.Select(p => p.Building + "|" + p.Recipe));
    }

    private void DrawHomeNeeds()
    {
        var summary = _summaryCache?.Get();
        if (summary is null || summary.Races.Count == 0) return;

        // For each living race, list the catalog needs whose stockpile is 0.
        var unmet = new List<string>();
        foreach (var p in summary.Races)
        {
            if (p.Alive == 0) continue;
            if (!Catalog.Races.TryGetValue(p.Race, out var info)) continue;
            foreach (var need in info.Needs)
            {
                if (string.IsNullOrEmpty(need)) continue;
                if (LiveGameState.StockpileOf(need) > 0) continue;
                var disp = Catalog.Goods.TryGetValue(need, out var gi) ? gi.DisplayName : need;
                unmet.Add($"{p.DisplayName}→{disp}");
            }
        }
        if (unmet.Count == 0) return;

        GUILayout.Space(6);
        GUILayout.Label($"⚠ Race needs unmet — {unmet.Count}", _bodyStyle);
        // Cap the list so the home tab doesn't unbounded-grow on starvation.
        foreach (var line in unmet.Take(6))
            GUILayout.Label($"   · {line}", _mutedStyle);
        if (unmet.Count > 6)
            GUILayout.Label($"   … and {unmet.Count - 6} more", _mutedStyle);
    }

    private void DrawHomeVillage()
    {
        var summary = _summaryCache?.Get();
        if (summary is null || summary.Races.Count == 0) return;

        var below = 0; var homeless = 0;
        foreach (var p in summary.Races)
        {
            if (p.Alive == 0 && p.Total == 0) continue;
            if (p.CurrentResolve < p.TargetResolve) below++;
            homeless += p.Homeless;
        }

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"● Village  —  {summary.TotalVillagers} villagers" +
                        (homeless > 0 ? $"  ·  {homeless} homeless" : ""),
                        _bodyStyle);
        GUILayout.FlexibleSpace();
        if (Config.ShowVillagersTab.Value &&
            GUILayout.Button(new GUIContent("open ›", "Jump to Villagers tab"),
                             _tabStyle, GUILayout.Width(70)))
        {
            _activeTab = Tab.Villagers;
        }
        GUILayout.EndHorizontal();

        var line = string.Join("  ·  ", summary.Races
            .Where(p => p.Alive > 0 || p.Total > 0)
            .Select(p =>
            {
                var arrow = p.CurrentResolve >= p.TargetResolve ? "↑" : "↓";
                return $"{p.DisplayName} {p.Alive} · {p.CurrentResolve:0.#}/{p.TargetResolve} {arrow}";
            }));
        GUILayout.Label(line, _mutedStyle);
        if (below > 0)
            GUILayout.Label($"⚠ {below} race{(below == 1 ? "" : "s")} below target resolve", _mutedStyle);
        DrawRaceRatioDrift(summary);
    }

    /// <summary>
    /// Compares the live race ratio to the user's configured targets (e.g.
    /// "beaver=30,human=40"). Any race more than 10 percentage points off
    /// target is surfaced as a drift warning.
    /// </summary>
    private void DrawRaceRatioDrift(StormGuide.Domain.VillageSummary summary)
    {
        var raw = (Config.RaceRatioTargets.Value ?? "").Trim();
        if (raw.Length == 0 || summary.TotalVillagers == 0) return;
        var targets = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in raw.Split(','))
        {
            var kv = pair.Split('=');
            if (kv.Length != 2) continue;
            if (double.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pct))
                targets[kv[0].Trim()] = pct;
        }
        if (targets.Count == 0) return;
        foreach (var p in summary.Races)
        {
            if (p.Alive == 0) continue;
            // Match against either model name or display name so users can
            // spell it however they prefer in the config.
            double tgt = 0;
            if (!targets.TryGetValue(p.Race, out tgt) &&
                !targets.TryGetValue(p.DisplayName, out tgt)) continue;
            var actual = 100.0 * p.Alive / Math.Max(1, summary.TotalVillagers);
            var drift = actual - tgt;
            if (Math.Abs(drift) < 10) continue;
            GUILayout.Label(
                $"   ratio drift: {p.DisplayName} {actual:0.#}% vs target {tgt:0.#}% ({drift:+0.#;-0.#}pp)",
                _warnStyle ?? _mutedStyle);
        }
    }

    private void DrawHomeTrader()
    {
        var cur = LiveGameState.CurrentTrader();
        var nxt = LiveGameState.NextTrader();
        if (cur is null && nxt is null) return;

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label("● Trade", _bodyStyle);
        GUILayout.FlexibleSpace();
        if (Config.ShowGoodTab.Value &&
            GUILayout.Button(new GUIContent("open ›", "Jump to Good tab"),
                             _tabStyle, GUILayout.Width(70)))
        {
            _activeTab = Tab.Good;
        }
        GUILayout.EndHorizontal();

        if (cur is not null)
        {
            DrawTraderDesireHeatmap(cur);
            var disp  = string.IsNullOrEmpty(cur.DisplayName) ? "(unnamed)" : cur.DisplayName;
            var here  = cur.IsInVillage ? "in village" : "en route";
            var extra = "";
            if (!cur.IsInVillage)
            {
                var p = LiveGameState.CurrentTraderTravelProgress();
                if (p is float pf) extra = $" · {pf * 100f:0}%";
            }
            GUILayout.Label(
                $"   current: {disp} ({here}{extra}) · wants {cur.Buys.Count} · sells {cur.Sells.Count}",
                _mutedStyle);
            // Visit countdown: estimate seconds-until-arrival from the recent
            // travel-progress samples (Δprogress / Δtime), only when we have
            // at least two samples and a positive rate.
            if (!cur.IsInVillage && _traderTravelSamples.Count >= 2)
            {
                var first = _traderTravelSamples.First();
                var last  = _traderTravelSamples.Last();
                var dProg = last.Progress - first.Progress;
                var dTime = last.Time - first.Time;
                if (dProg > 1e-4 && dTime > 0)
                {
                    var rate = dProg / dTime; // progress per second
                    var remaining = (1f - last.Progress) / rate;
                    if (remaining > 0 && remaining < 7200)
                    {
                        var mins = Mathf.FloorToInt(remaining / 60f);
                        var secs = Mathf.FloorToInt(remaining % 60f);
                        GUILayout.Label(
                            $"   ~{mins}:{secs:00} to arrival (extrapolated)",
                            _mutedStyle);
                    }
                }
            }

            // Top trader desire (best total-value good to sell right now).
            var desires = _currentDesiresCache?.Get()
                          ?? LiveGameState.RankTraderDesires(cur, Catalog, isCurrent: true);
            if (desires.Count > 0)
            {
                var d = desires[0];
                var marker = Config.ShowRecommendations.Value ? "★" : "→";
                var line = $"   {marker} sell {d.DisplayName}: {d.PricePerUnit:0.##}/u × {d.Stockpile} = {d.TotalValue:0.##}";
                if (GUILayout.Button(new GUIContent(line, "Open this good"), _tabStyle))
                {
                    _selectedGood = d.Good;
                    _activeTab    = Tab.Good;
                }
            }
            // Combined revenue across both traders' top-3 desires — a quick
            // "how much trade is on the table this rotation" headline.
            var nextDesires = _nextDesiresCache?.Get()
                              ?? (nxt is null
                                  ? Array.Empty<LiveGameState.TraderDesire>()
                                  : LiveGameState.RankTraderDesires(nxt, Catalog, isCurrent: false));
            var revenue = desires.Take(3).Sum(d => d.TotalValue)
                        + nextDesires.Take(3).Sum(d => d.TotalValue);
            if (revenue > 0f)
                GUILayout.Label(
                    $"   potential trade revenue (top-3 each): {revenue:0.##}",
                    _mutedStyle);
        }
        if (nxt is not null)
        {
            var disp = string.IsNullOrEmpty(nxt.DisplayName) ? "(unnamed)" : nxt.DisplayName;
            GUILayout.Label(
                $"   next: {disp} · wants {nxt.Buys.Count} · sells {nxt.Sells.Count}",
                _mutedStyle);
        }
        // Buy-list builder: pick from the current trader's Sells, see total
        // currency cost vs current pot from selling top-3 desires.
        if (cur is not null && cur.IsInVillage && cur.Sells.Count > 0)
            DrawTraderBuyList(cur);
    }

    /// <summary>
    /// Renders a small "shopping list" panel under the current trader. The
    /// user ticks goods they want to buy; the panel sums the per-unit cost
    /// and compares against the available currency from selling top-3 desires.
    /// Selections reset on visit change.
    /// </summary>
    private void DrawTraderBuyList(LiveGameState.TraderInfo cur)
    {
        var visitKey = cur.DisplayName ?? "";
        if (_buyListVisitKey != visitKey)
        {
            _buyListVisitKey = visitKey;
            _buyListSelections.Clear();
        }
        GUILayout.Space(2);
        GUILayout.Label("   buy list — click to tick:", _mutedStyle);
        // Render up to 8 sells per row of toggles.
        var sells = cur.Sells.Take(12).ToList();
        double totalCost = 0;
        GUILayout.BeginHorizontal();
        for (var i = 0; i < sells.Count; i++)
        {
            var s = sells[i];
            var disp = Catalog.Goods.TryGetValue(s, out var gi) ? gi.DisplayName : s;
            var on = _buyListSelections.Contains(s);
            var price = LiveGameState.BuyValueAtCurrentTrader(s);
            if (on) totalCost += price;
            var label = $"{(on ? "✓" : "·")} {disp} ({price:0.##})";
            if (GUILayout.Button(label, _chipStyle ?? _tabStyle))
            {
                if (on) _buyListSelections.Remove(s); else _buyListSelections.Add(s);
            }
            if ((i + 1) % 4 == 0 && i < sells.Count - 1)
            {
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }
        }
        GUILayout.EndHorizontal();
        if (_buyListSelections.Count > 0)
        {
            var pot = (_currentDesiresCache?.Get() ?? Array.Empty<LiveGameState.TraderDesire>())
                .Take(3).Sum(d => d.TotalValue);
            var afford = pot >= totalCost ? "✓ affordable" : "✗ short";
            var diff = pot - totalCost;
            GUILayout.Label(
                $"   list cost: {totalCost:0.##} · pot (top-3 sells): {pot:0.##} · {afford} ({diff:+0.##;-0.##;0})",
                pot >= totalCost ? (_okStyle ?? _mutedStyle)
                                 : (_critStyle ?? _mutedStyle));
        }
    }

    /// <summary>
    /// Heatmap: one coloured square per good the current trader wants. Red =
    /// no stockpile, amber = some, green = a meaningful pile. Quick eyeball
    /// of "can I trade right now?".
    /// </summary>
    private void DrawTraderDesireHeatmap(LiveGameState.TraderInfo cur)
    {
        if (cur.Buys.Count == 0) return;
        const float cell = 10f;
        var goods = cur.Buys.Take(20).ToList();
        var rect = GUILayoutUtility.GetRect(
            (cell + 2) * goods.Count, cell + 4,
            GUILayout.Width((cell + 2) * goods.Count));
        rect.xMin += 4;
        for (var i = 0; i < goods.Count; i++)
        {
            var stock = LiveGameState.StockpileOf(goods[i]);
            var c = stock <= 0      ? new Color(0.95f, 0.45f, 0.45f, 0.85f)
                  : stock < 20      ? new Color(0.95f, 0.80f, 0.30f, 0.85f)
                                    : new Color(0.45f, 0.85f, 0.55f, 0.85f);
            var box = new Rect(rect.x + i * (cell + 2), rect.y + 2, cell, cell);
            GUI.DrawTexture(box, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                false, 0, c, 0, 0);
        }
    }

    private void DrawHomeIdle()
    {
        var idle = LiveGameState.IdleBuildings();
        if (idle.Count == 0) return;

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"⚠ Idle workshops — {idle.Count}", _bodyStyle);
        GUILayout.FlexibleSpace();
        if (Config.ShowBuildingTab.Value &&
            GUILayout.Button(new GUIContent("open ›", "Jump to Building tab"),
                             _tabStyle, GUILayout.Width(70)))
        {
            _activeTab = Tab.Building;
        }
        GUILayout.EndHorizontal();

        // Top-3 grouped by model so duplicates collapse.
        var grouped = idle
            .GroupBy(t => t.ModelName)
            .OrderByDescending(g => g.Count())
            .Take(3);
        foreach (var g in grouped)
        {
            var name = g.First().DisplayName;
            if (GUILayout.Button(
                    new GUIContent($"   {g.Count()}× {name}", "Open in Building tab"),
                    _tabStyle))
            {
                _activeTab = Tab.Building;
                _selectedBuilding = g.Key;
            }
        }
    }

    private void DrawHomeRisks()
    {
        var alerts = _alertsCache?.Get();
        if (alerts is null || alerts.GoodsAtRisk.Count == 0) return;

        // Sample net flows once a second so the sparkline doesn't churn.
        SampleAtRiskFlows(alerts);

        GUILayout.Space(6);
        GUILayout.Label($"⚠ Goods at risk — {alerts.GoodsAtRisk.Count}", _bodyStyle);
        foreach (var g in alerts.GoodsAtRisk.Take(5))
        {
            var disp = Catalog.Goods.TryGetValue(g.Good, out var gi) ? gi.DisplayName : g.Good;
            var label = $"   {disp}  —  {g.RunwayMinutes:0.#} min runway";
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent(label, "Open in Good tab"), _tabStyle))
            {
                _activeTab = Tab.Good;
                _selectedGood = g.Good;
                _flowExpanded = true;
            }
            DrawFlowSparkline(g.Good);
            DrawFlowForecastChip(g.Good, g.RunwayMinutes);
            // Auto-pin: find the top producer recipe for this draining good
            // and add it to the pinned list with one click. Skipped when no
            // catalog recipe produces it or when it's already pinned.
            DrawAutoPinButton(g.Good);
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>
    /// Renders a small "pin" button on an at-risk row that pins the highest-
    /// throughput catalog producer of <paramref name="good"/> to Home. Hides
    /// the button when there's no catalog producer or the pair is already
    /// pinned, so the row stays clean for non-actionable cases.
    /// </summary>
    private void DrawAutoPinButton(string good)
    {
        var top = Catalog.Recipes.Values
            .Where(rr => string.Equals(rr.ProducedGood, good, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rr => rr.ProductionTime > 0
                ? (60.0 * rr.ProducedAmount) / rr.ProductionTime : 0)
            .FirstOrDefault();
        if (top is null) return;
        // Locate the building that hosts this recipe (catalog Recipes carry no
        // back-pointer, so scan Buildings for a recipe-name match).
        var building = Catalog.Buildings.Values
            .FirstOrDefault(b => b.Recipes.Contains(top.Name));
        if (building is null) return;
        var key = (building.Name, top.Name);
        if (_pinned.Contains(key))
        {
            GUILayout.Label("✓ pinned", _okStyle ?? _mutedStyle, GUILayout.Width(70));
            return;
        }
        if (GUILayout.Button(new GUIContent("☆ auto-pin",
                $"Pin {building.DisplayName} → {top.DisplayName} to Home"),
                _tabStyle, GUILayout.Width(80)))
        {
            _pinned.Add(key);
            SavePins();
        }
    }

    /// <summary>
    /// 60-second forecast extrapolation from the recent net-flow ring. Fits a
    /// linear trend across <see cref="_flowSamples"/> for <paramref name="good"/>
    /// and renders a small "→ X.Ym" chip indicating the projected runway one
    /// minute from now (or "steady" when the slope is negligible).
    /// </summary>
    private void DrawFlowForecastChip(string good, double currentRunwayMin)
    {
        if (!_flowSamples.TryGetValue(good, out var q) || q.Count < 4) return;
        var samples = q.ToArray();
        var n = samples.Length;
        // Simple linear fit: slope of net flow over sample index (1 sample/s).
        double meanX = (n - 1) / 2.0, meanY = 0;
        for (var i = 0; i < n; i++) meanY += samples[i];
        meanY /= n;
        double num = 0, den = 0;
        for (var i = 0; i < n; i++)
        {
            num += (i - meanX) * (samples[i] - meanY);
            den += (i - meanX) * (i - meanX);
        }
        var slopePerSec = den > 1e-9 ? num / den : 0; // Δnet per sample (~1s)
        if (Math.Abs(slopePerSec) < 0.001)
        {
            GUILayout.Label("steady", _mutedStyle, GUILayout.Width(50));
            return;
        }
        // Project net 60s ahead, recompute runway from the projected net.
        var projectedNet = samples[n - 1] + slopePerSec * 60.0;
        var stock = LiveGameState.IsReady ? LiveGameState.StockpileOf(good) : 0;
        if (projectedNet >= 0 || stock <= 0)
        {
            GUILayout.Label(slopePerSec > 0 ? "↗ recovering" : "→ ?",
                _mutedStyle, GUILayout.Width(80));
            return;
        }
        var projRunwayMin = stock / -projectedNet;
        var arrow = projRunwayMin < currentRunwayMin ? "↘" : "↗";
        GUILayout.Label($"{arrow} {projRunwayMin:0.#}m in 60s",
            projRunwayMin < currentRunwayMin ? (_critStyle ?? _mutedStyle)
            : _mutedStyle, GUILayout.Width(110));
    }

    /// <summary>
    /// Pushes a fresh net-flow sample for each at-risk good every ~1 second.
    /// Bounded to 30 samples per good so the dictionary stays small.
    /// </summary>
    private void SampleAtRiskFlows(LiveGameState.SettlementAlerts alerts)
    {
        var now = LiveGameState.GameTimeNow() ?? Time.unscaledTime;
        if (now - _lastSampleTime < 1f) return;
        _lastSampleTime = now;
        foreach (var g in alerts.GoodsAtRisk)
        {
            if (!_flowSamples.TryGetValue(g.Good, out var q))
                _flowSamples[g.Good] = q = new Queue<double>(32);
            q.Enqueue(LiveGameState.FlowFor(g.Good, Catalog).Net);
            while (q.Count > 30) q.Dequeue();
        }
    }

    /// <summary>
    /// Renders a small inline sparkline of recent net-flow samples for the
    /// given good. Shows nothing when we haven't collected enough data yet.
    /// </summary>
    private void DrawFlowSparkline(string good)
    {
        if (!_flowSamples.TryGetValue(good, out var q) || q.Count < 2) return;
        var rect = GUILayoutUtility.GetRect(60, 14, GUILayout.Width(60));
        GUI.Box(rect, GUIContent.none);
        var samples = q.ToArray();
        var max = 0.001;
        for (var i = 0; i < samples.Length; i++) max = Math.Max(max, Math.Abs(samples[i]));
        var w = rect.width / samples.Length;
        for (var i = 0; i < samples.Length; i++)
        {
            var v = samples[i];
            var hNorm = (float)Math.Min(1.0, Math.Abs(v) / max);
            var bar = new Rect(
                rect.x + i * w,
                v >= 0 ? rect.y + rect.height * 0.5f - rect.height * 0.5f * hNorm
                       : rect.y + rect.height * 0.5f,
                Mathf.Max(1f, w - 1f),
                rect.height * 0.5f * hNorm);
            var color = v >= 0
                ? new Color(0.45f, 0.85f, 0.55f, 0.8f)
                : new Color(0.95f, 0.45f, 0.45f, 0.8f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                false, 0, color, 0, 0);
        }
    }

    private void DrawHomeOrders()
    {
        var orders = LiveGameState.ActiveOrders();
        if (orders.Count == 0) return;

        var picked  = orders.Count(o => o.Picked);
        var tracked = orders.Count(o => o.Tracked);
        var critical = orders.Count(o => o.ShouldBeFailable && o.TimeLeft > 0f && o.TimeLeft < 60f);
        // ETA-based "won't finish in time" count: any failable order whose
        // worst-case objective ETA exceeds remaining time.
        var late = 0;
        foreach (var o in orders)
        {
            if (!o.ShouldBeFailable || o.TimeLeft <= 0f) continue;
            var minutesLeft = o.TimeLeft / 60.0;
            foreach (var ob in o.Objectives)
            {
                var eta = LiveGameState.ObjectiveEtaMinutes(ob, Catalog);
                if (eta is double m && m > minutesLeft) { late++; break; }
            }
        }

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            $"● Orders — {orders.Count} active ({picked} picked, {tracked} tracked)",
            _bodyStyle);
        GUILayout.FlexibleSpace();
        if (Config.ShowOrdersTab.Value &&
            GUILayout.Button(new GUIContent("open ›", "Jump to Orders tab"),
                             _tabStyle, GUILayout.Width(70)))
        {
            _activeTab = Tab.Orders;
        }
        GUILayout.EndHorizontal();
        if (critical > 0)
            GUILayout.Label(
                $"   ⚠ {critical} order{(critical == 1 ? "" : "s")} under 1 min from failure",
                _mutedStyle);
        if (late > 0)
            GUILayout.Label(
                $"   ⚠ {late} order{(late == 1 ? "" : "s")} won't finish in time at current burn",
                _warnStyle ?? _mutedStyle);
        // Average completion time across this session’s lookback ring.
        if (_completedDurations.Count > 0)
        {
            var avg = _completedDurations.Average() / 60.0;
            GUILayout.Label(
                $"   session avg completion: ~{avg:0.#}m across {_completedDurations.Count} order(s)",
                _mutedStyle);
        }
    }

    private void DrawHomeGlades()
    {
        var summary = LiveGameState.GladeSummaryFor();
        if (summary is null || summary.Total == 0) return;

        var explored = summary.Discovered * 100f / summary.Total;
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            $"● Forest — {summary.Discovered}/{summary.Total} glades ({explored:0}%)",
            _bodyStyle);
        GUILayout.FlexibleSpace();
        if (Config.ShowGladesTab.Value &&
            GUILayout.Button(new GUIContent("open ›", "Jump to Glades tab"),
                             _tabStyle, GUILayout.Width(70)))
        {
            _activeTab = Tab.Glades;
        }
        GUILayout.EndHorizontal();
        if (summary.RewardChasesActive > 0)
            GUILayout.Label(
                $"   ⚠ {summary.RewardChasesActive} reward-chase{(summary.RewardChasesActive == 1 ? "" : "s")} active",
                _mutedStyle);

        // Pinned chase: surface above the auto-priority pick if the user has
        // explicitly stuck one to Home. Includes a dismiss button. Persists
        // via Config.PinnedChaseModel until cleared or the chase resolves.
        if (!string.IsNullOrEmpty(Config.PinnedChaseModel.Value))
        {
            var pinnedChase = summary.Chases.FirstOrDefault(c =>
                string.Equals(c.Model, Config.PinnedChaseModel.Value,
                              StringComparison.Ordinal));
            GUILayout.BeginHorizontal();
            if (pinnedChase is null)
            {
                GUILayout.Label(
                    $"   ★ pinned chase: {Config.PinnedChaseModel.Value} (not active)",
                    _mutedStyle);
            }
            else
            {
                var nowP = LiveGameState.GameTimeNow();
                var rem = (nowP is float tt && pinnedChase.End > tt)
                    ? pinnedChase.End - tt : pinnedChase.Duration;
                var pm = Mathf.Max(0, Mathf.FloorToInt(rem / 60f));
                var ps = Mathf.Max(0, Mathf.FloorToInt(rem % 60f));
                GUILayout.Label(
                    $"   ★ pinned chase: {pinnedChase.Model} — {pm}:{ps:00} left",
                    _warnStyle ?? _bodyStyle);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("dismiss", "Unpin this chase"),
                                 _tabStyle, GUILayout.Width(70)))
                Config.PinnedChaseModel.Value = "";
            GUILayout.EndHorizontal();
        }

        // Best-next-chase pin: rank by (reward score / remaining time) so the
        // player chases the most valuable + soonest-to-expire one first.
        if (summary.Chases.Count > 0)
        {
            var now = LiveGameState.GameTimeNow();
            var best = summary.Chases
                .Select(c =>
                {
                    var remaining = (now is float t && c.End > t) ? c.End - t : c.Duration;
                    if (remaining <= 0) return (Chase: (LiveGameState.GladeRewardChase?)null, Priority: 0.0);
                    var rewardValue = c.Rewards.Sum(r =>
                        Catalog.Goods.TryGetValue(r, out var gi) ? gi.TradingBuyValue : 1.0);
                    if (rewardValue <= 0) rewardValue = c.Rewards.Count;
                    var priority = rewardValue / Math.Max(1, remaining);
                    return (Chase: (LiveGameState.GladeRewardChase?)c, Priority: priority);
                })
                .Where(t => t.Chase != null)
                .OrderByDescending(t => t.Priority)
                .FirstOrDefault();
            if (best.Chase is { } chase)
            {
                var remaining = (now is float t2 && chase.End > t2) ? chase.End - t2 : chase.Duration;
                var mins = Mathf.Max(0, Mathf.FloorToInt(remaining / 60f));
                var secs = Mathf.Max(0, Mathf.FloorToInt(remaining % 60f));
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"   ★ next chase: {chase.Model} — {mins}:{secs:00} left",
                    _warnStyle ?? _mutedStyle);
                GUILayout.FlexibleSpace();
                if (Config.PinnedChaseModel.Value != chase.Model &&
                    GUILayout.Button(new GUIContent("pin", "Pin this chase to Home"),
                                     _tabStyle, GUILayout.Width(60)))
                    Config.PinnedChaseModel.Value = chase.Model;
                GUILayout.EndHorizontal();
            }
        }
    }

    private void DrawHomeCornerstones()
    {
        var owned = _ownedCache?.Get();
        if (owned is null || owned.Count == 0) return;

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"● Cornerstones — {owned.Count} owned", _bodyStyle);
        GUILayout.FlexibleSpace();
        if (Config.ShowDraftTab.Value &&
            GUILayout.Button(new GUIContent("open ›", "Jump to Draft tab"),
                             _tabStyle, GUILayout.Width(70)))
        {
            _activeTab = Tab.Draft;
        }
        GUILayout.EndHorizontal();
        foreach (var o in owned.Take(3))
            GUILayout.Label($"   · {o.DisplayName}", _mutedStyle);
        if (owned.Count > 3)
            GUILayout.Label($"   … and {owned.Count - 3} more", _mutedStyle);
    }

    private void DrawBuildingTab()
    {
        DrawIdleBuildingsBanner();
        DrawBuildingRebalancePanel();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(56));
        _buildingSearch = GUILayout.TextField(_buildingSearch ?? "", GUILayout.Width(160));
        if (DrawClearButton(_selectedBuilding)) _selectedBuilding = null;
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{Catalog.Buildings.Count} known", _mutedStyle);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawBuildingList();
        GUILayout.Space(8);
        DrawBuildingDetail();
        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// Renders the live cycles counter for the currently-selected building
    /// model: cumulative total + a 5-minute delta if we have at least two
    /// samples that span enough time. Falls back to a muted dash when the
    /// game doesn't expose a counter we can read.
    /// </summary>
    private void DrawBuildingCyclesHeader(string modelName)
    {
        if (!LiveGameState.IsReady) return;
        if (!_cyclesSamples.TryGetValue(modelName, out var q) || q.Count == 0)
        {
            var live = LiveGameState.CyclesCompletedFor(modelName);
            if (live is null) return;
            GUILayout.Label($"   cycles: {live.Value} (sampling …)", _mutedStyle);
            return;
        }
        var samples = q.ToArray();
        var latest = samples[samples.Length - 1];
        // Find the first sample at least 5 minutes (300s) before the latest;
        // if none, fall back to the oldest available.
        (float Time, long Count) baseline = samples[0];
        for (var i = samples.Length - 1; i >= 0; i--)
        {
            if (latest.Time - samples[i].Time >= 300f) { baseline = samples[i]; break; }
        }
        var deltaCycles = latest.Count - baseline.Count;
        var deltaTime   = Math.Max(1f, latest.Time - baseline.Time);
        var perMin = deltaCycles * 60.0 / deltaTime;
        GUILayout.Label(
            $"   cycles: {latest.Count} total · {deltaCycles} in last {deltaTime:0}s (~{perMin:0.##}/min)",
            _mutedStyle);
    }

    /// <summary>
    /// Worker-rebalance panel under the Building tab: surfaces the same
    /// hint list as the Home block, but with one-click "open" buttons that
    /// jump straight to the source or target building.
    /// </summary>
    private void DrawBuildingRebalancePanel()
    {
        if (!LiveGameState.IsReady) return;
        var hints = LiveGameState.WorkerRebalanceHints(Catalog);
        if (hints.Count == 0) return;
        GUILayout.Space(2);
        GUILayout.Label($"⚠ Worker rebalance — {hints.Count}", _bodyStyle);
        // Map display name back to model name once for the open-buttons.
        var nameToModel = Catalog.Buildings.Values
            .GroupBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        foreach (var h in hints.Take(3))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"   {h.FromBuilding} → {h.ToBuilding}",
                _warnStyle ?? _mutedStyle);
            GUILayout.FlexibleSpace();
            if (nameToModel.TryGetValue(h.FromBuilding, out var fromModel) &&
                GUILayout.Button(new GUIContent("src", "Open source building"),
                    _tabStyle, GUILayout.Width(40)))
                _selectedBuilding = fromModel;
            if (nameToModel.TryGetValue(h.ToBuilding, out var toModel) &&
                GUILayout.Button(new GUIContent("dst", "Open target building"),
                    _tabStyle, GUILayout.Width(40)))
                _selectedBuilding = toModel;
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(2);
    }

    private void DrawIdleBuildingsBanner()
    {
        if (!LiveGameState.IsReady) return;
        // Idle list with best-effort root-cause tags so the player can act on
        // the actionable subset ("unstaffed" → hire, "no inputs" → supply).
        var idle = LiveGameState.IdleAnyBuildingsWithReasons(Catalog);
        if (idle.Count == 0) return;

        // Group by (model, reason) so duplicates collapse with their cause.
        var grouped = idle
            .GroupBy(t => (t.ModelName, t.DisplayName, t.Reason))
            .Select(g => string.IsNullOrEmpty(g.Key.Reason)
                ? $"{g.Count()}× {g.Key.DisplayName}"
                : $"{g.Count()}× {g.Key.DisplayName} ({g.Key.Reason})")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        GUILayout.Label("⚠ Idle workplaces: " + string.Join(", ", grouped), _bodyStyle);
        GUILayout.Space(2);
    }

    private void DrawBuildingList()
    {
        _buildingListScroll = GUILayout.BeginScrollView(_buildingListScroll,
            GUILayout.Width(160), GUILayout.ExpandHeight(true));

        // Cached match list — only recompute when the query (or hide-empty
        // filter) changes. Cuts per-frame LINQ allocations on long lists.
        var query = _buildingSearch?.Trim() ?? "";
        var cacheKey = (Config.HideEmptyRecipeBuildings.Value ? "H|" : "|") + query;
        if (_cachedBuildingQuery != cacheKey || _cachedBuildingMatches is null)
        {
            _cachedBuildingMatches = Catalog.Buildings.Values
                .Where(b => !Config.HideEmptyRecipeBuildings.Value || b.Recipes.Count > 0)
                .Where(b => query.Length == 0
                         || b.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                         || b.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                         // Tag chips reuse the same search field, so match
                         // against tag names too — a one-click filter.
                         || b.Tags.Any(t => t.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                // Group by Kind so the Building list mirrors the Good tab's
                // category-grouped rendering. Kind is the natural high-level
                // bucket (Workshop/Service/Cornerstone/etc).
                .OrderBy(b => b.Kind.ToString(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            // Fuzzy fallback: when the literal substring filter yields zero
            // matches, retry with a subsequence match so e.g. "blkpr" hits
            // "Bakery Press". Only kicks in when there's a real query.
            if (_cachedBuildingMatches.Count == 0 && query.Length > 1)
            {
                _cachedBuildingMatches = Catalog.Buildings.Values
                    .Where(b => !Config.HideEmptyRecipeBuildings.Value || b.Recipes.Count > 0)
                    .Where(b => SubsequenceMatch(b.DisplayName, query) ||
                                SubsequenceMatch(b.Name, query))
                    .OrderBy(b => b.Kind.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            _cachedBuildingQuery = cacheKey;
        }

        // Pre-compute Kind→count for the section headers.
        var kindCounts = _cachedBuildingMatches
            .GroupBy(b => b.Kind.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        string? lastKind = null;
        foreach (var b in _cachedBuildingMatches)
        {
            var kind = b.Kind.ToString();
            if (kind != lastKind)
            {
                GUILayout.Space(4);
                var count = kindCounts.TryGetValue(kind, out var n) ? n : 0;
                GUILayout.Label($"{kind} · {count}", _mutedStyle);
                lastKind = kind;
            }
            var label = HighlightMatch(b.DisplayName, query);
            var style = (_selectedBuilding == b.Name)
                ? _tabActiveStyle
                : (_listButtonStyle ?? _tabStyle);
            if (GUILayout.Button(label, style)) _selectedBuilding = b.Name;
        }
        GUILayout.EndScrollView();
    }

    /// <summary>
    /// Wraps the first case-insensitive occurrence of <paramref name="query"/>
    /// in <paramref name="text"/> with rich-text bold tags. Used by the
    /// Building/Good list rows so search matches stand out at a glance.
    /// Requires the consuming style to have <c>richText = true</c>; we set
    /// that on the list button styles in <see cref="EnsureStyles"/>.
    /// </summary>
    private static string HighlightMatch(string text, string? query)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text)) return text ?? "";
        var q = query!;
        var idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return text;
        return text.Substring(0, idx) +
               "<b>" + text.Substring(idx, q.Length) + "</b>" +
               text.Substring(idx + q.Length);
    }

    private void DrawBuildingDetail()
    {
        GUILayout.BeginVertical();

        if (string.IsNullOrEmpty(_selectedBuilding))
        {
            GUILayout.Label("Select a building from the list.", _bodyStyle);
            GUILayout.Label("Live worker / stockpile joins arrive once selection patches are wired. For now the score is base goods/min from the static catalog.", _mutedStyle);
            GUILayout.EndVertical();
            return;
        }

        Func<string, int>? stockLookup = LiveGameState.IsReady ? LiveGameState.StockpileOf : null;
        Func<string, WorkerStatus?>? workersLookup = LiveGameState.IsReady
            ? name =>
              {
                  var ws = LiveGameState.WorkersFor(name);
                  return ws is null ? null : new WorkerStatus(ws.Assigned, ws.Capacity, ws.Idle);
              }
            : null;
        // Net-flow lookup so the recipe card can flag draining inputs (input
        // good with negative net flow across the settlement).
        Func<string, double>? flowLookup = LiveGameState.IsReady
            ? name => LiveGameState.FlowFor(name, Catalog).Net
            : null;
        var vm = BuildingProvider.For(Catalog, _selectedBuilding!, stockLookup, workersLookup, flowLookup);
        GUILayout.Label(vm.Building.DisplayName, _h1Style);
        var meta = $"{vm.Building.Kind} · {vm.Building.Profession}";
        GUILayout.Label(meta, _mutedStyle);
        DrawBuildingCyclesHeader(_selectedBuilding!);

        // Recipe input chip filter — union of all input-good display names
        // for this building's recipes; click to scope the recipe list.
        var inputs = vm.Recipes
            .SelectMany(r => r.Recipe.RequiredGoods.SelectMany(s => s.Options.Select(o => o.Good)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (inputs.Count > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   uses:", _mutedStyle, GUILayout.Width(46));
            var prevColor = GUI.color;
            foreach (var good in inputs)
            {
                var disp = Catalog.Goods.TryGetValue(good, out var gi) ? gi.DisplayName : good;
                // Tint chip by net flow: red < 0, green > 0, neutral otherwise.
                if (LiveGameState.IsReady)
                {
                    var net = LiveGameState.FlowFor(good, Catalog).Net;
                    if      (net < -1e-6) GUI.color = new Color(1.0f, 0.65f, 0.65f, 1f);
                    else if (net >  1e-6) GUI.color = new Color(0.70f, 1.0f, 0.75f, 1f);
                    else                  GUI.color = prevColor;
                }
                var label = string.Equals(_recipeInputFilter, good, StringComparison.OrdinalIgnoreCase)
                    ? $"✓ {disp}"
                    : disp;
                if (GUILayout.Button(new GUIContent(label, $"Filter recipes consuming '{disp}'"),
                                     _tabStyle))
                {
                    _recipeInputFilter = string.Equals(_recipeInputFilter, good,
                                                       StringComparison.OrdinalIgnoreCase)
                        ? null  // clicking the active chip clears the filter
                        : good;
                }
            }
            GUI.color = prevColor;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // Building tag chips — click to drop the tag into the search box and
        // re-filter the list. Pill-styled to read as labels rather than buttons.
        if (vm.Building.Tags.Count > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   tags:", _mutedStyle, GUILayout.Width(46));
            foreach (var tag in vm.Building.Tags)
            {
                if (GUILayout.Button(new GUIContent(tag, $"Filter list by tag '{tag}'"),
                                     _chipStyle ?? _tabStyle))
                {
                    _buildingSearch = tag;
                    _cachedBuildingQuery = null; // force refilter
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(vm.IsLive ? "● live settlement data" : "○ static catalog only", _mutedStyle);
        if (vm.Workers is { } w)
        {
            var idleTag = w.Idle ? "  ⚠ idle" : "";
            GUILayout.Label($"Workers: {w.Assigned}/{w.Capacity}{idleTag}", _bodyStyle);
        }
        if (!string.IsNullOrEmpty(vm.Note)) { GUILayout.Label(vm.Note!, _mutedStyle); }

        DrawRaceFits(vm.RaceFits);

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{vm.Recipes.Count} recipes — sorted by throughput", _bodyStyle);
        GUILayout.FlexibleSpace();
        DrawWhyAllButton(_expandedRecipes, vm.Recipes.Select(r => r.Recipe.Name), Config.WhyAllRecipes);
        GUILayout.EndHorizontal();

        // Recipe sort mode selector.
        GUILayout.BeginHorizontal();
        GUILayout.Label("   sort:", _mutedStyle, GUILayout.Width(46));
        DrawSortModeChip(RecipeSort.Throughput,    "throughput");
        DrawSortModeChip(RecipeSort.Profitability, "profit");
        DrawSortModeChip(RecipeSort.Availability,  "availability");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        _buildingDetailScroll = GUILayout.BeginScrollView(_buildingDetailScroll, GUILayout.ExpandHeight(true));
        var visibleRecipes = string.IsNullOrEmpty(_recipeInputFilter)
            ? vm.Recipes.AsEnumerable()
            : vm.Recipes.Where(r => r.Recipe.RequiredGoods.Any(s =>
                s.Options.Any(o =>
                    string.Equals(o.Good, _recipeInputFilter, StringComparison.OrdinalIgnoreCase))));
        visibleRecipes = _recipeSort switch
        {
            RecipeSort.Profitability => visibleRecipes.OrderByDescending(r => RecipeProfit(r.Recipe)),
            RecipeSort.Availability  => visibleRecipes.OrderByDescending(r => MinInputStock(r.Recipe)),
            _                        => visibleRecipes,    // already sorted by throughput
        };
        foreach (var rk in visibleRecipes) DrawRecipeCard(rk);
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
    }

    /// <summary>Renders a single sort-mode chip, marking the active one.</summary>
    private void DrawSortModeChip(RecipeSort mode, string label)
    {
        var marker = _recipeSort == mode ? $"✓ {label}" : label;
        if (GUILayout.Button(marker, _chipStyle ?? _tabStyle))
            _recipeSort = mode;
    }

    /// <summary>
    /// Cycle profit (in catalog trade-value units): output trade value minus
    /// the cheapest input combination across each slot. Negative = loss-maker.
    /// </summary>
    private double RecipeProfit(RecipeInfo r)
    {
        double output = 0;
        if (Catalog.Goods.TryGetValue(r.ProducedGood, out var og))
            output = og.TradingBuyValue * r.ProducedAmount;
        double inputs = 0;
        foreach (var slot in r.RequiredGoods)
        {
            // Take the cheapest option in each slot — the worker's choice is
            // free at the catalog level so we assume the optimal one.
            double slotMin = double.PositiveInfinity;
            foreach (var opt in slot.Options)
            {
                if (!Catalog.Goods.TryGetValue(opt.Good, out var gi)) continue;
                slotMin = Math.Min(slotMin, gi.TradingBuyValue * opt.Amount);
            }
            if (!double.IsPositiveInfinity(slotMin)) inputs += slotMin;
        }
        return output - inputs;
    }

    /// <summary>
    /// Worst-case input stockpile across the recipe's required goods. Used to
    /// rank recipes whose ingredients you currently have plenty of.
    /// </summary>
    private int MinInputStock(RecipeInfo r)
    {
        if (r.RequiredGoods.Count == 0) return int.MaxValue;
        var min = int.MaxValue;
        foreach (var slot in r.RequiredGoods)
        {
            // Best stockpile within a slot wins (worker will pick that one).
            var best = 0;
            foreach (var opt in slot.Options)
                best = Math.Max(best, LiveGameState.IsReady
                    ? LiveGameState.StockpileOf(opt.Good) : 0);
            min = Math.Min(min, best);
        }
        return min;
    }

    /// <summary>
    /// Renders a one-tap toggle that expands every "why" row in <paramref name="set"/>
    /// when collapsed, or clears them when any are expanded. Used to flip
    /// reasoning rows on the Building/Good tabs without per-row clicks. The
    /// optional <paramref name="persisted"/> entry stores the on/off state
    /// across sessions.
    /// </summary>
    private void DrawWhyAllButton(
        HashSet<string> set,
        IEnumerable<string> keys,
        BepInEx.Configuration.ConfigEntry<bool>? persisted = null)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0) return;
        var anyExpanded = keyList.Any(k => set.Contains(k))
                       || (persisted?.Value ?? false);
        var label = anyExpanded ? "▾ why × all" : "▸ why × all";
        if (GUILayout.Button(new GUIContent(label, anyExpanded ? "Collapse all reasoning" : "Expand all reasoning"),
                             _tabStyle, GUILayout.Width(90)))
        {
            if (anyExpanded) set.Clear();
            else foreach (var k in keyList) set.Add(k);
            if (persisted is not null) persisted.Value = !anyExpanded;
        }
    }

    private void DrawRaceFits(IReadOnlyList<RaceFit>? fits)
    {
        if (fits is null || fits.Count == 0) return;
        GUILayout.Space(2);
        GUILayout.Label("Best workers — ranked by perk weight", _bodyStyle);
        foreach (var f in fits)
        {
            GUILayout.BeginHorizontal();
            var marker = f.IsTopRanked && Config.ShowRecommendations.Value ? "★" : $"#{f.Rank}";
            GUILayout.Label(marker, _badgeStyle, GUILayout.Width(24));
            GUILayout.BeginVertical();
            GUILayout.Label(f.DisplayName, _bodyStyle);
            GUILayout.Label($"   tag {f.MatchingTag} → {f.Effect}  (weight {f.Weight})", _mutedStyle);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(1);
        }
    }

    private void DrawRecipeCard(RecipeRanking rk)
    {
        GUILayout.BeginHorizontal();

        var marker = rk.IsTopRanked && Config.ShowRecommendations.Value ? "★" : $"#{rk.Rank}";
        GUILayout.Label(marker, _badgeStyle, GUILayout.Width(24));

        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        // Highlight the building search query inside the recipe display name
        // when set; _bodyStyle has richText enabled so the <b> wrap renders.
        GUILayout.Label(
            HighlightMatch(rk.Recipe.DisplayName, _buildingSearch?.Trim() ?? ""),
            _bodyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(rk.Throughput.Format("0.##"), _bodyStyle);
        GUILayout.EndHorizontal();

        // Profitability chip per cycle. Positive = profitable in trade-value
        // units, negative = loss-maker. Skip for recipes with no measurable
        // value on either side.
        var profit = RecipeProfit(rk.Recipe);
        if (Math.Abs(profit) > 1e-6)
        {
            var sign = profit >= 0 ? "+" : "";
            var pStyle = profit >= 0 ? (_okStyle ?? _mutedStyle)
                       : (_critStyle ?? _mutedStyle);
            GUILayout.Label($"   profit: {sign}{profit:0.##} /cycle", pStyle);
        }

        GUILayout.Label(FormatRecipeInputs(rk.Recipe), _mutedStyle);

        if (rk.Inputs.Count > 0)
        {
            foreach (var ia in rk.Inputs)
            {
                var prefix = ia.AtRisk ? "⚠ " : "   ";
                GUILayout.Label(
                    $"{prefix}{ia.Good}: {ia.InStock} in stock (need {ia.Required}/cycle)",
                    _mutedStyle);
            }
        }

        var key = rk.Recipe.Name;
        var expanded = _expandedRecipes.Contains(key);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(expanded ? "▾ why" : "▸ why", _tabStyle, GUILayout.Width(70)))
        {
            if (expanded) _expandedRecipes.Remove(key); else _expandedRecipes.Add(key);
        }
        // Pin / unpin the (selected building, this recipe) pair to Home.
        var pinKey = (_selectedBuilding ?? "", rk.Recipe.Name);
        var pinnedRecipe = _selectedBuilding != null && _pinned.Contains(pinKey);
        if (_selectedBuilding != null &&
            GUILayout.Button(
                new GUIContent(pinnedRecipe ? "★ pinned" : "☆ pin",
                               pinnedRecipe ? "Unpin from Home" : "Pin to Home"),
                _tabStyle, GUILayout.Width(80)))
        {
            if (pinnedRecipe) _pinned.Remove(pinKey); else _pinned.Add(pinKey);
            SavePins();
        }
        // UI-only "stopped" / "priority" markers, persisted in config and
        // surfaced on the Home pin row so the player can use them as a
        // personal queue regardless of in-game recipe state.
        DrawMarkerToggle(rk.Recipe.Name, _markedStopped,
            "\u26d4 stop", "Mark this recipe as stopped (UI marker)",
            Config.MarkedStoppedRecipes);
        DrawMarkerToggle(rk.Recipe.Name, _markedPriority,
            "\u2605 pri", "Mark this recipe as haul priority (UI marker)",
            Config.MarkedPriorityRecipes);
        GUILayout.EndHorizontal();
        if (expanded)
        {
            foreach (var c in rk.Throughput.Components)
            {
                var line = $"   {c.Label}: {c.Value:0.##}";
                if (!string.IsNullOrEmpty(c.Note)) line += $"   {c.Note}";
                GUILayout.Label(line, _mutedStyle);
            }
        }
        // Alternative producers: top-2 other recipes that produce the same
        // good (not this one), ranked by base throughput. Surfaces the
        // "what else makes this?" question without a tab switch.
        DrawAlternativeProducers(rk.Recipe);
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    /// <summary>
    /// One-tap toggle for a UI-only recipe marker (stopped or priority).
    /// Mutates <paramref name="set"/> in memory and writes the comma-separated
    /// list back to <paramref name="persisted"/>. Style: ✓ prefix when active.
    /// </summary>
    private void DrawMarkerToggle(string recipeName, HashSet<string> set,
        string label, string tooltip,
        BepInEx.Configuration.ConfigEntry<string> persisted)
    {
        var on = set.Contains(recipeName);
        var displayLabel = on ? "\u2713 " + label : label;
        if (GUILayout.Button(new GUIContent(displayLabel, tooltip),
                             _tabStyle, GUILayout.Width(70)))
        {
            if (on) set.Remove(recipeName); else set.Add(recipeName);
            persisted.Value = string.Join(",", set);
        }
    }

    /// <summary>
    /// Lists top-2 catalog recipes (other than <paramref name="current"/>)
    /// that produce the same good, with their base throughput. Skipped when
    /// nothing else makes that good.
    /// </summary>
    private void DrawAlternativeProducers(RecipeInfo current)
    {
        if (string.IsNullOrEmpty(current.ProducedGood)) return;
        var alts = Catalog.Recipes.Values
            .Where(rr => rr.Name != current.Name &&
                         string.Equals(rr.ProducedGood, current.ProducedGood,
                             StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rr => rr.ProductionTime > 0
                ? (60.0 * rr.ProducedAmount) / rr.ProductionTime : 0)
            .Take(2)
            .ToList();
        if (alts.Count == 0) return;
        var summary = string.Join(", ", alts.Select(rr =>
        {
            var pm = rr.ProductionTime > 0
                ? (60.0 * rr.ProducedAmount) / rr.ProductionTime : 0;
            return $"{rr.DisplayName} ({pm:0.##}/min)";
        }));
        GUILayout.Label("   also produced by: " + summary, _mutedStyle);
    }

    private static string FormatRecipeInputs(RecipeInfo r)
    {
        if (r.RequiredGoods.Count == 0) return "no inputs · cycle " + r.ProductionTime + "s";
        var slots = r.RequiredGoods.Select(slot =>
            string.Join(" OR ", slot.Options.Select(o => $"{o.Good} ×{o.Amount}")));
        return string.Join(" + ", slots) + $" · cycle {r.ProductionTime:0.##}s";
    }

    private void DrawGoodTab()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search:", GUILayout.Width(56));
        _goodSearch = GUILayout.TextField(_goodSearch ?? "", GUILayout.Width(160));
        if (DrawClearButton(_selectedGood)) { _selectedGood = null; _flowExpanded = false; }
        GUILayout.FlexibleSpace();
        GUILayout.Label($"{Catalog.Goods.Count} known", _mutedStyle);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawGoodList();
        GUILayout.Space(8);
        DrawGoodDetail();
        GUILayout.EndHorizontal();
    }

    private void DrawGoodList()
    {
        _goodListScroll = GUILayout.BeginScrollView(_goodListScroll,
            GUILayout.Width(180), GUILayout.ExpandHeight(true));
        var query = _goodSearch?.Trim() ?? "";
        if (_cachedGoodQuery != query || _cachedGoodMatches is null)
        {
            _cachedGoodMatches = Catalog.Goods.Values
                .Where(g => query.Length == 0
                         || g.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                         || g.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                         || g.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _cachedGoodQuery = query;
        }

        string? lastCategory = null;
        foreach (var g in _cachedGoodMatches)
        {
            if (g.Category != lastCategory)
            {
                GUILayout.Space(4);
                GUILayout.Label(string.IsNullOrEmpty(g.Category) ? "(uncategorized)" : g.Category, _mutedStyle);
                lastCategory = g.Category;
            }
            var style = (_selectedGood == g.Name)
                ? _tabActiveStyle
                : (_listButtonStyle ?? _tabStyle);
            // Right-click on a row copies its model name to the clipboard
            // (mod-authoring aid). Layout is handled by the button; we sniff
            // mouse events against its rect after the fact.
            if (GUILayout.Button(HighlightMatch(g.DisplayName, query), style))
                _selectedGood = g.Name;
            var btnRect = GUILayoutUtility.GetLastRect();
            var ev = Event.current;
            if (ev != null &&
                ev.type == EventType.MouseDown && ev.button == 1 &&
                btnRect.Contains(ev.mousePosition))
            {
                try { GUIUtility.systemCopyBuffer = g.Name; } catch { }
                ev.Use();
            }
        }
        GUILayout.EndScrollView();
    }

    /// <summary>
    /// Subsequence (not substring) match: returns true when every char in
    /// <paramref name="needle"/> appears in <paramref name="haystack"/> in
    /// order, case-insensitively. Used by the Building search fuzzy fallback.
    /// </summary>
    private static bool SubsequenceMatch(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return false;
        var i = 0;
        foreach (var ch in haystack)
        {
            if (i >= needle.Length) return true;
            if (char.ToLowerInvariant(ch) == char.ToLowerInvariant(needle[i])) i++;
        }
        return i >= needle.Length;
    }

    private void DrawGoodDetail()
    {
        GUILayout.BeginVertical();
        if (string.IsNullOrEmpty(_selectedGood))
        {
            GUILayout.Label("Select a good from the list.", _bodyStyle);
            GUILayout.Label("Production paths are ranked by cost-per-output (using base trade values as a common unit). Live trader rotation · net production · runway arrive once selection patches are wired.", _mutedStyle);
            GUILayout.EndVertical();
            return;
        }

        Func<GoodProvider.TraderLiveQuery?>? curT = LiveGameState.IsReady ? () => ToQuery(LiveGameState.CurrentTrader()) : null;
        Func<GoodProvider.TraderLiveQuery?>? nxtT = LiveGameState.IsReady ? () => ToQuery(LiveGameState.NextTrader())    : null;
        Func<string, GoodFlowSnapshot?>? flowLookup = LiveGameState.IsReady
            ? name =>
              {
                  var f = LiveGameState.FlowFor(name, Catalog);
                  var stock = LiveGameState.StockpileOf(name);
                  double? runway = (f.Net < 0 && stock > 0) ? (stock / -f.Net) * 60.0 : (double?)null;
                  var rows = f.Contributions
                      .Select(c => new FlowRow(c.BuildingName, c.RecipeName, c.PerMin, c.IsProducer))
                      .ToList();
                  return new GoodFlowSnapshot(f.ProducedPerMin, f.ConsumedPerMin, stock, runway, rows);
              }
            : null;
        var vm = GoodProvider.For(Catalog, _selectedGood!, curT, nxtT, flowLookup);
        GUILayout.Label(vm.Good.DisplayName, _h1Style);
        var meta = vm.Good.Category;
        if (vm.Good.IsEatable)   meta += $" · eatable (fullness {vm.Good.EatingFullness:0.##})";
        if (vm.Good.CanBeBurned) meta += $" · fuel ({vm.Good.BurningTime:0.##}s)";
        GUILayout.Label(meta, _mutedStyle);
        GUILayout.Label($"Trade value: sells for {vm.Good.TradingBuyValue:0.##} · buys at {vm.Good.TradingSellValue:0.##}", _mutedStyle);
        if (!string.IsNullOrEmpty(vm.Note)) GUILayout.Label(vm.Note!, _mutedStyle);

        // Price history sparkline for the selected good (current trader visit).
        DrawPriceHistory(_selectedGood!);

        if (vm.Flow is { } f)
        {
            var net = f.Net;
            var arrow = net > 1e-6 ? "↑" : (net < -1e-6 ? "↓" : "≡");
            var line = $"● flow: +{f.ProducedPerMin:0.##}/min · -{f.ConsumedPerMin:0.##}/min · net {arrow} {net:+0.##;-0.##;0}/min · {f.Stockpile} in stock";
            GUILayout.Label(line, _bodyStyle);
            if (f.RunwaySeconds is double rs)
            {
                var minutes = rs / 60.0;
                var prefix = minutes < 1 ? "⚠ " : "";
                GUILayout.Label($"{prefix}runway at current burn: {minutes:0.##} min", _mutedStyle);
            }
            if (f.Contributions.Count > 0 &&
                GUILayout.Button(_flowExpanded ? "▾ flow breakdown" : "▸ flow breakdown",
                                 _tabStyle, GUILayout.Width(160)))
            {
                _flowExpanded = !_flowExpanded;
            }
            if (_flowExpanded)
            {
                foreach (var c in f.Contributions)
                {
                    var sign = c.IsProducer ? "+" : "-";
                    GUILayout.Label(
                        $"   {sign}{c.PerMin:0.##}/min  — {c.BuildingName} · {c.RecipeName}",
                        _mutedStyle);
                }
            }
        }

        _goodDetailScroll = GUILayout.BeginScrollView(_goodDetailScroll, GUILayout.ExpandHeight(true));

        // Producers (cheapest first)
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{vm.Producers.Count} production paths — sorted by cost", _bodyStyle);
        GUILayout.FlexibleSpace();
        DrawWhyAllButton(_expandedProducers, vm.Producers.Select(p => "prod:" + p.Recipe.Name), Config.WhyAllProducers);
        GUILayout.EndHorizontal();

        // Filter chips. None active = show everything.
        GUILayout.BeginHorizontal();
        GUILayout.Label("   filters:", _mutedStyle, GUILayout.Width(64));
        _filterFuelOnly     = GUILayout.Toggle(_filterFuelOnly,     " fuel");
        _filterEatableOnly  = GUILayout.Toggle(_filterEatableOnly,  " eatable");
        _filterDrainingOnly = GUILayout.Toggle(_filterDrainingOnly, " draining");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        var filtered = vm.Producers.Where(p =>
        {
            if (!_filterFuelOnly && !_filterEatableOnly && !_filterDrainingOnly) return true;
            var output = p.Recipe.ProducedGood;
            var info = Catalog.Goods.TryGetValue(output, out var gi) ? gi : null;
            if (_filterFuelOnly    && info?.CanBeBurned != true) return false;
            if (_filterEatableOnly && info?.IsEatable   != true) return false;
            if (_filterDrainingOnly)
            {
                if (!LiveGameState.IsReady) return false;
                if (LiveGameState.FlowFor(output, Catalog).Net >= -1e-6) return false;
            }
            return true;
        });
        foreach (var p in filtered) DrawProductionPath(p);

        // Consumers — annotate each catalog row with the live consumption
        // share if we can compute it from the live flow's contributions.
        GUILayout.Space(6);
        GUILayout.Label($"Consumed by {vm.Consumers.Count} recipes", _bodyStyle);
        var liveConsumers = (vm.Flow?.Contributions ?? Array.Empty<FlowRow>())
            .Where(c => !c.IsProducer).ToList();
        var totalConsumed = liveConsumers.Sum(c => c.PerMin);
        foreach (var r in vm.Consumers.Take(20))
        {
            // Live row matches by recipe display name.
            var live = liveConsumers.FirstOrDefault(c =>
                string.Equals(c.RecipeName, r.DisplayName, StringComparison.OrdinalIgnoreCase));
            var pct = (totalConsumed > 1e-6 && live is not null)
                ? $"  · {(live.PerMin * 100.0 / totalConsumed):0.#}% of consumption"
                : "";
            GUILayout.Label(
                $"   {r.DisplayName}  ({r.ProducedGood} ×{r.ProducedAmount}){pct}",
                _mutedStyle);
        }

        // Race needs
        if (vm.NeededBy.Count > 0)
        {
            GUILayout.Space(6);
            GUILayout.Label("A racial need for: " + string.Join(", ", vm.NeededBy.Select(r => r.DisplayName)), _bodyStyle);
        }

        // Live trader rotation
        if (vm.CurrentTrader is not null) DrawTraderLine("Current", vm.CurrentTrader);
        if (vm.NextTrader    is not null) DrawTraderLine("Next",    vm.NextTrader);

        // Traders (catalog)
        GUILayout.Space(6);
        if (vm.Good.TradersBuying.Count > 0)
            GUILayout.Label("Buyers (catalog): "  + string.Join(", ", vm.Good.TradersBuying), _mutedStyle);
        if (vm.Good.TradersSelling.Count > 0)
            GUILayout.Label("Sellers (catalog): " + string.Join(", ", vm.Good.TradersSelling), _mutedStyle);

        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private static GoodProvider.TraderLiveQuery? ToQuery(LiveGameState.TraderInfo? info) =>
        info is null ? null : new GoodProvider.TraderLiveQuery
        {
            DisplayName = info.DisplayName,
            IsInVillage = info.IsInVillage,
            Buys        = info.Buys,
            Sells       = info.Sells,
        };

    private void DrawTraderLine(string label, TraderSnapshot t)
    {
        var here = t.IsInVillage ? " (in village)" : " (en route)";
        var disp = string.IsNullOrEmpty(t.DisplayName) ? "(unnamed)" : t.DisplayName;
        GUILayout.Space(4);
        GUILayout.Label($"● {label} trader: {disp}{here}", _bodyStyle);
        var marks = new List<string>();
        if (t.BuysThisGood)  marks.Add("★ buys this good");
        if (t.SellsThisGood) marks.Add("sells this good");
        if (!t.BuysThisGood && !t.SellsThisGood) marks.Add("does not trade this good");
        GUILayout.Label("   " + string.Join(" · ", marks), _mutedStyle);

        // Live currency multipliers — only meaningful for the CURRENT trader
        // (Trade prices are computed against the active trade context).
        if (label == "Current" && _selectedGood is not null && t.IsInVillage)
        {
            var sell = LiveGameState.SellValueAtCurrentTrader(_selectedGood);
            var buy  = LiveGameState.BuyValueAtCurrentTrader(_selectedGood);
            if (sell > 0 || buy > 0)
                GUILayout.Label(
                    $"   live price: sells for {sell:0.##} · buys at {buy:0.##}",
                    _mutedStyle);
            // Affordability: total currency from selling all top-3 desires,
            // and how many units of the selected good you could buy with it.
            var topDesires = _currentDesiresCache?.Get();
            if (topDesires is { Count: > 0 } && buy > 0)
            {
                var pot = topDesires.Take(3).Sum(d => d.TotalValue);
                if (pot > 0)
                {
                    var affordable = (int)Math.Floor(pot / buy);
                    if (affordable > 0)
                        GUILayout.Label(
                            $"   affordability: ~{pot:0.##} currency → {affordable} × {_selectedGood} @ {buy:0.##}/u",
                            _mutedStyle);
                    // Per-desire breakdown: show how many units of the
                    // selected good each top-3 sell could afford on its own.
                    foreach (var d in topDesires.Take(3))
                    {
                        var units = (int)Math.Floor(d.TotalValue / buy);
                        if (units <= 0) continue;
                        GUILayout.Label(
                            $"      {d.DisplayName} ({d.TotalValue:0.##}c) → {units} × {_selectedGood}",
                            _mutedStyle);
                    }
                }
            }
        }

        // Travel progress for the current (en-route) trader.
        if (label == "Current" && !t.IsInVillage)
        {
            var prog = LiveGameState.CurrentTraderTravelProgress();
            if (prog is float p)
            {
                GUILayout.Label($"   travel: {p * 100f:0}% en route", _mutedStyle);
                DrawTraderTimeline(p);
            }
        }

        // Trader desires ranking (top picks by total settlement value).
        // Reuse the TTL cache so all callers in this frame share one scan.
        var desires = (label == "Current"
            ? _currentDesiresCache?.Get()
            : _nextDesiresCache?.Get())
            ?? LiveGameState.RankTraderDesires(
                label == "Current" ? LiveGameState.CurrentTrader() : LiveGameState.NextTrader(),
                Catalog, isCurrent: label == "Current");
        DrawTraderDesires(desires, max: 5);
    }

    /// <summary>
    /// Renders a compact "sell this first" panel for a trader. Top entries get
    /// a ★ marker when recommendations are on; everyone else shows their rank.
    /// </summary>
    private void DrawTraderDesires(
        IReadOnlyList<LiveGameState.TraderDesire> desires, int max)
    {
        if (desires.Count == 0) return;
        var sumTopN = desires.Take(max).Sum(d => d.TotalValue);
        GUILayout.Label(
            $"   wants — ranked by total value (price × stockpile) · top {Math.Min(max, desires.Count)} = {sumTopN:0.##}",
            _mutedStyle);
        for (var i = 0; i < desires.Count && i < max; i++)
        {
            var d = desires[i];
            var rank = i + 1;
            var marker = rank == 1 && Config.ShowRecommendations.Value ? "★" : $"#{rank}";
            var line = $"   {marker} {d.DisplayName}: {d.PricePerUnit:0.##}/u × {d.Stockpile} = {d.TotalValue:0.##}";
            if (GUILayout.Button(new GUIContent(line, "Open this good"), _tabStyle))
            {
                _selectedGood = d.Good;
                _activeTab    = Tab.Good;
            }
        }
        if (desires.Count > max)
            GUILayout.Label($"   … and {desires.Count - max} more", _mutedStyle);
    }

    /// <summary>
    /// Best-effort ETA hint for "produce N goodX" objectives. Looks for a
    /// catalog good display name as a substring of the objective description
    /// and joins it with the live net flow. Skips if no good is referenced or
    /// if the settlement isn't producing the good fast enough to estimate.
    /// </summary>
    private void DrawObjectiveEta(LiveGameState.OrderObjective ob)
    {
        if (ob.Completed) return;
        if (string.IsNullOrEmpty(ob.Description) || !LiveGameState.IsReady) return;
        var match = ProgressRegex.Match(ob.Description);
        if (!match.Success) return;
        if (!int.TryParse(match.Groups[1].Value, out var have)) return;
        if (!int.TryParse(match.Groups[2].Value, out var need)) return;
        if (need <= have) return;

        // Find the first catalog good display name that occurs in the desc.
        // Order by length descending so "Pickled Goods" beats "Goods".
        StormGuide.Domain.GoodInfo? matched = null;
        foreach (var gi in Catalog.Goods.Values
                     .OrderByDescending(g => g.DisplayName.Length))
        {
            if (string.IsNullOrEmpty(gi.DisplayName)) continue;
            if (ob.Description.IndexOf(gi.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matched = gi;
                break;
            }
        }
        if (matched is null) return;

        var net = LiveGameState.FlowFor(matched.Name, Catalog).Net;
        if (net <= 1e-6) return;   // not producing or breaking even
        var minutes = (need - have) / net;
        if (minutes <= 0 || minutes > 999) return;
        GUILayout.Label($"      ETA at current burn: ~{minutes:0.#} min", _mutedStyle);
    }

    /// <summary>
    /// Renders a thin progress bar for an objective when its description
    /// contains an "X / Y" pattern. Uses the regex against the (possibly rich-
    /// text) description because <see cref="LiveGameState.OrderObjective"/>
    /// only carries the current amount, not the required count.
    /// </summary>
    private void DrawObjectiveProgress(LiveGameState.OrderObjective ob)
    {
        if (ob.Completed) return;
        if (string.IsNullOrEmpty(ob.Description)) return;
        var match = ProgressRegex.Match(ob.Description);
        if (!match.Success) return;
        if (!int.TryParse(match.Groups[1].Value, out var have)) return;
        if (!int.TryParse(match.Groups[2].Value, out var need)) return;
        if (need <= 0) return;

        var t = Mathf.Clamp01((float)have / need);
        var rect = GUILayoutUtility.GetRect(0, 6, GUILayout.ExpandWidth(true));
        // Indent visually under the objective text.
        rect.xMin += 28;
        GUI.Box(rect, GUIContent.none);
        var fill = new Rect(rect.x, rect.y, rect.width * t, rect.height);
        GUI.DrawTexture(fill, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
            new Color(0.45f, 0.85f, 0.55f, 0.7f), 0, 0);
    }

    /// <summary>
    /// Tiny progress bar visualising the current trader's travel %.
    /// Indented under the trader line so it visually associates with it.
    /// </summary>
    private void DrawTraderTimeline(float progress)
    {
        var rect = GUILayoutUtility.GetRect(0, 6, GUILayout.ExpandWidth(true));
        rect.xMin += 28;
        GUI.Box(rect, GUIContent.none);
        var fill = new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(progress), rect.height);
        GUI.DrawTexture(fill, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
            new Color(0.55f, 0.75f, 0.95f, 0.7f), 0, 0);   // sky blue
    }

    /// <summary>
    /// Tracks per-good sell-price samples for the duration of the current
    /// trader's visit and renders a small line chart in the Good detail.
    /// </summary>
    private void DrawPriceHistory(string good)
    {
        if (!LiveGameState.IsReady) return;
        var cur = LiveGameState.CurrentTrader();
        if (cur is null || !cur.IsInVillage) return;
        var visitKey = cur.DisplayName ?? "";
        if (_priceVisitKey != visitKey) { _priceVisitKey = visitKey; _priceSamples.Clear(); }

        // Sample once a second.
        var now = LiveGameState.GameTimeNow() ?? Time.unscaledTime;
        if (now - _lastPriceSampleTime >= 1f)
        {
            _lastPriceSampleTime = now;
            if (!_priceSamples.TryGetValue(good, out var list))
                _priceSamples[good] = list = new List<float>(120);
            var p = LiveGameState.SellValueAtCurrentTrader(good);
            list.Add(p);
            if (list.Count > 120) list.RemoveRange(0, list.Count - 120);
        }

        if (!_priceSamples.TryGetValue(good, out var samples) || samples.Count < 2) return;
        GUILayout.Label("   price history (this visit):", _mutedStyle);
        var rect = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
        rect.xMin += 16;
        GUI.Box(rect, GUIContent.none);
        var min = samples.Min();
        var max = Math.Max(min + 0.001f, samples.Max());
        for (var i = 1; i < samples.Count; i++)
        {
            var x0 = rect.x + rect.width * (i - 1) / (float)(samples.Count - 1);
            var x1 = rect.x + rect.width *  i      / (float)(samples.Count - 1);
            var y0 = rect.y + rect.height * (1f - (samples[i - 1] - min) / (max - min));
            var y1 = rect.y + rect.height * (1f - (samples[i]     - min) / (max - min));
            // Draw a 1-pixel "line" by rasterising a tiny colored quad along
            // the segment — OnGUI lacks a primitive line draw without GL setup.
            DrawSegment(x0, y0, x1, y1, new Color(0.55f, 0.85f, 1f, 0.85f));
        }
    }

    private static void DrawSegment(float x0, float y0, float x1, float y1, Color c)
    {
        var dx = x1 - x0; var dy = y1 - y0;
        var len = Mathf.Max(1f, Mathf.Sqrt(dx * dx + dy * dy));
        var steps = (int)len;
        for (var s = 0; s <= steps; s++)
        {
            var px = x0 + dx * s / len;
            var py = y0 + dy * s / len;
            GUI.DrawTexture(new Rect(px, py, 1f, 1f),
                Texture2D.whiteTexture, ScaleMode.StretchToFill,
                false, 0, c, 0, 0);
        }
    }

    /// <summary>Coloured pill style by order tier name.</summary>
    private GUIStyle TierBadgeStyle(string tier)
    {
        var s = new GUIStyle(_mutedStyle!) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        var t = tier.ToLowerInvariant();
        // Cheap hash to colour: "bronze"=amber, "silver"=light, "gold"=yellow,
        // anything else falls through as muted.
        if (t.Contains("gold"))        s.normal.textColor = new Color(1.00f, 0.85f, 0.20f, 1f);
        else if (t.Contains("silver")) s.normal.textColor = new Color(0.85f, 0.88f, 0.95f, 1f);
        else if (t.Contains("bronze")) s.normal.textColor = new Color(0.85f, 0.55f, 0.30f, 1f);
        return s;
    }

    private void DrawProductionPath(ProductionPath p)
    {
        GUILayout.BeginHorizontal();

        var marker = p.IsCheapest && Config.ShowRecommendations.Value ? "★" : $"#{p.Rank}";
        GUILayout.Label(marker, _badgeStyle, GUILayout.Width(24));

        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label(p.Recipe.DisplayName, _bodyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(p.Cost.Format("0.##"), _bodyStyle);
        GUILayout.EndHorizontal();

        var line = p.Building is null
            ? FormatRecipeInputs(p.Recipe)
            : $"@ {p.Building.DisplayName} · " + FormatRecipeInputs(p.Recipe);
        GUILayout.Label(line, _mutedStyle);

        var key = "prod:" + p.Recipe.Name;
        var expanded = _expandedProducers.Contains(key);
        if (GUILayout.Button(expanded ? "▾ why" : "▸ why", _tabStyle, GUILayout.Width(70)))
        {
            if (expanded) _expandedProducers.Remove(key); else _expandedProducers.Add(key);
        }
        if (expanded)
        {
            foreach (var c in p.Cost.Components)
            {
                var l = $"   {c.Label}: {c.Value:0.##}";
                if (!string.IsNullOrEmpty(c.Note)) l += $"   {c.Note}";
                GUILayout.Label(l, _mutedStyle);
            }
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    private void DrawVillagersTab()
    {
        DrawVillageSummary();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{Catalog.Races.Count} races", _mutedStyle);
        GUILayout.FlexibleSpace();
        if (DrawClearButton(_selectedRace)) _selectedRace = null;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        DrawRaceList();
        GUILayout.Space(8);
        DrawRaceDetail();
        GUILayout.EndHorizontal();
    }

    /// <summary>
    /// Renders a small ✕ button when <paramref name="current"/> is non-empty.
    /// Returns true on click. Keeps tab headers consistent.
    /// </summary>
    private bool DrawClearButton(string? current)
    {
        if (string.IsNullOrEmpty(current)) return false;
        return GUILayout.Button(new GUIContent("✕", "Clear selection"),
            _tabStyle, GUILayout.Width(24));
    }

    private void DrawVillageSummary()
    {
        if (!LiveGameState.IsReady) return;
        var summary = _summaryCache?.Get();
        if (summary is null || summary.Races.Count == 0) return;

        GUILayout.Label($"● Village summary — {summary.TotalVillagers} villagers", _bodyStyle);
        GUILayout.BeginHorizontal();
        foreach (var p in summary.Races)
        {
            if (p.Alive == 0 && p.Total == 0) continue;
            var deltaTag = p.CurrentResolve >= p.TargetResolve ? "↑" : "↓";
            var line =
                $"  {p.DisplayName} {p.Alive}" +
                (p.Homeless > 0 ? $" ({p.Homeless} homeless)" : "") +
                $" · {p.CurrentResolve:0.#}/{p.TargetResolve} {deltaTag}";
            GUILayout.Label(line, _mutedStyle);
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    private void DrawRaceList()
    {
        _raceListScroll = GUILayout.BeginScrollView(_raceListScroll,
            GUILayout.Width(140), GUILayout.ExpandHeight(true));
        foreach (var r in Catalog.Races.Values.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var style = (_selectedRace == r.Name) ? _tabActiveStyle : _tabStyle;
            if (GUILayout.Button(r.DisplayName, style)) _selectedRace = r.Name;
        }
        GUILayout.EndScrollView();
    }

    private void DrawRaceDetail()
    {
        GUILayout.BeginVertical();
        if (string.IsNullOrEmpty(_selectedRace))
        {
            GUILayout.Label("Select a race from the list.", _bodyStyle);
            GUILayout.Label("Per-villager live resolve breakdown (target vs current, top contributors) is queued for after a settlement is loaded.", _mutedStyle);
            GUILayout.EndVertical();
            return;
        }

        Func<string, (float Current, int Target)?>? resolveLookup =
            LiveGameState.IsReady ? LiveGameState.ResolveFor : null;
        Func<string, int, IReadOnlyList<(string, int, int)>>? topEffectsLookup =
            LiveGameState.IsReady ? LiveGameState.TopResolveEffectsFor : null;

        var vm = VillagerProvider.For(Catalog, _selectedRace!, resolveLookup, topEffectsLookup);
        GUILayout.Label(vm.Race.DisplayName, _h1Style);
        GUILayout.Label($"resolve {vm.Race.MinResolve:0}–{vm.Race.MaxResolve:0} · starts at {vm.Race.InitialResolve:0} · hunger tolerance {vm.Race.HungerTolerance}", _mutedStyle);
        if (vm.Race.Needs.Count > 0)
            GUILayout.Label("Needs: " + string.Join(", ", vm.Race.Needs), _mutedStyle);
        if (!string.IsNullOrEmpty(vm.Note)) GUILayout.Label(vm.Note!, _mutedStyle);

        // Dietary suggestion: list each need's stockpile (sorted), so the
        // player can see at a glance what they still have to feed this race.
        if (LiveGameState.IsReady && vm.Race.Needs.Count > 0)
        {
            var rows = vm.Race.Needs
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n =>
                {
                    var disp = Catalog.Goods.TryGetValue(n, out var gi) ? gi.DisplayName : n;
                    return (Good: n, Display: disp, Stock: LiveGameState.StockpileOf(n));
                })
                .OrderByDescending(r => r.Stock)
                .ToList();
            if (rows.Count > 0)
            {
                GUILayout.Label("   needs supplied:", _mutedStyle);
                foreach (var row in rows)
                {
                    var style = row.Stock <= 0 ? (_critStyle ?? _mutedStyle)
                              : row.Stock < 10  ? (_warnStyle ?? _mutedStyle)
                              : (_okStyle ?? _mutedStyle);
                    GUILayout.Label($"      {row.Display}: {row.Stock} in stock", style);
                }
            }
        }

        // Resolve forecast: rough "minutes until target" if current < target.
        // We don't know the game's exact climb rate so we use a coarse 1.0
        // resolve/min estimate, marked as approximate.
        if (vm.Resolve is { } rsv && rsv.Current < rsv.Target)
        {
            var gap = rsv.Target - rsv.Current;
            GUILayout.Label(
                $"   forecast: ~{gap / 1.0:0.#}m to target (climb ≈ 1/min, approximate)",
                _mutedStyle);
        }

        DrawResolveSnapshot(vm.Resolve);

        GUILayout.Space(6);
        GUILayout.Label("Race characteristics", _bodyStyle);
        foreach (var c in vm.Race.Characteristics.Where(c => !string.IsNullOrEmpty(c.BuildingTag)))
        {
            var line = $"   {c.BuildingTag}: "
                     + (string.IsNullOrEmpty(c.VillagerPerkEffect) ? "—" : c.VillagerPerkEffect);
            if (!string.IsNullOrEmpty(c.GlobalEffect))   line += $" · global {c.GlobalEffect}";
            if (!string.IsNullOrEmpty(c.BuildingPerk))   line += $" · perk {c.BuildingPerk}";
            GUILayout.Label(line, _mutedStyle);
        }

        GUILayout.Space(6);
        GUILayout.Label($"Best-fit workplaces — {vm.BestWorkplaces.Count} matches", _bodyStyle);
        _raceDetailScroll = GUILayout.BeginScrollView(_raceDetailScroll, GUILayout.ExpandHeight(true));
        foreach (var f in vm.BestWorkplaces)
        {
            GUILayout.BeginHorizontal();
            var marker = f.IsTopRanked && Config.ShowRecommendations.Value ? "★" : $"#{f.Rank}";
            GUILayout.Label(marker, _badgeStyle, GUILayout.Width(24));
            GUILayout.BeginVertical();
            GUILayout.Label(f.Building.DisplayName, _bodyStyle);
            GUILayout.Label($"   tag {f.BuildingTag} → {f.Effect}", _mutedStyle);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    /// <summary>
    /// Tiny sparkline of the rolling resolve samples for a race. Colours rise
    /// green / fall red. Skips when fewer than 2 samples have been collected.
    /// </summary>
    private void DrawResolveSparkline(string raceName)
    {
        if (!_resolveSamples.TryGetValue(raceName, out var q) || q.Count < 2) return;
        var samples = q.ToArray();
        var rect = GUILayoutUtility.GetRect(80, 14, GUILayout.Width(80));
        // Tooltip: hovering the sparkline shows the rolling min/max/last so
        // the player can read exact numbers without expanding the panel.
        GUI.Box(rect, new GUIContent("",
            $"resolve samples: min {samples.Min():0.#} \u00b7 max {samples.Max():0.#} \u00b7 last {samples[samples.Length - 1]:0.#} \u00b7 {samples.Length} pts"));
        var min = samples.Min(); var max = samples.Max();
        var span = Math.Max(0.001f, max - min);
        var rising = samples[samples.Length - 1] >= samples[0];
        var color = rising ? new Color(0.45f, 0.85f, 0.55f, 0.9f)
                           : new Color(0.95f, 0.45f, 0.45f, 0.9f);
        for (var i = 1; i < samples.Length; i++)
        {
            var x0 = rect.x + rect.width * (i - 1) / (float)(samples.Length - 1);
            var x1 = rect.x + rect.width *  i      / (float)(samples.Length - 1);
            var y0 = rect.y + rect.height * (1f - (samples[i - 1] - min) / span);
            var y1 = rect.y + rect.height * (1f - (samples[i]     - min) / span);
            DrawSegment(x0, y0, x1, y1, color);
        }
    }

    private void DrawResolveSnapshot(ResolveSnapshot? r)
    {
        if (r is null) return;

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            $"● live resolve: {r.Current:0.0} now · target {r.Target} · range {r.Min}–{r.Max}",
            _bodyStyle);
        GUILayout.FlexibleSpace();
        if (!string.IsNullOrEmpty(_selectedRace))
            DrawResolveSparkline(_selectedRace!);
        GUILayout.EndHorizontal();

        // Bar: width 0..1 mapped from Min..Max for current; faint marker for target.
        var barRect = GUILayoutUtility.GetRect(0, 12, GUILayout.ExpandWidth(true));
        var span = Math.Max(1, r.Max - r.Min);
        var curT = Mathf.Clamp01((r.Current - r.Min) / span);
        var tgtT = Mathf.Clamp01(((float)r.Target - r.Min) / span);
        GUI.Box(barRect, new GUIContent("",
            $"current {r.Current:0.0} · target {r.Target} · range {r.Min}–{r.Max}"));
        var fillRect = new Rect(barRect.x, barRect.y, barRect.width * curT, barRect.height);
        GUI.DrawTexture(fillRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
            r.Current >= r.Target ? new Color(0.45f, 0.85f, 0.55f, 0.7f) : new Color(0.85f, 0.55f, 0.45f, 0.7f),
            0, 0);
        var tickX = barRect.x + (barRect.width * tgtT);
        GUI.DrawTexture(new Rect(tickX - 1, barRect.y - 2, 2, barRect.height + 4),
            Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
            new Color(1f, 1f, 1f, 0.85f), 0, 0);

        if (r.TopEffects.Count > 0)
        {
            // Net trajectory: sum of positive vs negative TotalImpact across
            // visible effects. Useful at-a-glance signal for whether the race
            // is climbing or sliding regardless of the absolute current value.
            var pos = r.TopEffects.Where(e => e.TotalImpact > 0).Sum(e => e.TotalImpact);
            var neg = r.TopEffects.Where(e => e.TotalImpact < 0).Sum(e => e.TotalImpact);
            var net = pos + neg;
            var arrow = net > 0 ? "↑" : (net < 0 ? "↓" : "≡");
            var trajStyle = net > 0 ? (_okStyle ?? _bodyStyle)
                          : net < 0 ? (_critStyle ?? _bodyStyle)
                          : _bodyStyle;
            GUILayout.Label(
                $"   trajectory: {arrow} net {(net >= 0 ? "+" : "")}{net} (+{pos} / {neg})",
                trajStyle);
            GUILayout.Space(2);
            GUILayout.Label("Top resolve contributors", _bodyStyle);
            foreach (var e in r.TopEffects)
            {
                var sign = e.TotalImpact >= 0 ? "+" : "";
                var line = e.Stacks > 1
                    ? $"   {e.Name}: {sign}{e.TotalImpact}  ({e.Stacks} × {e.ResolvePerStack})"
                    : $"   {e.Name}: {sign}{e.TotalImpact}";
                GUILayout.Label(line, _mutedStyle);
            }
        }
    }

    private void DrawOrdersTab()
    {
        GUILayout.Label("Active orders", _h1Style);
        if (!LiveGameState.IsReady)
        {
            GUILayout.Label("Waiting for a settlement to load.", _mutedStyle);
            return;
        }

        var orders = LiveGameState.ActiveOrders();
        if (orders.Count == 0)
        {
            GUILayout.Label("No active orders.", _mutedStyle);
            return;
        }

        // Sort: tracked first, then picked, then time-pressure (failable + low
        // time-left first), then by display name.
        var sorted = orders
            .OrderByDescending(o => o.Tracked)
            .ThenByDescending(o => o.Picked)
            .ThenByDescending(o => o.ShouldBeFailable)
            .ThenBy(o => o.ShouldBeFailable ? o.TimeLeft : float.MaxValue)
            .ThenBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var picked  = sorted.Count(o => o.Picked);
        var tracked = sorted.Count(o => o.Tracked);
        GUILayout.Label($"{sorted.Count} orders — {picked} picked, {tracked} tracked", _mutedStyle);

        // Tier filter chips: empty filter = no scoping. Re-clicking the last
        // de-selected chip collapses back to "all" so the user always has a
        // path back to the unfiltered view.
        var tiers = orders
            .Select(o => o.Tier ?? "")
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (tiers.Count > 1)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("   tier:", _mutedStyle, GUILayout.Width(48));
            foreach (var tier in tiers)
            {
                var active = _orderTierFilter.Count == 0 ||
                             _orderTierFilter.Contains(tier);
                var lbl = active ? "\u2713 " + tier : tier;
                if (GUILayout.Button(lbl, _chipStyle ?? _tabStyle))
                {
                    if (_orderTierFilter.Count == 0)
                    {
                        // Switching from "all" to "specific": exclude clicked.
                        foreach (var t in tiers) _orderTierFilter.Add(t);
                        _orderTierFilter.Remove(tier);
                    }
                    else if (active) _orderTierFilter.Remove(tier);
                    else _orderTierFilter.Add(tier);
                    if (_orderTierFilter.SetEquals(tiers)) _orderTierFilter.Clear();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (_orderTierFilter.Count > 0)
                sorted = sorted
                    .Where(o => _orderTierFilter.Contains(o.Tier ?? ""))
                    .ToList();
        }

        _ordersScroll = GUILayout.BeginScrollView(_ordersScroll, GUILayout.ExpandHeight(true));
        foreach (var o in sorted) DrawOrderCard(o);
        GUILayout.EndScrollView();
    }

    private void DrawOrderCard(LiveGameState.OrderInfo o)
    {
        GUILayout.BeginHorizontal();

        // Status marker.
        string statusMarker = o.Tracked ? "◉" : (o.Picked ? "●" : "○");
        GUILayout.Label(statusMarker, _badgeStyle, GUILayout.Width(24));

        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label(o.DisplayName, _bodyStyle);
        GUILayout.FlexibleSpace();
        if (o.ShouldBeFailable && o.TimeLeft > 0f)
        {
            var mins = Mathf.Max(0, Mathf.FloorToInt(o.TimeLeft / 60f));
            var secs = Mathf.Max(0, Mathf.FloorToInt(o.TimeLeft % 60f));
            // Red under 1 min, yellow under 5 min, muted otherwise.
            var style = o.TimeLeft < 60f ? _critStyle
                       : o.TimeLeft < 300f ? _warnStyle
                       : _mutedStyle;
            var prefix = o.TimeLeft < 60f ? "⚠ " : "";
            GUILayout.Label($"{prefix}{mins}:{secs:00} left", style);
        }
        GUILayout.EndHorizontal();

        var status = new List<string>();
        if (o.Tracked) status.Add("tracked");
        else if (o.Picked) status.Add("picked");
        else status.Add("unpicked");
        if (o.RewardCategories.Count > 0)
            status.Add("value " + string.Join("+", o.RewardCategories));

        // Tier badge gets its own coloured pill before the rest of the status.
        GUILayout.BeginHorizontal();
        if (!string.IsNullOrEmpty(o.Tier))
        {
            GUILayout.Label("   ", _mutedStyle, GUILayout.Width(24));
            GUILayout.Label(o.Tier, TierBadgeStyle(o.Tier), GUILayout.Width(70));
        }
        if (status.Count > 0)
            GUILayout.Label(
                (string.IsNullOrEmpty(o.Tier) ? "   " : " ") +
                string.Join(" · ", status),
                _mutedStyle);
        GUILayout.EndHorizontal();

        // Tracker pin + open-in-game: track/untrack via SetOrderTracked,
        // and a best-effort "focus" button that calls FocusOrderInGame so
        // the in-game order panel scrolls to this entry (no-op when the
        // game version doesn't expose the right method).
        GUILayout.BeginHorizontal();
        GUILayout.Space(24);
        if (GUILayout.Button(
                new GUIContent(o.Tracked ? "◉ untrack" : "○ track",
                               o.Tracked ? "Untrack this order" : "Track this order"),
                _tabStyle, GUILayout.Width(90)))
        {
            LiveGameState.SetOrderTracked(o.Id, !o.Tracked);
        }
        if (GUILayout.Button(
                new GUIContent("↗ in-game", "Try to focus this order in the in-game UI"),
                _tabStyle, GUILayout.Width(90)))
        {
            LiveGameState.FocusOrderInGame(o.Id);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Storm vs deadline: if the next storm phase will start before this
        // failable order's deadline, the effective due-time is the storm.
        if (o.ShouldBeFailable && o.TimeLeft > 0f)
        {
            var w = LiveGameState.Weather();
            if (w?.SecondsLeft is float sl &&
                sl > 0 && sl < o.TimeLeft &&
                w.PhaseName.IndexOf("storm", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                GUILayout.Label(
                    $"   ⚠ storm hits in {sl / 60f:0.#}m — effective deadline shrinks",
                    _critStyle ?? _mutedStyle);
            }
        }

        if (o.Objectives.Count > 0)
        {
            // Headline: when every objective is completed, surface a green
            // "ready to deliver" line so the player can immediately spot orders
            // that just need a click in the in-game popup.
            if (o.Objectives.All(x => x.Completed))
                GUILayout.Label("   ✓ ready to deliver", _okStyle ?? _mutedStyle);
            foreach (var ob in o.Objectives)
            {
                var prefix = ob.Completed ? "   ✓ " : "   · ";
                GUILayout.Label(prefix + ob.Description, _mutedStyle);
                DrawObjectiveProgress(ob);
                DrawObjectiveEta(ob);
                if (o.Tracked && o.ShouldBeFailable && !ob.Completed)
                    DrawObjectivePlanOfAttack(ob);
            }
        }
        if (o.Rewards.Count > 0)
        {
            var rewardLine = string.Join(", ", o.Rewards.Select(r => r.DisplayName));
            GUILayout.Label(
                $"   reward (score {o.RewardScore:0}): {rewardLine}",
                _mutedStyle);
        }

        // For unpicked orders, show the pick options so the player can
        // compare reward shapes without opening the popup.
        if (!o.Picked)
        {
            var picks = LiveGameState.PickOptionsFor(o.Id);
            if (picks.Count > 1)
            {
                // Categories shared by all non-failed picks; the diff per pick
                // (categories unique to it) is the actual decision aid.
                var live = picks.Where(p => !p.Failed).ToList();
                var common = live.Count == 0
                    ? new HashSet<string>()
                    : new HashSet<string>(live[0].RewardCategories, StringComparer.OrdinalIgnoreCase);
                for (var i = 1; i < live.Count; i++)
                    common.IntersectWith(live[i].RewardCategories);

                GUILayout.Label("   picks — ranked by reward score:", _mutedStyle);
                foreach (var p in picks)
                {
                    var marker = p.Failed
                        ? "✕"
                        : (p.IsTopRanked && Config.ShowRecommendations.Value ? "★" : "·");
                    var rewardNames = p.Rewards.Count == 0
                        ? "(no rewards)"
                        : string.Join(", ", p.Rewards.Select(r => r.DisplayName));
                    var cats = p.RewardCategories.Count == 0 ? "" :
                        $" [{string.Join("+", p.RewardCategories)}]";
                    GUILayout.Label(
                        $"      {marker} {p.SetIndexLabel} (score {p.RewardScore:0}){cats} → {rewardNames}",
                        _mutedStyle);
                    // Diff: categories in this pick that no other pick offers.
                    if (!p.Failed && live.Count > 1)
                    {
                        var uniques = p.RewardCategories
                            .Where(c => !common.Contains(c) &&
                                live.Where(o2 => o2 != p)
                                    .All(o2 => !o2.RewardCategories.Contains(c, StringComparer.OrdinalIgnoreCase)))
                            .ToList();
                        if (uniques.Count > 0)
                            GUILayout.Label(
                                $"         only here: {string.Join(", ", uniques)}",
                                _mutedStyle);
                    }
                }
            }
        }

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    private void DrawSettingsTab()
    {
        GUILayout.Label("Settings", _h1Style);
        _settingsScroll = GUILayout.BeginScrollView(_settingsScroll, GUILayout.ExpandHeight(true));

        // Free-text filter: matches against the *property* names we render so
        // the table-of-contents shrinks as the user types.
        GUILayout.BeginHorizontal();
        GUILayout.Label("   filter:", _mutedStyle, GUILayout.Width(60));
        _settingsFilter = GUILayout.TextField(_settingsFilter ?? "", GUILayout.Width(180));
        if (!string.IsNullOrEmpty(_settingsFilter) &&
            GUILayout.Button("✕", _tabStyle, GUILayout.Width(24)))
            _settingsFilter = "";
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        DrawSettingHeader("General");
        if (MatchesSettingsFilter("ShowRecommendations"))
            DrawBoolSetting(Config.ShowRecommendations,      "Show recommendations");
        if (MatchesSettingsFilter("VisibleByDefault"))
            DrawBoolSetting(Config.VisibleByDefault,         "Visible by default");
        if (MatchesSettingsFilter("HideEmptyRecipeBuildings"))
            DrawBoolSetting(Config.HideEmptyRecipeBuildings, "Hide empty-recipe buildings");

        DrawSettingHeader("Tabs");
        // Reflection-driven: any future ConfigEntry<bool> named Show*Tab is
        // picked up automatically. Keeps Settings in sync with PluginConfig.
        var tabToggles = typeof(PluginConfig)
            .GetProperties()
            .Where(p => p.Name.StartsWith("Show", StringComparison.Ordinal)
                     && p.Name.EndsWith("Tab", StringComparison.Ordinal)
                     && p.PropertyType == typeof(BepInEx.Configuration.ConfigEntry<bool>))
            .OrderBy(p => p.Name, StringComparer.Ordinal);
        foreach (var prop in tabToggles)
        {
            if (!MatchesSettingsFilter(prop.Name)) continue;
            var entry = (BepInEx.Configuration.ConfigEntry<bool>?)prop.GetValue(Config);
            if (entry is null) continue;
            // Strip "Show" prefix and "Tab" suffix for the human-friendly label.
            var human = prop.Name.Substring(4, prop.Name.Length - 7) + " tab";
            DrawBoolSetting(entry, human);
        }
        // Reset-to-defaults button for the Tabs section. Iterates the same
        // Show*Tab properties and restores each entry's compiled-in default.
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(new GUIContent("reset tabs",
                "Reset all tab visibility toggles to defaults"),
                _tabStyle, GUILayout.Width(110)))
        {
            foreach (var prop in tabToggles)
            {
                var entry = (BepInEx.Configuration.ConfigEntry<bool>?)prop.GetValue(Config);
                if (entry?.DefaultValue is bool b) entry.Value = b;
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        DrawSettingHeader("Hotkey");
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            _rebindingHotkey
                ? "   press a key… (Esc to cancel)"
                : $"   toggle: {Config.ToggleHotkey.Value}",
            _mutedStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(_rebindingHotkey ? "cancel" : "rebind",
                             _tabStyle, GUILayout.Width(70)))
        {
            _rebindingHotkey = !_rebindingHotkey;
        }
        GUILayout.EndHorizontal();
        TryCaptureRebind();
        GUILayout.Label("   Ctrl+1..8 also switches tabs while the panel is visible.",
            _mutedStyle);

        GUILayout.Space(8);
        DrawSettingHeader("Catalog");
        var src = Catalog.IsEmpty ? "(empty)" : $"game {Catalog.GameVersion}";
        GUILayout.Label(
            $"   {Catalog.Buildings.Count} buildings · {Catalog.Goods.Count} goods · {Catalog.Recipes.Count} recipes · {Catalog.Races.Count} races",
            _mutedStyle);
        GUILayout.Label($"   source: {src}", _mutedStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("reload catalog", _tabStyle, GUILayout.Width(140)))
        {
            try { StormGuidePlugin.ReloadCatalog(); _catalogReloadStatus = "reloaded."; }
            catch (Exception ex) { _catalogReloadStatus = "error: " + ex.Message; }
        }
        if (!string.IsNullOrEmpty(_catalogReloadStatus))
            GUILayout.Label("   " + _catalogReloadStatus, _mutedStyle);
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        DrawSettingHeader("Risk thresholds");
        GUILayout.BeginHorizontal();
        GUILayout.Label("   goods runway minutes:", _mutedStyle, GUILayout.Width(180));
        var thr = Config.GoodsAtRiskThresholdMinutes.Value;
        var text = GUILayout.TextField(thr.ToString("0.#"), GUILayout.Width(60));
        if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var nv) &&
            Math.Abs(nv - thr) > 0.01f)
        {
            Config.GoodsAtRiskThresholdMinutes.Value = Mathf.Clamp(nv, 1f, 60f);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("   race ratio targets:", _mutedStyle, GUILayout.Width(180));
        Config.RaceRatioTargets.Value = GUILayout.TextField(
            Config.RaceRatioTargets.Value ?? "", GUILayout.Width(220));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Label("   format: race=pct,race=pct — e.g. beaver=30,human=40",
            _mutedStyle);

        GUILayout.Space(8);
        DrawSettingHeader("Config sync");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("export to clipboard",
                "Copy current StormGuide config as JSON to the clipboard"),
                _tabStyle, GUILayout.Width(160)))
        {
            try { GUIUtility.systemCopyBuffer = ExportConfigJson(); }
            catch { }
        }
        if (GUILayout.Button(new GUIContent("import from clipboard",
                "Apply JSON in the clipboard to the StormGuide config"),
                _tabStyle, GUILayout.Width(170)))
        {
            try { ImportConfigJson(GUIUtility.systemCopyBuffer ?? ""); }
            catch { }
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        DrawSettingHeader("Pin presets");
        DrawPinPresets();

        GUILayout.Space(8);
        DrawSettingHeader("Docs");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_docView == "README.md" ? "▾ README" : "▸ README",
                             _tabStyle, GUILayout.Width(110)))
            _docView = _docView == "README.md" ? "" : "README.md";
        if (GUILayout.Button(_docView == "AGENTS.md" ? "▾ AGENTS" : "▸ AGENTS",
                             _tabStyle, GUILayout.Width(110)))
            _docView = _docView == "AGENTS.md" ? "" : "AGENTS.md";
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(_docView))
        {
            var docText = LiveGameState.ReadEmbeddedDoc(_docView);
            if (string.IsNullOrEmpty(docText))
                GUILayout.Label($"   {_docView}: not found in plugin resources.", _mutedStyle);
            else
            {
                _docScroll = GUILayout.BeginScrollView(_docScroll,
                    GUILayout.Height(180), GUILayout.ExpandWidth(true));
                GUILayout.TextArea(docText);
                GUILayout.EndScrollView();
            }
        }

        GUILayout.EndScrollView();
    }

    /// <summary>
    /// While in rebind mode, captures the next non-modifier KeyDown and writes
    /// it back to the toggle hotkey. Esc cancels.
    /// </summary>
    private void TryCaptureRebind()
    {
        if (!_rebindingHotkey) return;
        var ev = Event.current;
        if (ev == null || ev.type != EventType.KeyDown) return;
        var key = ev.keyCode;
        if (key == KeyCode.None) return;
        if (key == KeyCode.Escape) { _rebindingHotkey = false; ev.Use(); return; }
        // Skip pure modifiers — wait for a real key.
        if (key is KeyCode.LeftShift or KeyCode.RightShift or
                  KeyCode.LeftControl or KeyCode.RightControl or
                  KeyCode.LeftAlt or KeyCode.RightAlt or
                  KeyCode.LeftCommand or KeyCode.RightCommand) return;

        var modifiers = new List<KeyCode>();
        if (ev.control) modifiers.Add(KeyCode.LeftControl);
        if (ev.shift)   modifiers.Add(KeyCode.LeftShift);
        if (ev.alt)     modifiers.Add(KeyCode.LeftAlt);
        Config.ToggleHotkey.Value = new BepInEx.Configuration.KeyboardShortcut(
            key, modifiers.ToArray());
        StormGuidePlugin.Log.LogInfo($"StormGuide hotkey rebound to {Config.ToggleHotkey.Value}.");
        _rebindingHotkey = false;
        ev.Use();
    }

    private void DrawSettingHeader(string label)
    {
        GUILayout.Space(4);
        GUILayout.Label(label, _bodyStyle);
    }

    /// <summary>
    /// Returns true when the Settings filter is empty or when
    /// <paramref name="propertyName"/> contains the filter substring (case
    /// insensitive). Used to gate individual setting rows.
    /// </summary>
    private bool MatchesSettingsFilter(string propertyName)
    {
        if (string.IsNullOrEmpty(_settingsFilter)) return true;
        return propertyName.IndexOf(_settingsFilter,
            StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Settings sub-panel for managing named pin presets. Stored as
    /// 'name=building|recipe,building|recipe;name2=...' in
    /// <see cref="PluginConfig.PinPresets"/>; load replaces the active pins,
    /// save snapshots them, delete removes the named entry.
    /// </summary>
    private void DrawPinPresets()
    {
        var presets = ParsePinPresets(Config.PinPresets.Value ?? "");
        GUILayout.BeginHorizontal();
        GUILayout.Label("   name:", _mutedStyle, GUILayout.Width(50));
        _newPresetName = GUILayout.TextField(_newPresetName ?? "", GUILayout.Width(140));
        if (GUILayout.Button("save current pins", _tabStyle, GUILayout.Width(140)) &&
            !string.IsNullOrWhiteSpace(_newPresetName))
        {
            presets[_newPresetName.Trim()] = string.Join(",",
                _pinned.Select(p => p.Building + "|" + p.Recipe));
            Config.PinPresets.Value = SerializePinPresets(presets);
            _newPresetName = "";
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        if (presets.Count == 0)
        {
            GUILayout.Label("   (no presets saved)", _mutedStyle);
            return;
        }
        foreach (var kv in presets.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            GUILayout.BeginHorizontal();
            var count = kv.Value.Split(',').Count(s => !string.IsNullOrEmpty(s));
            GUILayout.Label($"   {kv.Key} · {count} pin(s)", _mutedStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("load", _tabStyle, GUILayout.Width(60)))
            {
                _pinned.Clear();
                foreach (var item in kv.Value.Split(','))
                {
                    var parts = item.Split('|');
                    if (parts.Length == 2 &&
                        !string.IsNullOrEmpty(parts[0]) && !string.IsNullOrEmpty(parts[1]))
                        _pinned.Add((parts[0], parts[1]));
                }
                SavePins();
            }
            if (GUILayout.Button("delete", _tabStyle, GUILayout.Width(70)))
            {
                presets.Remove(kv.Key);
                Config.PinPresets.Value = SerializePinPresets(presets);
                GUILayout.EndHorizontal();
                return;  // dictionary mutated; redraw next frame
            }
            GUILayout.EndHorizontal();
        }
    }

    private static Dictionary<string, string> ParsePinPresets(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw)) return result;
        foreach (var entry in raw.Split(';'))
        {
            if (string.IsNullOrEmpty(entry)) continue;
            var eq = entry.IndexOf('=');
            if (eq <= 0) continue;
            result[entry.Substring(0, eq)] = entry.Substring(eq + 1);
        }
        return result;
    }

    private static string SerializePinPresets(Dictionary<string, string> presets) =>
        string.Join(";", presets
            .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => p.Key + "=" + p.Value));

    /// <summary>
    /// Suggests catalog recipes that satisfy the goal of an order objective.
    /// Resolves the matched good via <see cref="LiveGameState.MatchedGoodFor"/>
    /// and ranks producers by (RecipeProfit + base throughput). Indented under
    /// the objective so it reads as a plan-of-attack list.
    /// </summary>
    private void DrawObjectivePlanOfAttack(LiveGameState.OrderObjective ob)
    {
        var matched = LiveGameState.MatchedGoodFor(ob, Catalog);
        if (matched is null) return;
        var recipes = Catalog.Recipes.Values
            .Where(rr => string.Equals(rr.ProducedGood, matched.Name,
                            StringComparison.OrdinalIgnoreCase))
            .Select(rr =>
            {
                var perMin = rr.ProductionTime > 0
                    ? (60.0 * rr.ProducedAmount) / rr.ProductionTime : 0;
                return (Recipe: rr, PerMin: perMin, Profit: RecipeProfit(rr));
            })
            .OrderByDescending(t => t.Profit)
            .ThenByDescending(t => t.PerMin)
            .Take(2)
            .ToList();
        if (recipes.Count == 0) return;
        GUILayout.Label("      plan:", _mutedStyle);
        foreach (var (rr, perMin, profit) in recipes)
        {
            GUILayout.Label(
                $"        → {rr.DisplayName}  · {perMin:0.##}/min  · profit {(profit >= 0 ? "+" : "")}{profit:0.##}/cycle",
                _mutedStyle);
        }
        // Chain visualisation: 2-level walk back from the matched good
        // showing input → produced good with current stockpiles. Useful
        // when an order chain is long and the player needs to know which
        // upstream pile is the bottleneck.
        DrawObjectiveChain(matched);
    }

    /// <summary>
    /// Two-step "X(stock) → Y(stock) → Z(stock)" chain rooted at
    /// <paramref name="target"/>. Walks each step's first input from the
    /// catalog's primary producer; bails out when the chain has no meaningful
    /// upstream (e.g. raw resources).
    /// </summary>
    private void DrawObjectiveChain(StormGuide.Domain.GoodInfo target)
    {
        // Find the primary recipe producing the target.
        var step1 = Catalog.Recipes.Values
            .Where(rr => string.Equals(rr.ProducedGood, target.Name,
                            StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rr => rr.ProductionTime > 0
                ? (60.0 * rr.ProducedAmount) / rr.ProductionTime : 0)
            .FirstOrDefault();
        if (step1 is null || step1.RequiredGoods.Count == 0) return;
        var input1 = step1.RequiredGoods[0].Options.FirstOrDefault();
        if (input1 is null) return;
        var input1Disp = Catalog.Goods.TryGetValue(input1.Good, out var i1Info)
            ? i1Info.DisplayName : input1.Good;
        var input1Stock = LiveGameState.IsReady ? LiveGameState.StockpileOf(input1.Good) : 0;
        var targetStock = LiveGameState.IsReady ? LiveGameState.StockpileOf(target.Name) : 0;
        // Try step 2: producer of input1.
        var step2 = Catalog.Recipes.Values
            .Where(rr => string.Equals(rr.ProducedGood, input1.Good,
                            StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(rr => rr.ProductionTime > 0
                ? (60.0 * rr.ProducedAmount) / rr.ProductionTime : 0)
            .FirstOrDefault();
        string chain;
        if (step2 is null || step2.RequiredGoods.Count == 0)
        {
            chain = $"{input1Disp}({input1Stock}) → {target.DisplayName}({targetStock})";
        }
        else
        {
            var input2 = step2.RequiredGoods[0].Options.FirstOrDefault();
            if (input2 is null) return;
            var input2Disp = Catalog.Goods.TryGetValue(input2.Good, out var i2Info)
                ? i2Info.DisplayName : input2.Good;
            var input2Stock = LiveGameState.IsReady ? LiveGameState.StockpileOf(input2.Good) : 0;
            chain = $"{input2Disp}({input2Stock}) → {input1Disp}({input1Stock}) → {target.DisplayName}({targetStock})";
        }
        GUILayout.Label("      chain: " + chain, _mutedStyle);
    }

    private static void DrawBoolSetting(
        BepInEx.Configuration.ConfigEntry<bool> entry, string label)
    {
        var current = entry.Value;
        var next = GUILayout.Toggle(current, "  " + label);
        if (next != current) entry.Value = next;
    }

    /// <summary>
    /// Serialises a flat snapshot of the config's bool/string entries to a
    /// minimal JSON object. Reflection-driven so new entries are picked up
    /// without changes here. KeyboardShortcut/Vector2 are skipped because they
    /// already round-trip through BepInEx's own .cfg file.
    /// </summary>
    private string ExportConfigJson()
    {
        var sb = new System.Text.StringBuilder("{");
        var first = true;
        foreach (var prop in typeof(PluginConfig).GetProperties()
                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var entry = prop.GetValue(Config);
            if (entry == null) continue;
            var et = entry.GetType();
            if (!et.IsGenericType ||
                et.GetGenericTypeDefinition() != typeof(BepInEx.Configuration.ConfigEntry<>))
                continue;
            var inner = et.GetGenericArguments()[0];
            if (inner != typeof(bool) && inner != typeof(string)) continue;
            var val = et.GetProperty("Value")?.GetValue(entry);
            if (val is null) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(prop.Name).Append("\":");
            if (val is bool b) sb.Append(b ? "true" : "false");
            else sb.Append('"').Append((val.ToString() ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Applies a previously-exported JSON object onto the config. Unknown keys
    /// are silently ignored. Uses Newtonsoft.Json (already referenced) for
    /// resilience against whitespace and quoting variations.
    /// </summary>
    private void ImportConfigJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            foreach (var prop in typeof(PluginConfig).GetProperties())
            {
                if (!obj.TryGetValue(prop.Name, out var token)) continue;
                var entry = prop.GetValue(Config);
                if (entry == null) continue;
                var et = entry.GetType();
                if (!et.IsGenericType ||
                    et.GetGenericTypeDefinition() != typeof(BepInEx.Configuration.ConfigEntry<>))
                    continue;
                var inner = et.GetGenericArguments()[0];
                var valueProp = et.GetProperty("Value");
                if (inner == typeof(bool)) valueProp?.SetValue(entry, token.ToObject<bool>());
                else if (inner == typeof(string)) valueProp?.SetValue(entry, token.ToObject<string>() ?? "");
            }
        }
        catch { /* malformed JSON — ignore. */ }
    }

    private void DrawGladesTab()
    {
        GUILayout.Label("Forest exploration", _h1Style);
        if (!LiveGameState.IsReady)
        {
            GUILayout.Label("Waiting for a settlement to load.", _mutedStyle);
            return;
        }

        var summary = LiveGameState.GladeSummaryFor();
        if (summary is null)
        {
            GUILayout.Label("Glade service is not available yet.", _mutedStyle);
            return;
        }

        _gladesScroll = GUILayout.BeginScrollView(_gladesScroll, GUILayout.ExpandHeight(true));

        var explored = summary.Total > 0
            ? (summary.Discovered * 100f / summary.Total)
            : 0f;
        GUILayout.Label(
            $"● {summary.Discovered} / {summary.Total} glades discovered ({explored:0}%)",
            _bodyStyle);
        GUILayout.Label(
            $"   {summary.Dangerous} dangerous · {summary.Forbidden} forbidden",
            _mutedStyle);
        if (summary.RewardChasesActive > 0)
        {
            GUILayout.Label(
                $"⚠ {summary.RewardChasesActive} reward-chase{(summary.RewardChasesActive == 1 ? "" : "s")} active",
                _bodyStyle);
            GUILayout.Label(
                "   reward chases expire if untouched — prioritise scouting/clearing.",
                _mutedStyle);
            var now = LiveGameState.GameTimeNow();
            foreach (var c in summary.Chases.Take(8))
            {
                if (now is float t && c.End > t && c.Duration > 0f)
                {
                    var remaining = c.End - t;
                    var mins = Mathf.Max(0, Mathf.FloorToInt(remaining / 60f));
                    var secs = Mathf.Max(0, Mathf.FloorToInt(remaining % 60f));
                    var pct = Mathf.Clamp01((t - c.Start) / c.Duration) * 100f;
                    var rowStyle = remaining < 60f ? (_critStyle ?? _mutedStyle)
                                 : remaining < 180f ? (_warnStyle ?? _mutedStyle)
                                 : _mutedStyle;
                    GUILayout.Label(
                        $"   · {c.Model}  —  {mins}:{secs:00} left ({pct:0}% elapsed of {c.Duration:0.#}s window)",
                        rowStyle);
                }
                else
                {
                    GUILayout.Label(
                        $"   · {c.Model}  —  window {c.Start:0.#}…{c.End:0.#}s ({c.Duration:0.#}s)",
                        _mutedStyle);
                }
                // Reward preview — the chase carries one or more reward ids;
                // resolve display names through catalog goods or model service.
                if (c.Rewards.Count > 0)
                {
                    var sample = c.Rewards.Take(3)
                        .Select(rid => Catalog.Goods.TryGetValue(rid, out var gi)
                            ? gi.DisplayName : rid);
                    var more = c.Rewards.Count > 3 ? $" (+{c.Rewards.Count - 3} more)" : "";
                    GUILayout.Label(
                        "        rewards: " + string.Join(", ", sample) + more,
                        _mutedStyle);
                }
            }
            if (summary.Chases.Count > 8)
                GUILayout.Label($"   … and {summary.Chases.Count - 8} more", _mutedStyle);
        }

        // Danger-level distribution: safe = total - dangerous - forbidden.
        if (summary.Total > 0)
        {
            GUILayout.Space(6);
            GUILayout.Label("Danger distribution", _bodyStyle);
            var safe = Math.Max(0, summary.Total - summary.Dangerous - summary.Forbidden);
            DrawDangerDistribution(safe, summary.Dangerous, summary.Forbidden);
            GUILayout.Label(
                $"   safe {safe} · dangerous {summary.Dangerous} · forbidden {summary.Forbidden}",
                _mutedStyle);
        }

        GUILayout.Space(6);
        GUILayout.Label(
            "Tip: dangerous and forbidden glades carry stronger rewards but spawn worse forest mysteries. The game spawns event decisions when scouts enter; this tab will surface those once a settlement provides them.",
            _mutedStyle);

        GUILayout.EndScrollView();
    }

    private void DrawDangerDistribution(int safe, int dangerous, int forbidden)
    {
        var total = safe + dangerous + forbidden;
        if (total <= 0) return;
        var rect = GUILayoutUtility.GetRect(0, 10, GUILayout.ExpandWidth(true));
        rect.xMin += 12; // light indent
        var w     = rect.width;
        var safeW = w * safe / total;
        var dangW = w * dangerous / total;
        var forbW = w - safeW - dangW;
        var x     = rect.x;
        if (safeW > 0)
            GUI.DrawTexture(new Rect(x, rect.y, safeW, rect.height),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
                new Color(0.45f, 0.85f, 0.55f, 0.7f), 0, 0);
        x += safeW;
        if (dangW > 0)
            GUI.DrawTexture(new Rect(x, rect.y, dangW, rect.height),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
                new Color(0.95f, 0.80f, 0.30f, 0.85f), 0, 0);
        x += dangW;
        if (forbW > 0)
            GUI.DrawTexture(new Rect(x, rect.y, forbW, rect.height),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0,
                new Color(0.95f, 0.40f, 0.40f, 0.85f), 0, 0);
    }

    private void DrawEmbarkTab()
    {
        GUILayout.Label("Embark planner", _h1Style);
        _embarkScroll = GUILayout.BeginScrollView(_embarkScroll, GUILayout.ExpandHeight(true));
        GUILayout.Label(
            "Pre-settlement helper sourced from the static catalog. Per-biome ranking and the cornerstone deck composition still need a MetaController join.",
            _mutedStyle);
        GUILayout.Space(6);
        GUILayout.Label($"Catalog ready: {(Catalog.IsEmpty ? "no" : "yes")}.", _bodyStyle);
        if (Catalog.IsEmpty)
        {
            GUILayout.EndScrollView();
            return;
        }
        GUILayout.Label(
            $"   {Catalog.Races.Count} races · {Catalog.Buildings.Count} buildings · {Catalog.Goods.Count} goods",
            _mutedStyle);

        // Race picker reference: resolve range + needs at a glance.
        GUILayout.Space(6);
        GUILayout.Label($"Races — ranked by min resolve", _bodyStyle);
        foreach (var race in Catalog.Races.Values
                     .OrderBy(r => r.MinResolve)
                     .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var needs = race.Needs.Count == 0
                ? "(no needs)"
                : string.Join(", ", race.Needs.Select(n =>
                    Catalog.Goods.TryGetValue(n, out var gi) ? gi.DisplayName : n));
            if (GUILayout.Button(
                    new GUIContent(
                        $"   {race.DisplayName}: resolve {race.MinResolve}–{race.MaxResolve} · needs {needs}",
                        "Open in Villagers tab"),
                    _tabStyle))
            {
                _selectedRace = race.Name;
                _activeTab = Tab.Villagers;
            }
        }

        // Starting goods recommendation: union all races' needs, score each
        // good by (number of races that need it × trade value), surface the
        // top 5 as "bring these on day 1".
        GUILayout.Space(6);
        GUILayout.Label("Starting goods — ranked by need overlap × value", _bodyStyle);
        var startingScore = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var startingHits  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in Catalog.Races.Values)
        {
            foreach (var n in race.Needs)
            {
                if (string.IsNullOrEmpty(n)) continue;
                startingHits.TryGetValue(n, out var h);
                startingHits[n] = h + 1;
            }
        }
        foreach (var kv in startingHits)
        {
            var val = Catalog.Goods.TryGetValue(kv.Key, out var gi)
                ? Math.Max(1, gi.TradingBuyValue) : 1;
            startingScore[kv.Key] = kv.Value * val;
        }
        var top = startingScore
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .ToList();
        if (top.Count == 0)
            GUILayout.Label("   (no race needs found)", _mutedStyle);
        foreach (var kv in top)
        {
            var disp = Catalog.Goods.TryGetValue(kv.Key, out var gi) ? gi.DisplayName : kv.Key;
            GUILayout.Label(
                $"   ★ {disp}  · needed by {startingHits[kv.Key]} race(s)  · score {kv.Value:0.##}",
                _mutedStyle);
        }

        // Cornerstone-tag advisory: rank building tags by (race-perk hits ×
        // catalog-building hits) so the player knows which tags carry the
        // highest leverage if they see a matching cornerstone offered later.
        GUILayout.Space(6);
        GUILayout.Label("Cornerstone tags — most leverage for this race set", _bodyStyle);
        var tagScore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var race in Catalog.Races.Values)
        {
            foreach (var c in race.Characteristics)
            {
                var tag = c.BuildingTag;
                if (string.IsNullOrEmpty(tag)) continue;
                var hits = Catalog.Buildings.Values.Count(b =>
                    b.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
                if (hits == 0) continue;
                tagScore.TryGetValue(tag, out var s);
                tagScore[tag] = s + hits;
            }
        }
        if (tagScore.Count == 0)
            GUILayout.Label("   (no race-tag overlap found)", _mutedStyle);
        foreach (var kv in tagScore.OrderByDescending(kv => kv.Value).Take(5))
            GUILayout.Label(
                $"   \u2605 {kv.Key} \u00b7 {kv.Value} building-hit(s) across {Catalog.Races.Count} race(s)",
                _mutedStyle);

        GUILayout.Space(6);
        GUILayout.Label(
            "Tip: races with overlapping needs (e.g. fuel + porridge) are easier to keep above target resolve early; combine with cornerstones that target their preferred building tags.",
            _mutedStyle);
        GUILayout.EndScrollView();
    }

    private void DrawDiagnosticsTab()
    {
        GUILayout.Label("Diagnostics", _h1Style);
        var tail = StormGuidePlugin.LogTail;
        if (tail is null)
        {
            GUILayout.Label("   log capture is not initialised.", _mutedStyle);
            return;
        }

        // One-shot catalog-diff banner. Stays until the user dismisses it.
        if (!string.IsNullOrEmpty(_catalogDiffNotice))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("ℹ " + _catalogDiffNotice, _warnStyle ?? _mutedStyle);
            if (GUILayout.Button("dismiss", _tabStyle, GUILayout.Width(80)))
                _catalogDiffNotice = "";
            GUILayout.EndHorizontal();
        }

        // Session stats: settlement age + counters tracked across the session.
        DrawSessionStats();
        DrawPerfHistory();
        DrawCrashDumpsList();

        var snapshot = tail.Snapshot();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"   {snapshot.Count} captured plugin log line(s).", _mutedStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(new GUIContent("copy", "Copy all captured lines to clipboard"),
                             _tabStyle, GUILayout.Width(60)))
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var e in snapshot)
                    sb.AppendLine($"{e.UtcAt:O} [{e.Level}] {e.Message}");
                GUIUtility.systemCopyBuffer = sb.ToString();
            }
            catch { /* clipboard sometimes unavailable in fullscreen; swallow */ }
        }
        if (GUILayout.Button("clear", _tabStyle, GUILayout.Width(70)))
            tail.Clear();
        GUILayout.EndHorizontal();
        GUILayout.Label(
            "   Lives in memory only — the BepInEx log file (LogOutput.log) is the source of truth.",
            _mutedStyle);

        GUILayout.Space(4);
        _diagScroll = GUILayout.BeginScrollView(_diagScroll, GUILayout.ExpandHeight(true));
        // Newest first; capacity is ~200 so this is fine to render in one go.
        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            var e = snapshot[i];
            var style = e.Level switch
            {
                BepInEx.Logging.LogLevel.Error or BepInEx.Logging.LogLevel.Fatal => _critStyle,
                BepInEx.Logging.LogLevel.Warning => _warnStyle,
                _ => _mutedStyle,
            };
            GUILayout.Label(
                $"{e.UtcAt:HH:mm:ss} [{e.Level}] {e.Message}",
                style);
        }
        GUILayout.EndScrollView();
    }

    /// <summary>
    /// Settlement age + per-session counters (orders completed, cornerstones
    /// drafted) surfaced under Diagnostics. Useful at-a-glance snapshot for
    /// performance reviews and bug reports.
    /// </summary>
    /// <summary>
    /// Diagnostics sub-panel: per-section p50/p95 frame cost across the
    /// rolling 120-frame ring. Sorted by p95 descending so the slowest
    /// section bubbles to the top.
    /// </summary>
    private void DrawPerfHistory()
    {
        if (_perfHistory.Count == 0) return;
        GUILayout.Space(4);
        GUILayout.Label("Per-section p50 / p95 (last 120 frames)", _bodyStyle);
        foreach (var kv in _perfHistory
                     .Select(kv => (Name: kv.Key, P50: Percentile(kv.Value, 0.5),
                                                  P95: Percentile(kv.Value, 0.95)))
                     .OrderByDescending(t => t.P95))
        {
            GUILayout.Label(
                $"   {kv.Name}: p50 {kv.P50:0.0}ms · p95 {kv.P95:0.0}ms",
                _mutedStyle);
        }
    }

    private static double Percentile(IEnumerable<double> values, double p)
    {
        var arr = values.OrderBy(v => v).ToArray();
        if (arr.Length == 0) return 0;
        var idx = Math.Min(arr.Length - 1,
            (int)Math.Floor((arr.Length - 1) * p));
        return arr[idx];
    }

    /// <summary>
    /// Lists any <c>stormguide-crash-*.txt</c> dumps in the BepInEx config
    /// directory with copy-paths and open-folder buttons. Skipped when none
    /// are present so the section stays out of the way most of the time.
    /// </summary>
    private void DrawCrashDumpsList()
    {
        string? dir = null;
        try { dir = BepInEx.Paths.ConfigPath; } catch { }
        if (string.IsNullOrEmpty(dir)) return;
        string[] files;
        try { files = System.IO.Directory.GetFiles(dir!, "stormguide-crash-*.txt"); }
        catch { return; }
        if (files.Length == 0) return;
        GUILayout.Space(4);
        GUILayout.Label($"Crash dumps — {files.Length} on disk", _bodyStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("copy paths",
                "Copy all crash dump paths to clipboard"),
                _tabStyle, GUILayout.Width(110)))
        {
            try { GUIUtility.systemCopyBuffer = string.Join("\n", files); }
            catch { }
        }
        if (GUILayout.Button(new GUIContent("open dir",
                "Open BepInEx config dir in the system file browser"),
                _tabStyle, GUILayout.Width(110)))
        {
            try { Application.OpenURL("file://" + dir!.Replace("\\", "/")); }
            catch { }
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        foreach (var f in files.OrderByDescending(x => x).Take(5))
            GUILayout.Label("   \u00b7 " + System.IO.Path.GetFileName(f),
                _mutedStyle);
    }

    private void DrawSessionStats()
    {
        if (!LiveGameState.IsReady) return;
        var now = LiveGameState.GameTimeNow();
        if (now is null || _sessionStartTime is null) return;
        var ageMin = (now.Value - _sessionStartTime.Value) / 60f;
        GUILayout.Space(4);
        GUILayout.Label("Session stats", _bodyStyle);
        GUILayout.Label(
            $"   age: {ageMin:0.#} min · orders completed: {_completedDurations.Count} · cornerstones drafted: {_cornerstonesDraftedThisSession}",
            _mutedStyle);
        // Locale fallback notice: if any catalog DisplayName is empty, hint
        // once that we'll show model names instead.
        if (!_localeWarned)
        {
            var missing = Catalog.Goods.Values.Count(g => string.IsNullOrWhiteSpace(g.DisplayName));
            if (missing > 0)
            {
                GUILayout.Label(
                    $"   ℹ {missing} good(s) have no localised display name; falling back to model ids.",
                    _mutedStyle);
                _localeWarned = true;
            }
        }
    }

    private void DrawDraftTab()
    {
        var vm = CornerstoneDraftProvider.Current();
        if (!vm.IsActive)
        {
            GUILayout.Label("Cornerstone Draft", _h1Style);
            GUILayout.Label(vm.Note ?? "Idle.", _mutedStyle);
            GUILayout.Space(6);
            GUILayout.Label("This tab auto-activates when the in-game cornerstone pick popup is shown. Each option is ranked by synergy hits against your current buildings/tags, with the math visible.", _mutedStyle);
            DrawOwnedCornerstones(vm.Owned);
            return;
        }

        GUILayout.Label($"Cornerstone Draft — {vm.Options.Count} options", _h1Style);
        // Auto-pick recommendation: when one option scores >=1.5x runner-up
        // and recommendations are on, surface a green headline so the
        // player can act quickly during the time-limited popup.
        if (Config.ShowRecommendations.Value && vm.Options.Count >= 2)
        {
            var sorted = vm.Options.OrderByDescending(o => o.Synergy.Value).ToList();
            var top = sorted[0]; var second = sorted[1];
            if (second.Synergy.Value > 0 &&
                top.Synergy.Value >= second.Synergy.Value * 1.5)
            {
                GUILayout.Label(
                    $"★ recommended pick: {top.DisplayName} ({top.Synergy.Value:0.##} vs {second.Synergy.Value:0.##})",
                    _okStyle ?? _bodyStyle);
            }
        }
        foreach (var o in vm.Options) DrawCornerstoneOption(o, vm.Options);
        DrawOwnedCornerstones(vm.Owned);
        DrawCornerstoneHistory();
    }

    /// <summary>Lists previously-drafted cornerstones for the current run.</summary>
    private void DrawCornerstoneHistory()
    {
        var history = LiveGameState.CornerstoneHistory();
        if (history.Count == 0) return;
        GUILayout.Space(8);
        GUILayout.Label($"Previously drafted — {history.Count}", _bodyStyle);
        var ms = LiveGameState.Services?.GameModelService;
        foreach (var id in history.Take(20))
        {
            string display = id;
            try
            {
                var eff = ms?.GetEffect(id);
                if (eff != null) display = eff.DisplayName ?? id;
            }
            catch { }
            GUILayout.Label($"   · {display}", _mutedStyle);
        }
        if (history.Count > 20)
            GUILayout.Label($"   … and {history.Count - 20} more", _mutedStyle);
    }

    /// <summary>
    /// Heuristic anti-synergy hint: option's effect typename hints at a
    /// negative modifier (Decrease/Negative/Penalty) AND we already own a
    /// cornerstone targeting the same usability tag. We can't know
    /// definitively without parsing every effect family, but this catches the
    /// obvious self-stomping picks (e.g. a global penalty that erases a buff
    /// you just took).
    /// </summary>
    private void DrawCornerstoneBlocksOwned(CornerstoneOption o)
    {
        try
        {
            var ms = LiveGameState.Services?.GameModelService;
            var eff = ms?.GetEffect(o.EffectId);
            if (eff is null) return;
            var tn = eff.GetType().Name;
            var negative = tn.IndexOf("Decrease", StringComparison.OrdinalIgnoreCase) >= 0
                        || tn.IndexOf("Negative", StringComparison.OrdinalIgnoreCase) >= 0
                        || tn.IndexOf("Penalty",  StringComparison.OrdinalIgnoreCase) >= 0;
            if (!negative) return;
            var owned = LiveGameState.OwnedCornerstoneUsabilityTags();
            var hits = new List<string>();
            try
            {
                foreach (var t in eff.usabilityTags ?? Array.Empty<Eremite.Model.ModelTag>())
                {
                    if (t == null || string.IsNullOrEmpty(t.Name)) continue;
                    if (owned.ContainsKey(t.Name)) hits.Add(t.Name);
                }
            }
            catch { }
            if (hits.Count == 0) return;
            GUILayout.Label(
                $"   ⚠ may conflict with owned cornerstone(s) on tag(s): {string.Join(", ", hits)}",
                _critStyle ?? _mutedStyle);
        }
        catch { /* swallow — advisory only. */ }
    }

    /// <summary>Renders the cornerstone diff line under each option.</summary>
    private void DrawCornerstoneDiff(CornerstoneOption o)
    {
        if (o.AffectedBuildings == 0 &&
            (o.NewlyTargetedTags is null || o.NewlyTargetedTags.Count == 0)) return;
        var parts = new List<string>();
        if (o.AffectedBuildings > 0)
            parts.Add($"affects {o.AffectedBuildings} built building{(o.AffectedBuildings == 1 ? "" : "s")}");
        if (o.NewlyTargetedTags is { Count: > 0 } nt)
        {
            var max = Math.Min(3, nt.Count);
            var sample = string.Join(", ", nt.Take(max));
            parts.Add(nt.Count > max
                ? $"+{nt.Count} new tag(s) ({sample}…)"
                : $"+{nt.Count} new tag(s) ({sample})");
        }
        GUILayout.Label("   delta: " + string.Join(" · ", parts), _mutedStyle);
    }

    private void DrawOwnedCornerstones(IReadOnlyList<OwnedCornerstoneInfo> owned)
    {
        if (owned.Count == 0) return;
        GUILayout.Space(8);
        GUILayout.Label($"Currently owned — {owned.Count}", _bodyStyle);
        foreach (var o in owned)
        {
            // Tooltip carries the description so hovering the name surfaces the
            // full effect text without dedicating a permanent line to it.
            GUILayout.Label(
                new GUIContent($"   · {o.DisplayName}", o.Description ?? ""),
                _bodyStyle);
            if (!string.IsNullOrEmpty(o.Description))
                GUILayout.Label($"     {o.Description}", _mutedStyle);
        }
    }

    private void DrawCornerstoneOption(
        CornerstoneOption o, IReadOnlyList<CornerstoneOption> all)
    {
        GUILayout.BeginHorizontal();
        var marker = o.IsTopRanked && Config.ShowRecommendations.Value ? "★" : $"#{o.Rank}";
        GUILayout.Label(marker, _badgeStyle, GUILayout.Width(24));
        // Cornerstone option icon — best-effort. Effect.Icon is a Sprite that
        // sits inside an atlas; we slice via DrawTextureWithTexCoords so the
        // panel only paints the relevant sub-rect. Falls through silently if
        // the sprite/texture aren't available.
        DrawCornerstoneIcon(o.EffectId);
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        // Tooltip: full effect description on hover.
        GUILayout.Label(new GUIContent(o.DisplayName, o.Description ?? ""), _bodyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(o.Synergy.Format("0"), _bodyStyle);
        // Compare toggle: shows a side-by-side delta against the other
        // options. Click again on the same row to collapse.
        var compareOpen = _compareOption == o.EffectId;
        if (GUILayout.Button(
                new GUIContent(compareOpen ? "▾ vs" : "▸ vs",
                    "Compare against the other options"),
                _tabStyle, GUILayout.Width(50)))
        {
            _compareOption = compareOpen ? null : o.EffectId;
        }
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(o.Description))
            GUILayout.Label(o.Description, _mutedStyle);
        DrawCornerstoneDiff(o);
        DrawCornerstoneBlocksOwned(o);
        DrawCornerstoneAffected(o);
        foreach (var c in o.Synergy.Components)
        {
            var line = $"   {c.Label}: {c.Value:0.##}";
            if (!string.IsNullOrEmpty(c.Note)) line += $"   {c.Note}";
            GUILayout.Label(line, _mutedStyle);
        }
        if (compareOpen) DrawCornerstoneCompare(o, all);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    /// <summary>
    /// Inline "would affect:" preview shown under each draft option without
    /// requiring the compare toggle. Lists up to 4 buildings for each of the
    /// option's first two newly-targeted tags so the player sees concrete
    /// names rather than just a count.
    /// </summary>
    private void DrawCornerstoneAffected(CornerstoneOption o)
    {
        var tags = o.NewlyTargetedTags ?? Array.Empty<string>();
        if (tags.Count == 0) return;
        foreach (var tag in tags.Take(2))
        {
            var bs = LiveGameState.BuildingsCarryingTag(tag);
            if (bs.Count == 0) continue;
            var sample = string.Join(", ", bs.Take(4));
            var more = bs.Count > 4 ? $" (+{bs.Count - 4} more)" : "";
            GUILayout.Label(
                $"   would affect ({tag}): {sample}{more}",
                _mutedStyle);
        }
    }

    /// <summary>
    /// Renders a small side-by-side delta panel showing this option's score,
    /// affected-buildings count, and unique tags vs each of the other options.
    /// Acts as the "if I skip this, what do I lose?" view during draft.
    /// </summary>
    private void DrawCornerstoneCompare(
        CornerstoneOption focus, IReadOnlyList<CornerstoneOption> all)
    {
        GUILayout.Label("   compare — vs other options:", _mutedStyle);
        foreach (var other in all)
        {
            if (other.EffectId == focus.EffectId) continue;
            var dScore   = focus.Synergy.Value - other.Synergy.Value;
            var dAffect  = focus.AffectedBuildings - other.AffectedBuildings;
            var focusTags = focus.NewlyTargetedTags ?? Array.Empty<string>();
            var otherTags = other.NewlyTargetedTags ?? Array.Empty<string>();
            var onlyHere = focusTags
                .Where(t => !otherTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                .Take(3).ToList();
            var loseHint = onlyHere.Count == 0
                ? ""
                : $"  unique tag(s): {string.Join(", ", onlyHere)}";
            var style = dScore > 0 ? (_okStyle ?? _mutedStyle)
                      : dScore < 0 ? (_critStyle ?? _mutedStyle)
                      : _mutedStyle;
            GUILayout.Label(
                $"      vs {other.DisplayName}: \u0394score {dScore:+0.##;-0.##;0} \u00b7 \u0394buildings {dAffect:+0;-0;0}{loseHint}",
                style);
        }
        // Per-unique-tag deep dive: enumerate the actual buildings each tag
        // would touch so the player sees "this hits Bakery, Brewery" rather
        // than just a count. Capped at 5 buildings per tag.
        var focusOnly = (focus.NewlyTargetedTags ?? Array.Empty<string>()).ToList();
        if (focusOnly.Count > 0)
        {
            foreach (var tag in focusOnly.Take(3))
            {
                var bs = LiveGameState.BuildingsCarryingTag(tag);
                if (bs.Count == 0) continue;
                var sample = string.Join(", ", bs.Take(5));
                var more = bs.Count > 5 ? $" (+{bs.Count - 5} more)" : "";
                GUILayout.Label(
                    $"      tag {tag} → {sample}{more}",
                    _mutedStyle);
            }
        }
    }

    /// <summary>
    /// Paints the cornerstone effect's sprite icon inside a fixed 22×22 box
    /// next to the marker. Resolves the live <c>EffectModel</c> at draw time
    /// since icons aren't in our static catalog. Safe-guarded; failures show
    /// no icon but preserve layout via a reserved <see cref="GUILayout.Space"/>.
    /// </summary>
    private void DrawCornerstoneIcon(string effectId)
    {
        const float size = 22f;
        var slot = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size));
        try
        {
            var ms = LiveGameState.Services?.GameModelService;
            var eff = ms?.GetEffect(effectId);
            UnityEngine.Sprite? icon = null;
            try { icon = (UnityEngine.Sprite?)eff?.GetType().GetProperty("Icon")?.GetValue(eff); }
            catch { }
            if (icon == null) return;
            var tex = icon.texture;
            if (tex == null) return;
            var r = icon.textureRect;
            // Convert atlas pixel rect to normalised UV coords.
            var uv = new Rect(r.x / tex.width, r.y / tex.height,
                              r.width / tex.width, r.height / tex.height);
            GUI.DrawTextureWithTexCoords(slot, tex, uv, alphaBlend: true);
        }
        catch { /* swallow — icons are decorative. */ }
    }

    private void DrawFooter()
    {
        GUILayout.BeginHorizontal();
        var src = Catalog.IsEmpty ? "(catalog empty)" : $"game {Catalog.GameVersion}";
        GUILayout.Label(src, _bodyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"v{StormGuidePlugin.PluginVersion} • {Config.ToggleHotkey.Value} to toggle", _bodyStyle);
        GUILayout.EndHorizontal();
    }

    private void EnsureStyles()
    {
        var compact      = Config.CompactMode.Value;
        var compactLists = Config.CompactLists.Value;
        // Rebuild styles when either compact toggle flips so fonts/heights
        // can change live.
        if (_windowStyle != null &&
            _lastCompactApplied == compact &&
            _lastCompactListsApplied == compactLists) return;
        _lastCompactApplied = compact;
        _lastCompactListsApplied = compactLists;

        var body  = compact ? 11 : 12;
        var muted = compact ? 10 : 11;
        var h1    = compact ? 13 : 14;
        var pad   = compact ? new RectOffset(8, 8, 20, 8) : new RectOffset(10, 10, 22, 10);

        _windowStyle = new GUIStyle(GUI.skin.window) { padding = pad };
        _bodyStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
            fontSize = body,
            // Rich text on the body label lets HighlightMatch's <b>...</b>
            // wrap render bold inside Building/Good list rows and recipe
            // cards. Tab/button styles get richText set independently.
            richText = true,
        };
        _h1Style = new GUIStyle(_bodyStyle) { fontSize = h1, fontStyle = FontStyle.Bold };
        _mutedStyle = new GUIStyle(_bodyStyle) { fontSize = muted };
        _mutedStyle.normal.textColor = new Color(0.75f, 0.75f, 0.78f, 1f);
        _badgeStyle = new GUIStyle(_bodyStyle) { fontSize = h1, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        _tabStyle = new GUIStyle(GUI.skin.button) { fixedHeight = compact ? 20 : 24, fontSize = body };
        _tabActiveStyle = new GUIStyle(_tabStyle);
        _tabActiveStyle.normal.background = _tabStyle.active.background;
        _tabActiveStyle.normal.textColor  = Color.white;
        _tabActiveStyle.fontStyle         = FontStyle.Bold;
        _warnStyle = new GUIStyle(_mutedStyle) { fontStyle = FontStyle.Bold };
        _warnStyle.normal.textColor = new Color(0.95f, 0.80f, 0.30f, 1f);   // amber
        _critStyle = new GUIStyle(_mutedStyle) { fontStyle = FontStyle.Bold };
        _critStyle.normal.textColor = new Color(0.95f, 0.40f, 0.40f, 1f);   // red
        _okStyle = new GUIStyle(_mutedStyle) { fontStyle = FontStyle.Bold };
        _okStyle.normal.textColor = new Color(0.45f, 0.85f, 0.55f, 1f);   // green
        // Pill-style for tag chips: tighter padding + no wrap so chips read as
        // labels rather than full-height buttons.
        _chipStyle = new GUIStyle(_tabStyle)
        {
            fixedHeight = 0,
            padding     = new RectOffset(6, 6, 2, 2),
            margin      = new RectOffset(2, 2, 1, 1),
            fontSize    = compact ? 10 : 11,
        };
        // Compact list-button: shorter rows for the Building/Good list scrollers.
        // Falls back to the regular tab style when the toggle is off so callers
        // can pick a style without branching every frame.
        _listButtonStyle = compactLists
            ? new GUIStyle(_tabStyle)
              {
                  fixedHeight = compact ? 16 : 18,
                  padding     = new RectOffset(6, 6, 1, 1),
                  margin      = new RectOffset(2, 2, 0, 0),
                  fontSize    = compact ? 10 : 11,
                  richText    = true,
              }
            : new GUIStyle(_tabStyle) { richText = true };
    }
}
