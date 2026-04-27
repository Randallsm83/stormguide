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
[BepInDependency("ATS_API")]
public sealed class StormGuidePlugin : BaseUnityPlugin
{
    public const string PluginGuid    = "stormguide";
    public const string PluginName    = "StormGuide";
    public const string PluginVersion = "0.0.1";

    internal static ManualLogSource Log { get; private set; } = null!;
    internal static StormGuidePlugin Instance { get; private set; } = null!;
    internal static Catalog       Catalog { get; private set; } = Catalog.Empty;
    internal static PluginConfig? Cfg     { get; private set; }

    private Harmony?   _harmony;
    private GameObject? _panelHost;

    private void Awake()
    {
        Instance = this;
        Log = Logger;

        Log.LogInfo($"{PluginName} {PluginVersion} loading…");

        Cfg = new PluginConfig(Config);
        Catalog = StaticCatalog.Load(Log);

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll(typeof(StormGuidePlugin).Assembly);

        SpawnPanel();

        Log.LogInfo($"{PluginName} loaded. Patches: {_harmony.GetPatchedMethods().Count()}.");
    }

    private void SpawnPanel()
    {
        _panelHost = new GameObject("StormGuide.PanelHost");
        DontDestroyOnLoad(_panelHost);
        var panel = _panelHost.AddComponent<SidePanel>();
        panel.Config  = Cfg!;
        panel.Catalog = Catalog;
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (_panelHost != null) Destroy(_panelHost);
        Log.LogInfo($"{PluginName} unloaded.");
    }
}
