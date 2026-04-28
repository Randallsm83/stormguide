using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using StormGuide.Configuration;
using StormGuide.Data;
using StormGuide.Domain;
using StormGuide.UI;
using UnityEngine;

namespace StormGuide;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
// ATS_API_Devs-API registers its BepInPlugin with GUID "API" (the friendly
// name is also "API"). Using the wrong GUID here will cause BepInEx to skip
// our plugin with "missing dependencies: <guid>".
[BepInDependency("API", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class StormGuidePlugin : BaseUnityPlugin
{
    public const string PluginGuid    = "stormguide";
    public const string PluginName    = "StormGuide";
    public const string PluginVersion = "0.0.1";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static StormGuidePlugin Instance { get; private set; } = null!;
    internal static Catalog       Catalog { get; private set; } = Catalog.Empty;
    internal static PluginConfig? Cfg     { get; private set; }
    internal static LogCapture?   LogTail { get; private set; }

    private Harmony?    _harmony;
    private GameObject? _panelHost;
    private SidePanel?  _panel;

    /// <summary>
    /// Reloads the embedded static catalog and pushes it into the live panel.
    /// Used by the in-panel Settings tab so the catalog can be reread without
    /// restarting the game.
    /// </summary>
    internal static void ReloadCatalog()
    {
        if (Instance == null) return;
        Catalog = StaticCatalog.Load(Log);
        if (Instance._panel != null) Instance._panel.Catalog = Catalog;
        Log.LogInfo("StormGuide catalog reloaded.");
    }

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        // Capture our own log lines for the Diagnostics tab. Registering the
        // listener with the global pipeline is cheap; we filter by source name
        // so we never see other plugins' chatter.
        LogTail = new LogCapture(filterSource: PluginName, capacity: 200);
        BepInEx.Logging.Logger.Listeners.Add(LogTail);

        Log.LogInfo($"{PluginName} {PluginVersion} loading…");

        Cfg = new PluginConfig(Config);
        Catalog = StaticCatalog.Load(Log);
        PruneStaleSelections();

        // TODO(localization): wire StormGuide.Domain.Localization.LiveLookup
        // to the AtS in-game text-service once the API surface is verified
        // via dnSpy. Until then, Localization falls back to the catalog's
        // embedded English DisplayName, which is correct for v1 but won't
        // pick up the player's selected locale or any modded translations.
        // Expected shape: Localization.LiveLookup = key => textService.Get(key);

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(StormGuidePlugin).Assembly);

        SpawnPanel();

        Log.LogInfo(
            $"{PluginName} loaded. Patches: {_harmony.GetPatchedMethods().Count()}. " +
            $"Toggle hotkey: {Cfg!.ToggleHotkey.Value} " +
            $"(visible by default: {Cfg.VisibleByDefault.Value}).");
    }

    private void SpawnPanel()
    {
        _panelHost = new GameObject("StormGuide.PanelHost");
        DontDestroyOnLoad(_panelHost);
        _panel = _panelHost.AddComponent<SidePanel>();
        _panel.Config  = Cfg!;
        _panel.Catalog = Catalog;
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (_panelHost != null) Destroy(_panelHost);
        if (LogTail != null) BepInEx.Logging.Logger.Listeners.Remove(LogTail);
        Log.LogInfo($"{PluginName} unloaded.");
    }

    /// <summary>
    /// Clears persisted last-selected building/good/race entries that are no
    /// longer present in the loaded catalog. Prevents broken state when a mod
    /// adds or removes content between sessions.
    /// </summary>
    private void PruneStaleSelections()
    {
        if (Cfg == null) return;
        if (!string.IsNullOrEmpty(Cfg.LastSelectedBuilding.Value) &&
            !Catalog.Buildings.ContainsKey(Cfg.LastSelectedBuilding.Value))
        {
            Log.LogWarning($"Stale building selection '{Cfg.LastSelectedBuilding.Value}' " +
                            "is not in the current catalog — clearing.");
            Cfg.LastSelectedBuilding.Value = "";
        }
        if (!string.IsNullOrEmpty(Cfg.LastSelectedGood.Value) &&
            !Catalog.Goods.ContainsKey(Cfg.LastSelectedGood.Value))
        {
            Log.LogWarning($"Stale good selection '{Cfg.LastSelectedGood.Value}' " +
                            "is not in the current catalog — clearing.");
            Cfg.LastSelectedGood.Value = "";
        }
        if (!string.IsNullOrEmpty(Cfg.LastSelectedRace.Value) &&
            !Catalog.Races.ContainsKey(Cfg.LastSelectedRace.Value))
        {
            Log.LogWarning($"Stale race selection '{Cfg.LastSelectedRace.Value}' " +
                            "is not in the current catalog — clearing.");
            Cfg.LastSelectedRace.Value = "";
        }
    }
}
