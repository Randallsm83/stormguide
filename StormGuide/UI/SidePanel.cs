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

    public enum Tab { Home, Building, Good, Villagers, Orders, Glades, Draft, Settings }

    // ---- Home tab state ----------------------------------------------------
    private Vector2 _homeScroll;

    // ---- Orders/Glades tab state ------------------------------------------
    private Vector2 _ordersScroll;
    private Vector2 _gladesScroll;
    private Vector2 _settingsScroll;

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

        // Cache the slow aggregates: 0.5s is well below player perception
        // for these kinds of metrics, but it cuts redundant scans by ~30x at
        // the typical IMGUI redraw rate.
        _alertsCache  = new TtlCache<LiveGameState.SettlementAlerts?>(
            () => LiveGameState.AlertsFor(Catalog), ttlSeconds: 0.5f);
        _summaryCache = new TtlCache<StormGuide.Domain.VillageSummary?>(
            () => LiveGameState.VillageSummary(name =>
                Catalog.Races.TryGetValue(name, out var r) ? r.DisplayName : name),
            ttlSeconds: 0.5f);
        _ownedCache   = new TtlCache<IReadOnlyList<LiveGameState.OwnedCornerstone>>(
            LiveGameState.OwnedCornerstones, ttlSeconds: 1.0f);
    }

    private void Update()
    {
        if (Config.ToggleHotkey.Value.IsDown())
        {
            _visible = !_visible;
            StormGuidePlugin.Log.LogInfo($"StormGuide panel {(_visible ? "shown" : "hidden")}.");
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
    }

    private void DrawWindow(int _)
    {
        DrawTitleControls();
        DrawTabs();
        DrawAlertsStrip();
        GUILayout.Space(6);

        switch (_activeTab)
        {
            case Tab.Home:      DrawHomeTab();      break;
            case Tab.Building:  DrawBuildingTab();  break;
            case Tab.Good:      DrawGoodTab();      break;
            case Tab.Villagers: DrawVillagersTab(); break;
            case Tab.Orders:    DrawOrdersTab();    break;
            case Tab.Glades:    DrawGladesTab();    break;
            case Tab.Draft:     DrawDraftTab();     break;
            case Tab.Settings:  DrawSettingsTab();  break;
        }

        GUILayout.FlexibleSpace();
        DrawFooter();

        DrawResizeHandle();

        // Make the title bar (top 22 px) draggable, but exclude the right edge
        // where the reset button lives so clicks there don't initiate drag.
        GUI.DragWindow(new Rect(0, 0, _rect.width - 28, 22));
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
        }
    }

    private bool IsTabVisible(Tab tab) => tab switch
    {
        Tab.Home      => Config.ShowHomeTab.Value,
        Tab.Building  => Config.ShowBuildingTab.Value,
        Tab.Good      => Config.ShowGoodTab.Value,
        Tab.Villagers => Config.ShowVillagersTab.Value,
        Tab.Orders    => Config.ShowOrdersTab.Value,
        Tab.Glades    => Config.ShowGladesTab.Value,
        Tab.Draft     => Config.ShowDraftTab.Value,
        Tab.Settings  => Config.ShowSettingsTab.Value,
        _ => true
    };

    private void DrawTab(Tab tab, string label)
    {
        var style = (_activeTab == tab) ? _tabActiveStyle : _tabStyle;
        if (GUILayout.Button(label, style)) _activeTab = tab;
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

        DrawHomeVillage();
        DrawHomeTrader();
        DrawHomeIdle();
        DrawHomeRisks();
        DrawHomeOrders();
        DrawHomeGlades();
        DrawHomeCornerstones();

        GUILayout.EndScrollView();
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
        }
        if (nxt is not null)
        {
            var disp = string.IsNullOrEmpty(nxt.DisplayName) ? "(unnamed)" : nxt.DisplayName;
            GUILayout.Label(
                $"   next: {disp} · wants {nxt.Buys.Count} · sells {nxt.Sells.Count}",
                _mutedStyle);
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

        GUILayout.Space(6);
        GUILayout.Label($"⚠ Goods at risk — {alerts.GoodsAtRisk.Count}", _bodyStyle);
        foreach (var g in alerts.GoodsAtRisk.Take(5))
        {
            var disp = Catalog.Goods.TryGetValue(g.Good, out var gi) ? gi.DisplayName : g.Good;
            var label = $"   {disp}  —  {g.RunwayMinutes:0.#} min runway";
            if (GUILayout.Button(new GUIContent(label, "Open in Good tab"), _tabStyle))
            {
                _activeTab = Tab.Good;
                _selectedGood = g.Good;
                _flowExpanded = true;
            }
        }
    }

    private void DrawHomeOrders()
    {
        var orders = LiveGameState.ActiveOrders();
        if (orders.Count == 0) return;

        var picked  = orders.Count(o => o.Picked);
        var tracked = orders.Count(o => o.Tracked);
        var critical = orders.Count(o => o.ShouldBeFailable && o.TimeLeft > 0f && o.TimeLeft < 60f);

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

    private void DrawIdleBuildingsBanner()
    {
        if (!LiveGameState.IsReady) return;
        var idle = LiveGameState.IdleBuildings();
        if (idle.Count == 0) return;

        // Group by model name so duplicates collapse: "3× Bakery, 1× Brewery".
        var grouped = idle
            .GroupBy(t => t.ModelName)
            .Select(g => $"{g.Count()}× {g.First().DisplayName}")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
        GUILayout.Label("⚠ Idle workshops: " + string.Join(", ", grouped), _bodyStyle);
        GUILayout.Space(2);
    }

    private void DrawBuildingList()
    {
        _buildingListScroll = GUILayout.BeginScrollView(_buildingListScroll,
            GUILayout.Width(160), GUILayout.ExpandHeight(true));

        var query = _buildingSearch?.Trim() ?? "";
        var matches = Catalog.Buildings.Values
            .Where(b => !Config.HideEmptyRecipeBuildings.Value || b.Recipes.Count > 0)
            .Where(b => query.Length == 0
                     || b.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                     || b.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(b => b.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var b in matches)
        {
            var label = b.DisplayName;
            var style = (_selectedBuilding == b.Name) ? _tabActiveStyle : _tabStyle;
            if (GUILayout.Button(label, style)) _selectedBuilding = b.Name;
        }
        GUILayout.EndScrollView();
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
        if (vm.Building.Tags.Count > 0) meta += $" · tags: {string.Join(", ", vm.Building.Tags)}";
        GUILayout.Label(meta, _mutedStyle);
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
        DrawWhyAllButton(_expandedRecipes, vm.Recipes.Select(r => r.Recipe.Name));
        GUILayout.EndHorizontal();

        _buildingDetailScroll = GUILayout.BeginScrollView(_buildingDetailScroll, GUILayout.ExpandHeight(true));
        foreach (var rk in vm.Recipes) DrawRecipeCard(rk);
        GUILayout.EndScrollView();

        GUILayout.EndVertical();
    }

    /// <summary>
    /// Renders a one-tap toggle that expands every "why" row in <paramref name="set"/>
    /// when collapsed, or clears them when any are expanded. Used to flip
    /// reasoning rows on the Building/Good tabs without per-row clicks.
    /// </summary>
    private void DrawWhyAllButton(HashSet<string> set, IEnumerable<string> keys)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0) return;
        var anyExpanded = keyList.Any(k => set.Contains(k));
        var label = anyExpanded ? "▾ why × all" : "▸ why × all";
        if (GUILayout.Button(new GUIContent(label, anyExpanded ? "Collapse all reasoning" : "Expand all reasoning"),
                             _tabStyle, GUILayout.Width(90)))
        {
            if (anyExpanded) set.Clear();
            else foreach (var k in keyList) set.Add(k);
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
        GUILayout.Label(rk.Recipe.DisplayName, _bodyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(rk.Throughput.Format("0.##"), _bodyStyle);
        GUILayout.EndHorizontal();

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
        if (GUILayout.Button(expanded ? "▾ why" : "▸ why", _tabStyle, GUILayout.Width(70)))
        {
            if (expanded) _expandedRecipes.Remove(key); else _expandedRecipes.Add(key);
        }
        if (expanded)
        {
            foreach (var c in rk.Throughput.Components)
            {
                var line = $"   {c.Label}: {c.Value:0.##}";
                if (!string.IsNullOrEmpty(c.Note)) line += $"   {c.Note}";
                GUILayout.Label(line, _mutedStyle);
            }
        }
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.Space(4);
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
        var matches = Catalog.Goods.Values
            .Where(g => query.Length == 0
                     || g.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                     || g.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                     || g.Category.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase);

        string? lastCategory = null;
        foreach (var g in matches)
        {
            if (g.Category != lastCategory)
            {
                GUILayout.Space(4);
                GUILayout.Label(string.IsNullOrEmpty(g.Category) ? "(uncategorized)" : g.Category, _mutedStyle);
                lastCategory = g.Category;
            }
            var style = (_selectedGood == g.Name) ? _tabActiveStyle : _tabStyle;
            if (GUILayout.Button(g.DisplayName, style)) _selectedGood = g.Name;
        }
        GUILayout.EndScrollView();
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
        DrawWhyAllButton(_expandedProducers, vm.Producers.Select(p => "prod:" + p.Recipe.Name));
        GUILayout.EndHorizontal();
        foreach (var p in vm.Producers) DrawProductionPath(p);

        // Consumers
        GUILayout.Space(6);
        GUILayout.Label($"Consumed by {vm.Consumers.Count} recipes", _bodyStyle);
        foreach (var r in vm.Consumers.Take(20))
            GUILayout.Label($"   {r.DisplayName}  ({r.ProducedGood} ×{r.ProducedAmount})", _mutedStyle);

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
        }

        // Travel progress for the current (en-route) trader.
        if (label == "Current" && !t.IsInVillage)
        {
            var prog = LiveGameState.CurrentTraderTravelProgress();
            if (prog is float p)
                GUILayout.Label($"   travel: {p * 100f:0}% en route", _mutedStyle);
        }

        // Trader desires ranking (top picks by total settlement value).
        var info = label == "Current"
            ? LiveGameState.CurrentTrader()
            : LiveGameState.NextTrader();
        var desires = LiveGameState.RankTraderDesires(
            info, Catalog, isCurrent: label == "Current");
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
        GUILayout.Label("   wants — ranked by total value (price × stockpile)", _mutedStyle);
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

    private void DrawResolveSnapshot(ResolveSnapshot? r)
    {
        if (r is null) return;

        GUILayout.Space(6);
        GUILayout.Label(
            $"● live resolve: {r.Current:0.0} now · target {r.Target} · range {r.Min}–{r.Max}",
            _bodyStyle);

        // Bar: width 0..1 mapped from Min..Max for current; faint marker for target.
        var barRect = GUILayoutUtility.GetRect(0, 12, GUILayout.ExpandWidth(true));
        var span = Math.Max(1, r.Max - r.Min);
        var curT = Mathf.Clamp01((r.Current - r.Min) / span);
        var tgtT = Mathf.Clamp01(((float)r.Target - r.Min) / span);
        GUI.Box(barRect, GUIContent.none);
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
            GUILayout.Space(4);
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

        _ordersScroll = GUILayout.BeginScrollView(_ordersScroll, GUILayout.ExpandHeight(true));
        foreach (var o in sorted) DrawOrderCard(o);
        GUILayout.EndScrollView();
    }

    private void DrawOrderCard(LiveGameState.OrderInfo o)
    {
        GUILayout.BeginHorizontal();

        // Status marker.
        string marker = o.Tracked ? "◉" : (o.Picked ? "●" : "○");
        GUILayout.Label(marker, _badgeStyle, GUILayout.Width(24));

        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label(o.DisplayName, _bodyStyle);
        GUILayout.FlexibleSpace();
        if (o.ShouldBeFailable && o.TimeLeft > 0f)
        {
            var mins = Mathf.Max(0, Mathf.FloorToInt(o.TimeLeft / 60f));
            var secs = Mathf.Max(0, Mathf.FloorToInt(o.TimeLeft % 60f));
            var prefix = o.TimeLeft < 60f ? "⚠ " : "";
            GUILayout.Label($"{prefix}{mins}:{secs:00} left", _mutedStyle);
        }
        GUILayout.EndHorizontal();

        var status = new List<string>();
        if (!string.IsNullOrEmpty(o.Tier)) status.Add(o.Tier);
        if (o.Tracked) status.Add("tracked");
        else if (o.Picked) status.Add("picked");
        else status.Add("unpicked");
        if (o.RewardCategories.Count > 0)
            status.Add("value " + string.Join("+", o.RewardCategories));
        if (status.Count > 0)
            GUILayout.Label("   " + string.Join(" · ", status), _mutedStyle);

        if (o.Objectives.Count > 0)
        {
            foreach (var ob in o.Objectives)
            {
                var prefix = ob.Completed ? "   ✓ " : "   · ";
                GUILayout.Label(prefix + ob.Description, _mutedStyle);
            }
        }
        if (o.Rewards.Count > 0)
        {
            var rewardLine = string.Join(", ", o.Rewards.Select(r => r.DisplayName));
            GUILayout.Label(
                $"   reward (score {o.RewardScore:0}): {rewardLine}",
                _mutedStyle);
        }

        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    private void DrawSettingsTab()
    {
        GUILayout.Label("Settings", _h1Style);
        _settingsScroll = GUILayout.BeginScrollView(_settingsScroll, GUILayout.ExpandHeight(true));

        DrawSettingHeader("General");
        DrawBoolSetting(Config.ShowRecommendations,      "Show recommendations");
        DrawBoolSetting(Config.VisibleByDefault,         "Visible by default");
        DrawBoolSetting(Config.HideEmptyRecipeBuildings, "Hide empty-recipe buildings");

        DrawSettingHeader("Tabs");
        DrawBoolSetting(Config.ShowHomeTab,      "Home tab");
        DrawBoolSetting(Config.ShowBuildingTab,  "Building tab");
        DrawBoolSetting(Config.ShowGoodTab,      "Good tab");
        DrawBoolSetting(Config.ShowVillagersTab, "Villagers tab");
        DrawBoolSetting(Config.ShowOrdersTab,    "Orders tab");
        DrawBoolSetting(Config.ShowGladesTab,    "Glades tab");
        DrawBoolSetting(Config.ShowDraftTab,     "Cornerstone Draft tab");
        DrawBoolSetting(Config.ShowSettingsTab,  "Settings tab (this one)");

        GUILayout.Space(8);
        DrawSettingHeader("Hotkey");
        GUILayout.Label(
            $"   toggle: {Config.ToggleHotkey.Value}",
            _mutedStyle);
        GUILayout.Label(
            "   change in BepInEx config (the in-panel rebinder is not yet wired).",
            _mutedStyle);

        GUILayout.Space(8);
        DrawSettingHeader("Catalog");
        var src = Catalog.IsEmpty ? "(empty)" : $"game {Catalog.GameVersion}";
        GUILayout.Label(
            $"   {Catalog.Buildings.Count} buildings · {Catalog.Goods.Count} goods · {Catalog.Recipes.Count} recipes · {Catalog.Races.Count} races",
            _mutedStyle);
        GUILayout.Label($"   source: {src}", _mutedStyle);

        GUILayout.EndScrollView();
    }

    private void DrawSettingHeader(string label)
    {
        GUILayout.Space(4);
        GUILayout.Label(label, _bodyStyle);
    }

    private static void DrawBoolSetting(
        BepInEx.Configuration.ConfigEntry<bool> entry, string label)
    {
        var current = entry.Value;
        var next = GUILayout.Toggle(current, "  " + label);
        if (next != current) entry.Value = next;
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
        }

        GUILayout.Space(6);
        GUILayout.Label(
            "Tip: dangerous and forbidden glades carry stronger rewards but spawn worse forest mysteries. The game spawns event decisions when scouts enter; this tab will surface those once a settlement provides them.",
            _mutedStyle);

        GUILayout.EndScrollView();
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
        foreach (var o in vm.Options) DrawCornerstoneOption(o);
        DrawOwnedCornerstones(vm.Owned);
    }

    private void DrawOwnedCornerstones(IReadOnlyList<OwnedCornerstoneInfo> owned)
    {
        if (owned.Count == 0) return;
        GUILayout.Space(8);
        GUILayout.Label($"Currently owned — {owned.Count}", _bodyStyle);
        foreach (var o in owned)
        {
            GUILayout.Label($"   · {o.DisplayName}", _bodyStyle);
            if (!string.IsNullOrEmpty(o.Description))
                GUILayout.Label($"     {o.Description}", _mutedStyle);
        }
    }

    private void DrawCornerstoneOption(CornerstoneOption o)
    {
        GUILayout.BeginHorizontal();
        var marker = o.IsTopRanked && Config.ShowRecommendations.Value ? "★" : $"#{o.Rank}";
        GUILayout.Label(marker, _badgeStyle, GUILayout.Width(24));
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label(o.DisplayName, _bodyStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(o.Synergy.Format("0"), _bodyStyle);
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(o.Description))
            GUILayout.Label(o.Description, _mutedStyle);
        foreach (var c in o.Synergy.Components)
        {
            var line = $"   {c.Label}: {c.Value:0.##}";
            if (!string.IsNullOrEmpty(c.Note)) line += $"   {c.Note}";
            GUILayout.Label(line, _mutedStyle);
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
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
        if (_windowStyle != null) return;

        _windowStyle = new GUIStyle(GUI.skin.window) { padding = new RectOffset(10, 10, 22, 10) };
        _bodyStyle = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            alignment = TextAnchor.UpperLeft,
            fontSize = 12,
        };
        _h1Style = new GUIStyle(_bodyStyle) { fontSize = 14, fontStyle = FontStyle.Bold };
        _mutedStyle = new GUIStyle(_bodyStyle) { fontSize = 11 };
        _mutedStyle.normal.textColor = new Color(0.75f, 0.75f, 0.78f, 1f);
        _badgeStyle = new GUIStyle(_bodyStyle) { fontSize = 14, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        _tabStyle = new GUIStyle(GUI.skin.button) { fixedHeight = 24, fontSize = 12 };
        _tabActiveStyle = new GUIStyle(_tabStyle);
        _tabActiveStyle.normal.background = _tabStyle.active.background;
        _tabActiveStyle.normal.textColor  = Color.white;
        _tabActiveStyle.fontStyle         = FontStyle.Bold;
    }
}
