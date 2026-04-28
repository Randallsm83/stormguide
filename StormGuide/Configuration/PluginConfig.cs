using BepInEx.Configuration;
using UnityEngine;

namespace StormGuide.Configuration;

/// <summary>
/// Centralised access to all BepInEx config bindings. The ATS_API mod menu
/// auto-renders these so they're player-editable in-game.
/// </summary>
public sealed class PluginConfig
{
    private const string SectionGeneral = "General";
    private const string SectionUI      = "UI";
    private const string SectionAdvice  = "Recommendations";

    public ConfigEntry<KeyboardShortcut> ToggleHotkey { get; }
    public ConfigEntry<bool>             VisibleByDefault { get; }
    public ConfigEntry<Vector2>          PanelPosition { get; }
    public ConfigEntry<Vector2>          PanelSize { get; }
    public ConfigEntry<bool>             ShowRecommendations { get; }
    public ConfigEntry<bool>             ShowHomeTab { get; }
    public ConfigEntry<bool>             ShowBuildingTab { get; }
    public ConfigEntry<bool>             ShowGoodTab { get; }
    public ConfigEntry<bool>             ShowVillagersTab { get; }
    public ConfigEntry<bool>             ShowOrdersTab { get; }
    public ConfigEntry<bool>             ShowGladesTab { get; }
    public ConfigEntry<bool>             ShowDraftTab { get; }
    public ConfigEntry<bool>             ShowSettingsTab { get; }
    public ConfigEntry<bool>             ShowDiagnosticsTab { get; }
    public ConfigEntry<bool>             ShowEmbarkTab { get; }
    public ConfigEntry<bool>             HideEmptyRecipeBuildings { get; }
    public ConfigEntry<bool>             CompactMode { get; }
    public ConfigEntry<bool>             WhyAllRecipes { get; }
    public ConfigEntry<bool>             WhyAllProducers { get; }

    public ConfigEntry<string>           ActiveTab { get; }
    public ConfigEntry<string>           LastSelectedBuilding { get; }
    public ConfigEntry<string>           LastSelectedGood { get; }
    public ConfigEntry<string>           LastSelectedRace { get; }
    public ConfigEntry<string>           LastBuildingSearch { get; }
    public ConfigEntry<string>           LastGoodSearch { get; }
    public ConfigEntry<string>           PinnedRecipes { get; }
    public ConfigEntry<bool>             CompactLists { get; }
    public ConfigEntry<string>           LastCatalogHash { get; }
    public ConfigEntry<float>            GoodsAtRiskThresholdMinutes { get; }
    public ConfigEntry<string>           RaceRatioTargets { get; }
    public ConfigEntry<string>           CornerstonePickHistory { get; }
    public ConfigEntry<string>           PinPresets { get; }
    public ConfigEntry<string>           MarkedStoppedRecipes { get; }
    public ConfigEntry<string>           MarkedPriorityRecipes { get; }
    public ConfigEntry<string>           PinnedChaseModel { get; }
    public ConfigEntry<string>           TraderVisitArchive { get; }
    public ConfigEntry<string>           CornerstoneHistorySearch { get; }

    public PluginConfig(ConfigFile cfg)
    {
        // Register Vector2 type converter so KeyboardShortcut isn't the only
        // structured type that round-trips. (BepInEx does not ship one.)
        if (!TomlTypeConverter.CanConvert(typeof(Vector2)))
        {
            TomlTypeConverter.AddConverter(typeof(Vector2), new TypeConverter
            {
                ConvertToObject = (s, _) =>
                {
                    var parts = s.Split(',');
                    if (parts.Length != 2 ||
                        !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                        !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y))
                        return Vector2.zero;
                    return new Vector2(x, y);
                },
                ConvertToString = (o, _) =>
                {
                    var v = (Vector2)o;
                    return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.###},{1:0.###}", v.x, v.y);
                },
            });
        }

        ToggleHotkey = cfg.Bind(
            SectionGeneral, "Toggle Hotkey",
            // F8 chosen because vanilla AtS + the common mod set (More
            // Hotkeys, TimerHUD, Workplace Overhaul, etc.) leave it free.
            // 'G' was a poor default — colliding with vanilla bindings
            // meant the panel never toggled.
            new KeyboardShortcut(KeyCode.F8),
            "Key to show/hide the StormGuide side panel.");

        VisibleByDefault = cfg.Bind(
            SectionGeneral, "Visible By Default",
            false,
            "If true, the panel is shown immediately when a settlement loads. Otherwise it waits for the toggle hotkey.");

        PanelPosition = cfg.Bind(
            SectionUI, "Panel Position",
            new Vector2(40, 80),
            "Top-left screen position (pixels) of the panel. Updated automatically as you drag the panel.");

        PanelSize = cfg.Bind(
            SectionUI, "Panel Size",
            new Vector2(420, 480),
            "Width/height of the panel in pixels.");

        ShowRecommendations = cfg.Bind(
            SectionAdvice, "Show Recommendations",
            true,
            "Show ranked recommendations and badges. When false, all data is shown without scoring/highlights.");

        ShowHomeTab      = cfg.Bind(SectionUI, "Tab · Home",      true, "Show the Home / dashboard tab.");
        ShowBuildingTab  = cfg.Bind(SectionUI, "Tab · Building",  true, "Show the Building tab.");
        ShowGoodTab      = cfg.Bind(SectionUI, "Tab · Good",      true, "Show the Good tab.");
        ShowVillagersTab = cfg.Bind(SectionUI, "Tab · Villagers", true, "Show the Villagers tab.");
        ShowOrdersTab    = cfg.Bind(SectionUI, "Tab · Orders",    true, "Show the Orders tab (active reputation orders).");
        ShowGladesTab    = cfg.Bind(SectionUI, "Tab · Glades",    true, "Show the Glades tab (forest exploration summary).");
        ShowDraftTab     = cfg.Bind(SectionUI, "Tab · Draft",     true, "Show the Cornerstone Draft tab.");
        ShowSettingsTab  = cfg.Bind(SectionUI, "Tab · Settings",  true, "Show the in-panel Settings tab.");
        ShowDiagnosticsTab = cfg.Bind(SectionUI, "Tab · Diagnostics", false,
            "Show the in-panel Diagnostics tab (recent plugin log lines).");
        ShowEmbarkTab    = cfg.Bind(SectionUI, "Tab \u00b7 Embark",    true,
            "Show the Embark planner tab (pre-settlement helper: race comparison, starting-goods overlap, cornerstone-tag leverage).");

        HideEmptyRecipeBuildings = cfg.Bind(SectionUI, "Hide empty-recipe buildings", true,
            "Hide buildings that have no recipes from the Building tab list.");
        CompactMode = cfg.Bind(SectionUI, "Compact Mode", false,
            "Use a smaller-font/tighter layout. Useful on small displays.");

        WhyAllRecipes   = cfg.Bind(SectionUI, "Expand all recipe reasoning",   false,
            "Persistent state for the Building tab “why × all” toggle.");
        WhyAllProducers = cfg.Bind(SectionUI, "Expand all producer reasoning", false,
            "Persistent state for the Good tab “why × all” toggle.");

        ActiveTab            = cfg.Bind(SectionUI, "Active Tab",            "Home",
            "Last-active tab. Persisted automatically.");
        LastSelectedBuilding = cfg.Bind(SectionUI, "Last Selected Building", "",
            "Last-selected building model name. Persisted automatically.");
        LastSelectedGood     = cfg.Bind(SectionUI, "Last Selected Good",     "",
            "Last-selected good model name. Persisted automatically.");
        LastSelectedRace     = cfg.Bind(SectionUI, "Last Selected Race",     "",
            "Last-selected race model name. Persisted automatically.");
        LastBuildingSearch   = cfg.Bind(SectionUI, "Last Building Search",   "",
            "Last value of the Building tab search box. Persisted automatically.");
        LastGoodSearch       = cfg.Bind(SectionUI, "Last Good Search",       "",
            "Last value of the Good tab search box. Persisted automatically.");
        PinnedRecipes        = cfg.Bind(SectionUI, "Pinned Recipes",         "",
            "Semicolon-separated list of \"buildingModel|recipeModel\" pinned to the Home tab.");
        CompactLists         = cfg.Bind(SectionUI, "Compact Lists",          false,
            "Use a tighter row height for the Building/Good list scrollers. Independent of Compact Mode.");
        LastCatalogHash      = cfg.Bind(SectionGeneral, "Last Catalog Hash", "",
            "SHA-1 of the embedded catalog last seen by this install. Used to detect catalog updates.");
        GoodsAtRiskThresholdMinutes = cfg.Bind(SectionAdvice, "Goods At Risk Threshold (min)", 5f,
            "Runway threshold below which a good is flagged as at-risk. Lower = sooner alerts.");
        RaceRatioTargets     = cfg.Bind(SectionAdvice, "Race Ratio Targets",  "",
            "Comma-separated 'race=pct' pairs (e.g. 'beaver=30,human=40'). Drift > 10% flags on Home.");
        CornerstonePickHistory = cfg.Bind(SectionAdvice, "Cornerstone Pick History", "",
            "Semicolon-separated cornerstone ids the player has picked across runs (rolling 50). Used as a tie-breaker by the synergy ranker.");
        PinPresets = cfg.Bind(SectionUI, "Pin Presets", "",
            "Named pin presets in the form 'name=building|recipe,building|recipe;name2=...'. Managed via the Settings tab.");
        MarkedStoppedRecipes = cfg.Bind(SectionUI, "Marked Stopped Recipes", "",
            "Comma-separated recipe model names the player has flagged as stopped (UI-only marker).");
        MarkedPriorityRecipes = cfg.Bind(SectionUI, "Marked Priority Recipes", "",
            "Comma-separated recipe model names the player has flagged as haul-priority (UI-only marker).");
        PinnedChaseModel = cfg.Bind(SectionUI, "Pinned Chase Model", "",
            "Model name of a glade reward chase pinned to Home. Empty = none.");
        TraderVisitArchive = cfg.Bind(SectionUI, "Trader Visit Archive", "",
            "Semicolon-separated archive of past trader visits. Format: 'trader|good=val,good=val,good=val;...'. Rolling 20 entries.");
        CornerstoneHistorySearch = cfg.Bind(SectionUI, "Cornerstone History Search", "",
            "Last text filter applied to the Draft tab's previously-drafted list. Persists between sessions.");
    }
}
