using System.IO;
using System.Reflection;
using BepInEx.Logging;
using Newtonsoft.Json;
using StormGuide.Domain;

namespace StormGuide.Data;

/// <summary>
/// Loads the bundled static catalog from embedded JSON resources.
/// Always returns a usable <see cref="Catalog"/>; if data is missing, returns
/// <see cref="Catalog.Empty"/> and logs a warning so the plugin can run in
/// degraded (live-only) mode.
/// </summary>
public static class StaticCatalog
{
    private const string ResourcePrefix = "StormGuide.Resources.catalog.";

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling     = NullValueHandling.Ignore,
    };

    public static Catalog Load(ManualLogSource log)
    {
        var asm = typeof(StaticCatalog).Assembly;

        var goods     = LoadDict<GoodInfo>(asm,     "goods.json",     g => g.Name,     log);
        var races     = LoadDict<RaceInfo>(asm,     "races.json",     r => r.Name,     log);
        var buildings = LoadDict<BuildingInfo>(asm, "buildings.json", b => b.Name,     log);
        var recipes   = LoadDict<RecipeInfo>(asm,   "recipes.json",   r => r.Name,     log);
        var meta      = LoadMeta(asm, log);

        var catalog = new Catalog
        {
            Goods         = goods,
            Races         = races,
            Buildings     = buildings,
            Recipes       = recipes,
            GameVersion   = meta?.GameVersion   ?? "unknown",
            ExportedAtUtc = meta?.ExportedAtUtc ?? "",
        };

        if (catalog.IsEmpty)
        {
            log.LogWarning(
                "Static catalog is empty. Run the CatalogTrim tool against a JSONLoader export " +
                "to populate StormGuide/Resources/catalog/, then rebuild the plugin.");
        }
        else
        {
            log.LogInfo(
                $"Catalog loaded: {goods.Count} goods, {races.Count} races, " +
                $"{buildings.Count} buildings, {recipes.Count} recipes " +
                $"(game {catalog.GameVersion}, exported {catalog.ExportedAtUtc}).");
        }

        return catalog;
    }

    private static IReadOnlyDictionary<string, T> LoadDict<T>(
        Assembly asm, string fileName, Func<T, string> keySelector, ManualLogSource log)
    {
        var resourceName = ResourcePrefix + fileName;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return new Dictionary<string, T>();

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, T>();

        try
        {
            var list = JsonConvert.DeserializeObject<List<T>>(json, JsonSettings);
            if (list == null) return new Dictionary<string, T>();
            var dict = new Dictionary<string, T>(list.Count);
            foreach (var item in list)
            {
                var key = keySelector(item);
                if (!string.IsNullOrEmpty(key)) dict[key] = item;
            }
            return dict;
        }
        catch (JsonException ex)
        {
            log.LogError($"Failed to parse {fileName}: {ex.Message}");
            return new Dictionary<string, T>();
        }
    }

    private static CatalogMeta? LoadMeta(Assembly asm, ManualLogSource log)
    {
        var resourceName = ResourcePrefix + "meta.json";
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        try { return JsonConvert.DeserializeObject<CatalogMeta>(reader.ReadToEnd(), JsonSettings); }
        catch (JsonException ex) { log.LogError($"Failed to parse meta.json: {ex.Message}"); return null; }
    }

    private sealed class CatalogMeta
    {
        public string GameVersion   { get; set; } = "unknown";
        public string ExportedAtUtc { get; set; } = "";
    }
}
