using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace StormGuide.CatalogTrim;

/// <summary>
/// Reads JSONLoader's Exported folder (e.g.
/// %userprofile%\AppData\LocalLow\Eremite Games\Against the Storm\JSONLoader\Exported)
/// and emits a trimmed StormGuide catalog into Resources/catalog/.
///
/// Usage:
///   catalog-trim &lt;exportDir&gt; &lt;outDir&gt;
///   catalog-trim                    # uses default paths
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented   = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters      = { new JsonStringEnumConverter() },
    };

    private static int Main(string[] args)
    {
        var exportDir = args.Length > 0
            ? args[0]
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "Eremite Games", "Against the Storm",
                "JSONLoader", "Exported");

        var outDir = args.Length > 1
            ? args[1]
            : Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "StormGuide", "Resources", "catalog");

        outDir = Path.GetFullPath(outDir);

        if (!Directory.Exists(exportDir))
        {
            Console.Error.WriteLine($"Export folder not found: {exportDir}");
            Console.Error.WriteLine("Run JSONLoader's export first:");
            Console.Error.WriteLine("  Options → Mods → JSONLoader → Export On Game Load = true → restart game.");
            return 2;
        }

        Directory.CreateDirectory(outDir);

        Console.WriteLine($"Export source: {exportDir}");
        Console.WriteLine($"Output target: {outDir}");

        var goods     = new List<TrimmedGood>();
        var races     = new List<TrimmedRace>();
        var buildings = new List<TrimmedBuilding>();
        var recipes   = new List<TrimmedRecipe>();
        var unclassified = 0;

        foreach (var path in Directory.EnumerateFiles(exportDir, "*.json", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith("_schema.json")) continue;

            JsonNode? root;
            try { using var fs = File.OpenRead(path); root = JsonNode.Parse(fs); }
            catch (Exception ex) { Console.Error.WriteLine($"  skip (parse): {name}: {ex.Message}"); continue; }
            if (root is not JsonObject obj) continue;

            // Classify by suffix first, then fall back to content sniffing.
            switch (Classify(lower, obj))
            {
                case Kind.Good:        if (TryGood(obj, out var g))     goods.Add(g); break;
                case Kind.Race:        if (TryRace(obj, out var r))     races.Add(r); break;
                case Kind.Building:    if (TryBuilding(obj, out var b)) buildings.Add(b); break;
                case Kind.Recipe:      if (TryRecipe(obj, out var rc))  recipes.Add(rc); break;
                default:               unclassified++; break;
            }
        }

        WriteJson(Path.Combine(outDir, "goods.json"),     goods);
        WriteJson(Path.Combine(outDir, "races.json"),     races);
        WriteJson(Path.Combine(outDir, "buildings.json"), buildings);
        WriteJson(Path.Combine(outDir, "recipes.json"),   recipes);
        WriteJson(Path.Combine(outDir, "meta.json"), new TrimmedMeta(
            GameVersion:   Environment.GetEnvironmentVariable("ATS_GAME_VERSION") ?? "unknown",
            ExportedAtUtc: DateTime.UtcNow.ToString("o")));

        Console.WriteLine();
        Console.WriteLine($"Goods:     {goods.Count}");
        Console.WriteLine($"Races:     {races.Count}");
        Console.WriteLine($"Buildings: {buildings.Count}");
        Console.WriteLine($"Recipes:   {recipes.Count}");
        Console.WriteLine($"Skipped (unclassified): {unclassified}");
        return 0;
    }

    private enum Kind { Unknown, Good, Race, Building, Recipe }

    private static Kind Classify(string lowerName, JsonObject obj)
    {
        if (lowerName.EndsWith("_good.json"))                            return Kind.Good;
        if (lowerName.EndsWith("_race.json"))                            return Kind.Race;
        if (lowerName.EndsWith("building.json"))                         return Kind.Building;
        if (lowerName.EndsWith("recipe.json"))                           return Kind.Recipe;

        // Content sniffing fallback.
        if (obj.ContainsKey("eatable") || obj.ContainsKey("tradingBuyValue"))      return Kind.Good;
        if (obj.ContainsKey("baseSpeed") && obj.ContainsKey("initialResolve"))     return Kind.Race;
        if (obj.ContainsKey("producedGood") || obj.ContainsKey("productionTime"))  return Kind.Recipe;
        if (obj.ContainsKey("workshopRecipes") || obj.ContainsKey("profession"))   return Kind.Building;
        return Kind.Unknown;
    }

    // ---- Good ----------------------------------------------------------------
    private static bool TryGood(JsonObject o, out TrimmedGood g)
    {
        var name = AsString(o["name"]);
        if (string.IsNullOrEmpty(name)) { g = default!; return false; }
        g = new TrimmedGood(
            Name:             name,
            DisplayName:      AsString(o["displayName"], name),
            Category:         AsString(o["category"]),
            IsEatable:        AsBool(o["eatable"]),
            EatingFullness:   AsDouble(o["eatingFullness"]),
            CanBeBurned:      AsBool(o["canBeBurned"]),
            BurningTime:      AsDouble(o["burningTime"]),
            TradingBuyValue:  AsDouble(o["tradingBuyValue"]),
            TradingSellValue: AsDouble(o["tradingSellValue"]),
            TradersBuying:    AsStringList(o["tradersBuyingThisGood"]),
            TradersSelling:   AsStringList(o["tradersSellingThisGood"]),
            Tags:             AsStringList(o["tags"]));
        return true;
    }

    // ---- Race ----------------------------------------------------------------
    private static bool TryRace(JsonObject o, out TrimmedRace r)
    {
        var name = AsString(o["name"]);
        if (string.IsNullOrEmpty(name)) { r = default!; return false; }
        var characteristics = new List<TrimmedCharacteristic>();
        if (o["characteristics"] is JsonArray arr)
        {
            foreach (var node in arr)
            {
                if (node is not JsonObject c) continue;
                characteristics.Add(new TrimmedCharacteristic(
                    BuildingTag:        AsString(c["buildingTag"]),
                    VillagerPerkEffect: AsString(c["villagerPerkEffect"]),
                    GlobalEffect:       AsString(c["globalEffect"]),
                    BuildingPerk:       AsString(c["buildingPerk"])));
            }
        }
        r = new TrimmedRace(
            Name:                        name,
            DisplayName:                 AsString(o["displayName"], name),
            BaseSpeed:                   AsDouble(o["baseSpeed"]),
            InitialResolve:              AsDouble(o["initialResolve"]),
            MinResolve:                  AsDouble(o["minResolve"]),
            MaxResolve:                  AsDouble(o["maxResolve"]),
            ResolvePositiveChangePerSec: AsDouble(o["resolvePositveChangePerSec"]),
            ResolveNegativeChangePerSec: AsDouble(o["resolveNegativeChangePerSec"]),
            HungerTolerance:             AsInt(o["hungerTolerance"]),
            Needs:                       AsStringList(o["needs"]),
            Characteristics:             characteristics);
        return true;
    }

    // ---- Building ------------------------------------------------------------
    private static bool TryBuilding(JsonObject o, out TrimmedBuilding b)
    {
        var name = AsString(o["name"]);
        if (string.IsNullOrEmpty(name)) { b = default!; return false; }
        b = new TrimmedBuilding(
            Name:        name,
            DisplayName: AsString(o["displayName"], name),
            Kind:        ClassifyBuildingKind(o),
            Category:    AsString(o["category"]),
            Profession:  AsString(o["profession"]),
            MaxBuilders: AsInt(o["maxBuilders"], 4),
            Tags:        AsStringList(o["tags"]),
            Recipes:     AsStringList(o["workshopRecipes"]));
        return true;
    }

    private static string ClassifyBuildingKind(JsonObject o)
    {
        // We don't have the source filename here; classify by shape heuristics.
        if (o.ContainsKey("workshopRecipes")) return "Workshop";
        if (o.ContainsKey("profession"))      return "Workshop";
        return "Other";
    }

    // ---- Recipe --------------------------------------------------------------
    private static bool TryRecipe(JsonObject o, out TrimmedRecipe r)
    {
        var name = AsString(o["name"]);
        if (string.IsNullOrEmpty(name)) { r = default!; return false; }
        // requiredGoods is array of { goods: [{ good, amount }] }
        var slots = new List<TrimmedRecipeSlot>();
        if (o["requiredGoods"] is JsonArray reqArr)
        {
            foreach (var slotNode in reqArr)
            {
                if (slotNode is not JsonObject slotObj) continue;
                var options = new List<TrimmedGoodAmount>();
                if (slotObj["goods"] is JsonArray gArr)
                {
                    foreach (var gn in gArr)
                    {
                        if (gn is not JsonObject g) continue;
                        var goodName = AsString(g["good"]);
                        if (string.IsNullOrEmpty(goodName)) continue;
                        options.Add(new TrimmedGoodAmount(goodName, AsInt(g["amount"])));
                    }
                }
                if (options.Count > 0) slots.Add(new TrimmedRecipeSlot(options));
            }
        }
        r = new TrimmedRecipe(
            Name:           name,
            DisplayName:    AsString(o["displayName"], name),
            Grade:          AsString(o["grade"], "One"),
            ProducedGood:   AsString(o["producedGood"]),
            ProducedAmount: AsInt(o["producedAmount"], 1),
            ProductionTime: AsDouble(o["productionTime"], 1.0),
            Tags:           AsStringList(o["tags"]),
            RequiredGoods:  slots,
            Buildings:      AsStringList(o["buildings"]));
        return true;
    }

    // ---- helpers -------------------------------------------------------------
    private static string AsString(JsonNode? n, string fallback = "")
    {
        if (n is null) return fallback;
        // Localized fields can come through as objects (e.g. {"en": "Bakery", "pl": "..."}).
        // Unwrap those by preferring "en" if present, otherwise the first string value.
        if (n is JsonObject obj)
        {
            if (obj["en"] is JsonValue jvEn && jvEn.TryGetValue<string>(out var en)) return en;
            foreach (var kv in obj)
                if (kv.Value is JsonValue jv && jv.TryGetValue<string>(out var s)) return s;
            return fallback;
        }
        if (n is JsonValue v && v.TryGetValue<string>(out var str)) return str;
        return fallback;
    }

    private static bool AsBool(JsonNode? n, bool fallback = false)
    {
        if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        return fallback;
    }

    private static double AsDouble(JsonNode? n, double fallback = 0)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<int>(out var i))    return i;
        }
        return fallback;
    }

    private static int AsInt(JsonNode? n, int fallback = 0)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i))    return i;
            if (v.TryGetValue<double>(out var d)) return (int)d;
        }
        return fallback;
    }

    private static IReadOnlyList<string> AsStringList(JsonNode? n)
    {
        if (n is JsonArray arr)
        {
            var list = new List<string>(arr.Count);
            foreach (var item in arr)
            {
                var s = AsString(item);
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list;
        }
        var single = AsString(n);
        return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { single };
    }

    private static void WriteJson<T>(string path, T value)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(value, WriteOpts));
        Console.WriteLine($"  wrote {Path.GetFileName(path)}");
    }
}

// ---------- Trimmed DTOs (independent of the plugin to avoid coupling) -------
internal sealed record TrimmedMeta(string GameVersion, string ExportedAtUtc);

internal sealed record TrimmedGood(
    string Name, string DisplayName, string Category,
    bool IsEatable, double EatingFullness, bool CanBeBurned, double BurningTime,
    double TradingBuyValue, double TradingSellValue,
    IReadOnlyList<string> TradersBuying, IReadOnlyList<string> TradersSelling,
    IReadOnlyList<string> Tags);

internal sealed record TrimmedCharacteristic(
    string BuildingTag, string VillagerPerkEffect, string GlobalEffect, string BuildingPerk);

internal sealed record TrimmedRace(
    string Name, string DisplayName, double BaseSpeed,
    double InitialResolve, double MinResolve, double MaxResolve,
    double ResolvePositiveChangePerSec, double ResolveNegativeChangePerSec,
    int HungerTolerance,
    IReadOnlyList<string> Needs,
    IReadOnlyList<TrimmedCharacteristic> Characteristics);

internal sealed record TrimmedBuilding(
    string Name, string DisplayName, string Kind, string Category, string Profession,
    int MaxBuilders, IReadOnlyList<string> Tags, IReadOnlyList<string> Recipes);

internal sealed record TrimmedGoodAmount(string Good, int Amount);
internal sealed record TrimmedRecipeSlot(IReadOnlyList<TrimmedGoodAmount> Options);
internal sealed record TrimmedRecipe(
    string Name, string DisplayName, string Grade,
    string ProducedGood, int ProducedAmount, double ProductionTime,
    IReadOnlyList<string> Tags,
    IReadOnlyList<TrimmedRecipeSlot> RequiredGoods,
    IReadOnlyList<string> Buildings);
