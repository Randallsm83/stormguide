# StormGuide — Agent Guide
BepInEx plugin for Against the Storm. C# / `netstandard2.0` / Unity 2021.3.x / Mono. Decision-time overlay; never mutates game state.
## Layout
```
StormGuide/                (the plugin assembly)
  Configuration/           BepInEx config bindings (PluginConfig)
  Data/                    LiveGameState (game-API wrapper) + StaticCatalog + TtlCache
  Domain/                  Pure DTOs: Catalog, *Info, *ViewModel, Score, FlowRow…
  Providers/               Pure functions: Catalog + lookups → ViewModel (Building, Good, Villager, CornerstoneDraft)
  UI/SidePanel.cs          Single IMGUI MonoBehaviour. Renders all tabs, the alerts strip, resize/reset
  Resources/catalog/*.json Embedded trimmed game catalog
  StormGuidePlugin.cs      Entry point: BaseUnityPlugin, spawns SidePanel host
tools/CatalogTrim/         net10 console: JSONLoader export → trimmed catalog
research/                  Decompiled game source (gitignored). NOTES.md has hook map.
```
## Build / deploy
```
dotnet build StormGuide/StormGuide.csproj -c Release
```
Post-build copies the DLL into the r2modman profile's `BepInEx/plugins/StormGuide/`. Paths come from `Directory.Build.props` (`StormPath`, `BepInExPath`).
## Hard invariants
1. **Read-only.** No Harmony patches mutate state. We `Subscribe` to UniRx properties and read service accessors; never `Pick`, `SetProfession`, `Reroll`, etc.
2. **Transparent recommendations.** Every `Score` carries `Components` (label/value/note rows) that the UI renders below the headline number. If a feature can't show its math, it doesn't ship.
3. **Defensive live access.** Every `LiveGameState` accessor returns `null` / `0` / empty on missing services and try/catches around the game-side call. The plugin must never crash if a game type is renamed in a patch.
4. **No prescriptive UI.** Badges/ranks/sorts are fine; auto-clicks, modal nudges, and hidden non-recommended options are not. `Config.ShowRecommendations` toggles all `★`/ranked highlights off cleanly.
## Where to add a feature
- **New tab** — add a value to `SidePanel.Tab`, a `bool` config in `PluginConfig`, a draw method, and a hide-check entry in `IsTabVisible`.
- **New live-state read** — add a static method on `LiveGameState` returning a small DTO. Wrap the game-side call in try/catch; never let exceptions escape.
- **New recommendation** — add a `Provider.For(...)` overload taking the new lookup as an optional `Func<...>`. Return a `Score` whose `Components` explain it row-by-row.
- **Hot-path call** (called every frame from `OnGUI`) — wrap in `TtlCache<T>` (see existing `_alertsCache`, `_summaryCache`).
## Game API hook map
Curated in `research/NOTES.md`. Highlights:
- `GameController.Instance.GameServices` is the root.
- `GameInputService.PickedObject` (UniRx) drives building selection.
- `GameBlackboardService.OnRewardsPopupRequested` drives the Cornerstone Draft auto-switch.
- `BuildingsService.Buildings` is `Dictionary<int, Building>`. Iterate `.Values`.
- `ProductionBuilding.Workers : int[]`, `IsIdle : bool`, `GetCurrentRecipeFor(i)`.
- `CornerstonesService.GetCurrentPick().options : List<string>` (effect IDs); resolve via `GameModelService.GetEffect(id)`.
- `StateService.Cornerstones.activeCornerstones : List<string>` for owned.
- `EffectModel.usabilityTags : ModelTag[]` for structural synergy.
## Catalog regeneration
After a game patch, recipes/goods/buildings can drift. To regen:
1. Install `JSONLoader` mod, set `Export On Game Load = true`, launch AtS once.
2. `dotnet run --project tools/CatalogTrim` (defaults read the standard JSONLoader export path; emits to `StormGuide/Resources/catalog/`).
3. Rebuild.
A handful of the game's exported JSON files are malformed; the trim tool logs and skips them.
## Decompiling for new symbols
```
ilspycmd -p -o research/AssemblyCSharp <path-to>/Assembly-CSharp.dll
```
Then `grep` under `research/AssemblyCSharp` for the type/field. The decompiled tree is gitignored.
## Feature surface (cheat-sheet)
What the panel currently shows, by tab. Update when adding/removing surfaces so future agents don't have to spelunk `SidePanel.cs`.
- **Home** — village summary (pop, homeless, per-race resolve), trade (current/next trader, top-1 desire), idle workshops top-3, goods at risk top-5, race needs unmet, orders summary (`N picked, M tracked`, time-critical line), forest exploration %, owned cornerstones (count + first-3).
- **Building** — search (matches name + tags), clear-selection, tag chips (click to filter list), best-workers (race-fit by perk weight), recipe cards with `▸ why` + `why × all` toggle, draining-input flag, idle banner.
- **Good** — search, clear-selection, flow line + breakdown, production paths with `▸ why` + `why × all`, consumers, race needs, current/next trader desires (top-N total + per-row), live currency, trader-rotation jump links.
- **Villagers** — village summary header, race list, race detail with live resolve bar (current/target tick), top resolve contributors, race characteristics, best-fit workplaces.
- **Orders** — active orders sorted (tracked → picked → time-pressure → name), tier badge (bronze/silver/gold pill), failable countdown (red/amber/muted), objectives with `✓`/progress-bar, reward score + categories, pick-options ranking on unpicked orders.
- **Glades** — explored %, dangerous/forbidden counts, reward-chase alerts, danger-level distribution chart.
- **Draft** — cornerstone draft auto-popup; per-option synergy with breakdown components (tag matches, owned-stack, resolve-shaped, total-buildings).
- **Settings** (⚙) — reflection-driven tab toggles, persisted why-all flags, hide-empty-recipe filter, hotkey rebinder, catalog reload, embedded README/AGENTS docs.
- **Diagnostics** (⚙?, off by default) — 200-line ring buffer of plugin log lines (BepInEx ILogListener filtered by source).
- **Footer** — catalog source + plugin version + active hotkey.
- **Hotkeys** — toggle hotkey (G default, rebindable), Ctrl+1…9 jump-to-tab while panel is visible.
## Don'ts
- Don't add `System.Math.Clamp` — not in netstandard2.0. Use `UnityEngine.Mathf.Clamp01` or manual `Math.Min/Max`.
- Don't reference `EffectModel.Description` without referencing `Sirenix.Serialization` (transitive dep already wired in csproj).
- Don't iterate `BuildingsService.Buildings` directly — it's a `Dictionary<int, Building>`; use `.Values`.
- Don't auto-launch the game from PowerShell scripts. r2modman owns the launch and we don't replicate its preloader injection reliably.
