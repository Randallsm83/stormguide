namespace StormGuide.Domain;

/// <summary>
/// Single-source-of-truth adapter for resolving display strings from catalog
/// model keys (good / race / building / recipe). Resolution order:
///
/// <list type="number">
///   <item><description>The live game text service (via <see cref="LiveLookup"/>),
///         when wired up by <c>StormGuidePlugin</c> at startup.</description></item>
///   <item><description>The embedded catalog's <c>DisplayName</c> (English-only,
///         from JSONLoader's last export).</description></item>
///   <item><description>The raw model key, as a last resort.</description></item>
/// </list>
///
/// Pure (no Unity / BepInEx / game refs) so it sits in <c>Domain/</c> alongside
/// the rest of the layer that the test project compiles. The plan filed this as
/// <c>StormGuide/Data/Localization.cs</c>; it lives here instead so the fallback
/// logic is unit-tested. <c>Data/</c> still owns the eventual game-side wiring
/// (assigning <see cref="LiveLookup"/> from <c>LiveGameState</c>).
///
/// Score breakdowns and structural UI labels (e.g. "Settings", "Diagnostics",
/// section headers) intentionally stay English-only - they're authored against
/// AGENTS.md hard invariants, not catalog content. Only catalog-derived strings
/// flow through this adapter.
/// </summary>
public static class Localization
{
    /// <summary>
    /// Optional live lookup. Returns the localised string for a given catalog
    /// model key when the game's text service is reachable, or <c>null</c> /
    /// empty to fall through to the embedded catalog DisplayName.
    ///
    /// Wired by <c>StormGuidePlugin.Awake()</c> once the text service is
    /// verified. Until then this stays <c>null</c> and every caller transparently
    /// gets the embedded English; that's a feature, not a bug - it means the
    /// adapter is safe to call from anywhere without conditional checks and
    /// the build never blocks on game-side wiring.
    /// </summary>
    public static Func<string, string?>? LiveLookup { get; set; }

    /// <summary>Resolve a good's display name.</summary>
    public static string GoodName(string modelKey, Catalog catalog) =>
        Resolve(modelKey,
            catalog.Goods.TryGetValue(modelKey, out var gi) ? gi.DisplayName : null);

    /// <summary>Resolve a building's display name.</summary>
    public static string BuildingName(string modelKey, Catalog catalog) =>
        Resolve(modelKey,
            catalog.Buildings.TryGetValue(modelKey, out var bi) ? bi.DisplayName : null);

    /// <summary>Resolve a race's display name.</summary>
    public static string RaceName(string modelKey, Catalog catalog) =>
        Resolve(modelKey,
            catalog.Races.TryGetValue(modelKey, out var ri) ? ri.DisplayName : null);

    /// <summary>Resolve a recipe's display name.</summary>
    public static string RecipeName(string modelKey, Catalog catalog) =>
        Resolve(modelKey,
            catalog.Recipes.TryGetValue(modelKey, out var ri) ? ri.DisplayName : null);

    private static string Resolve(string modelKey, string? catalogDisplay)
    {
        if (string.IsNullOrEmpty(modelKey)) return modelKey ?? "";

        // 1. Live lookup wins when available. Any throw or null/empty result
        // falls through; we never let a translation lookup take down the UI.
        try
        {
            var live = LiveLookup?.Invoke(modelKey);
            if (!string.IsNullOrWhiteSpace(live)) return live!;
        }
        catch { /* swallow - translation must never crash. */ }

        // 2. Embedded catalog DisplayName (the JSONLoader-exported English).
        if (!string.IsNullOrWhiteSpace(catalogDisplay)) return catalogDisplay!;

        // 3. Last resort: the raw model key, so the UI never renders blank.
        return modelKey;
    }
}
