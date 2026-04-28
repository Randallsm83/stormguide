# StormGuide — Agent Guide

BepInEx 5 plugin for Against the Storm. C# / `netstandard2.0` / Unity 2021.3.x / Mono. Decision-time overlay; **never mutates game state**.

## Solution layout

```
StormGuide.slnx               solution (slnx, not sln) — opens all three projects
StormGuide/                   plugin assembly (netstandard2.0)
  Configuration/              BepInEx config bindings (PluginConfig)
  Data/                       LiveGameState (game-API wrapper) + StaticCatalog + TtlCache
                              + CacheBudget (TTL/window constants) + LogCapture
  Domain/                     pure DTOs + math: Catalog, *Info, *ViewModel, Score,
                              FlowRow, VillageSummary, PerfRing, EmbarkScoring,
                              Localization (catalog-key → display-name adapter)…
                              (no game refs)
  Providers/                  pure functions: Catalog + lookups → ViewModel
                              (Building, Good, Villager, CornerstoneDraft) + EffectWeights
  UI/SidePanel.cs             single IMGUI MonoBehaviour. Renders all tabs, alerts strip, resize/reset
  Resources/catalog/*.json    embedded trimmed game catalog
  Resources/docs/             link-embedded README/AGENTS/USER_GUIDE (offline Settings viewer)
  GlobalUsings.cs             System / Collections.Generic / Linq
  Polyfills.cs                IsExternalInit shim (records work on netstandard2.0)
  StormGuidePlugin.cs         entry point: BaseUnityPlugin, spawns SidePanel host
tests/StormGuide.Tests/       net10.0 xunit, Domain-only via <Compile Include>
tools/CatalogTrim/            net10.0 console: JSONLoader export → trimmed catalog
tools/{Pack,Capture,SmokeRun,Bump}.ps1   release / screenshot / smoke / version-bump scripts
docs/USER_GUIDE.md            end-user guide (also embedded into the plugin)
research/                     decompiled game source (gitignored). NOTES.md has the hook map.
```

### Dependency direction (must not invert)

`Resources/catalog/*.json` (data) → `Domain/` (pure DTOs, **no game refs**) → `Providers/` (pure, depend on Domain only) → `UI/SidePanel.cs` (depends on Providers + Data + Configuration).

`Data/LiveGameState.cs` is the **only** file that touches the running game — every other file consumes its DTOs. New code that needs game state goes through a `LiveGameState` accessor; never reach into `GameController` from `Providers/` or `UI/`.

## Build / deploy

```pwsh
# Whole solution (plugin + CatalogTrim console)
dotnet build StormGuide.slnx -c Release

# Just the plugin (skip the net10 catalog-trim tool)
dotnet build StormGuide/StormGuide.csproj -c Release
```

The plugin's `Deploy` target (`AfterTargets="Build"` in `StormGuide.csproj`) copies `StormGuide.dll` (+ `.pdb` if present) into `$(PluginDeployPath)` = `$(BepInExPath)\BepInEx\plugins\StormGuide`. `StormPath` / `BepInExPath` are user-specific and live in `Directory.Build.props`. **A fresh checkout will fail to resolve `Assembly-CSharp` / `UniRx` / `Sirenix.*` / `API.dll` until those paths exist** — fork `Directory.Build.props` (or a local `Directory.Build.local.props` once introduced) on each machine.

Reference assemblies are resolved from:

- Game `Managed/` via `$(ManagedPath)` — `Assembly-CSharp`, `Assembly-CSharp-firstpass`, `UniRx`, `Unity.TextMeshPro`, `Newtonsoft.Json`, `Sirenix.Serialization`, `Sirenix.OdinInspector.Attributes`.
- ATS_API plugin via `$(ATSAPIPath)` — `API.dll`.
- NuGet (`bepinex` feed in `NuGet.config`) — `BepInEx.Core 5.4.21`, `BepInEx.PluginInfoProps`, `UnityEngine.Modules 2021.3.33` (compile-only).

`netstandard2.0` ⇒ `LangVersion=latest` plus `Polyfills.cs` for `IsExternalInit`.

## Test / smoke

Three layers, fastest to slowest:

```pwsh
# 1. Domain-only xunit tests (no game refs). Runs on CI.
dotnet test tests/StormGuide.Tests/StormGuide.Tests.csproj -c Release

# 2. Structural sanity over embedded catalog + built plugin assembly.
dotnet build StormGuide/StormGuide.csproj /t:Build;Smoke -c Release

# 3. End-to-end: build → deploy to r2modman → tail BepInEx log.
pwsh tools/SmokeRun.ps1
```

`tests/StormGuide.Tests/` (`net10.0`, xunit) source-shares the `Domain/` layer via `<Compile Include="..\..\StormGuide\Domain\**\*.cs" />`. **Do not** broaden that glob to `Providers/` / `Data/` / `UI/` and **do not** add a `<ProjectReference>` to `StormGuide.csproj` — either move would pull in `Assembly-CSharp` / Unity / BepInEx, which CI cannot resolve. If a Provider needs unit-testable pure logic, extract it to `Domain/` first.

Catalog JSON files are copied next to the test assembly via `<None Include=... CopyToOutputDirectory>` so `CatalogDeserializationTests` can read from `AppContext.BaseDirectory/catalog/`. The deserialization settings in that test must mirror `StormGuide/Data/StaticCatalog.cs` — keep them in lockstep when one moves.

`Smoke` (the MSBuild target) checks that `Resources/catalog/{buildings,goods,recipes,races}.json` exist and the built assembly is on disk. Extend it by adding more `<Error Condition="..." Text="..." />` guards — keep checks fast and message-rich. Anything that needs the running game belongs in `tools/SmokeRun.ps1`, not `Smoke`.

## Packaging / release

```pwsh
pwsh tools/Pack.ps1            # Release build → tools/dist/StormGuide-<version>.zip (Thunderstore-shaped)
pwsh tools/Pack.ps1 -Publish   # also runs `gh release create` (or uploads to existing tag)
pwsh tools/Capture.ps1         # full-screen PNGs into tools/screenshots/ (Thunderstore page)
```

Version comes from `<Version>` in `StormGuide/StormGuide.csproj`. Bump it via `tools/Bump.ps1 -Version <x.y.z>` so the csproj and `CHANGELOG.md` move together; never hand-edit either.

`tools/dist/` and `tools/screenshots/` are gitignored. The release flow is split: `release.yml` creates the GitHub Release and attaches notes from `CHANGELOG.md` on `v*` tag push; `tools/Pack.ps1 -Publish` is run from a workstation afterwards to build the plugin DLL (game refs aren't on the runner) and `gh release upload --clobber` the Thunderstore zip onto the same release. The same zip is then uploaded to `thunderstore.io/c/against-the-storm/` by hand.

## Hard invariants

1. **Read-only.** No Harmony patches mutate state. We `Subscribe` to UniRx properties and read service accessors; never `Pick`, `SetProfession`, `Reroll`, etc.
2. **Transparent recommendations.** Every `Score` carries `Components` (label/value/note rows) that the UI renders below the headline number. If a feature can't show its math, it doesn't ship.
3. **Defensive live access.** Every `LiveGameState` accessor returns `null` / `0` / empty on missing services and try/catches around the game-side call. The plugin must never crash if a game type is renamed in a patch.
4. **No prescriptive UI.** Badges/ranks/sorts are fine; auto-clicks, modal nudges, and hidden non-recommended options are not. `Config.ShowRecommendations` toggles all `★`/ranked highlights off cleanly.

## Where to add a feature

- **New tab** — add a value to `SidePanel.Tab`, a `bool` config in `PluginConfig`, a draw method, and a hide-check entry in `IsTabVisible`.
- **New live-state read** — add a static method on `LiveGameState` returning a small DTO. Wrap the game-side call in try/catch; never let exceptions escape.
- **New recommendation** — add a `Provider.For(...)` overload taking the new lookup as an optional `Func<...>`. Return a `Score` whose `Components` explain it row-by-row.
- **Hot-path call** (called every frame from `OnGUI`) — wrap in `TtlCache<T>` and source the TTL from `StormGuide.Data.CacheBudget` (e.g. `CacheBudget.AlertsTtlSec`, `CacheBudget.SummaryTtlSec`). Don't sprinkle bare `0.5f` / `1.0f` literals through the UI — the centralised constants exist so editing one file rebalances the whole panel.
- **New embedded doc** — drop the `.md` somewhere in the repo, add an `<EmbeddedResource Include="..\..." Link="Resources\docs\X.md" LogicalName="StormGuide.Resources.docs.X.md" />` entry to `StormGuide.csproj`, and surface it in the Settings tab's doc viewer.
- **New catalog-backed UI label** — route through `StormGuide.Domain.Localization.GoodName` / `BuildingName` / `RaceName` / `RecipeName` so the live game text-service lookup (when wired) takes precedence over the embedded English `DisplayName`. Don't reach into `Catalog.Goods.TryGetValue(..).DisplayName` from new UI code.

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

After a game patch, recipes/goods/buildings drift. To regen:

1. Install `JSONLoader`, set `Export On Game Load = true`, launch AtS once.
2. `dotnet run --project tools/CatalogTrim` — defaults read the standard JSONLoader export path; emits to `StormGuide/Resources/catalog/`.
3. Rebuild.

A handful of the game's exported JSON files are malformed; the trim tool logs and skips them.

## Decompiling for new symbols

```pwsh
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
- **Settings** (⚙) — reflection-driven tab toggles, persisted why-all flags, hide-empty-recipe filter, hotkey rebinder, catalog reload, **diagnostics bundle** copy action, embedded README/AGENTS/USER_GUIDE doc viewer.
- **Diagnostics** (⚙?, off by default) — 200-line ring buffer of plugin log lines (BepInEx ILogListener filtered by source). The Settings tab carries a **Copy diagnostics bundle** button (plugin version, catalog, hotkey, per-section p50/p95, active config, crash-dump dir, recent log) so users don't need to enable this tab to share state for bug reports.
- **Embark** (on by default) — pre-settlement guidance from the static catalog: race list ranked by min resolve, top starting goods (need overlap × trade value via `EmbarkScoring.TopStartingGoods`), top cornerstone tags by total catalog-building hits (`EmbarkScoring.TopCornerstoneTags`).
- **Footer** — catalog source + plugin version + active hotkey.
- **Hotkeys** — toggle hotkey (default rebindable; `F8` / `G` are common), `Ctrl+1`…`9` jump-to-tab while panel is visible, `F5` reload catalog.

## Don'ts

- Don't add `System.Math.Clamp` — not in netstandard2.0. Use `UnityEngine.Mathf.Clamp01` or manual `Math.Min/Max`.
- Don't reference `EffectModel.Description` without referencing `Sirenix.Serialization` (transitive dep already wired in csproj).
- Don't iterate `BuildingsService.Buildings` directly — it's a `Dictionary<int, Building>`; use `.Values`.
- Don't auto-launch the game from PowerShell scripts. r2modman owns the launch and we don't replicate its preloader injection reliably.
- Don't reach into `GameController` from `Providers/` or `UI/` — go through `LiveGameState`. Keeps the read-only invariant auditable in one file.
- Don't reorder dependency direction (Resources → Domain → Providers → UI). Domain stays game-free so `tests/StormGuide.Tests/` can stay game-free too.
- Don't commit `tools/dist/`, `tools/screenshots/`, or `research/AssemblyCSharp/` — all gitignored.
- Don't bump `<Version>` in `StormGuide/StormGuide.csproj` directly once `tools/Bump.ps1` lands — it has to keep csproj and `CHANGELOG.md` in sync.

## 1.0 history

The full Phase A–F productionization arc that closed `0.0.1 → 1.0.0` is recorded in plan `503ea85f-d552-4ae2-baae-2b894fb2bc18` ("StormGuide 1.0 — Productionize, Performance, Release") and in `CHANGELOG.md` under `[1.0.0]`. Per-phase summaries:

- **Phase A** — release plumbing (manifest template, real icon, CHANGELOG, `tools/Bump.ps1`, `.github/workflows/{ci,release}.yml`).
- **Phase B** — `tests/StormGuide.Tests/` (`net10.0`, xunit, Domain-only via `<Compile Include>`).
- **Phase C** — cache TTL constants in `Data/CacheBudget.cs`, percentile math in `Domain/PerfRing.cs`. Live profiling baseline + per-tab p95 budgets are still open follow-ups (Diagnostics surfaces them; CI doesn't gate).
- **Phase D** — `Domain/EmbarkScoring.cs`, `ShowEmbarkTab` flipped to `true`, Settings "Diagnostics bundle" copy-action.
- **Phase E** — `Domain/Localization.cs` adapter; SidePanel display-name lookups routed through it. `Localization.LiveLookup` wiring against the AtS text-service is deferred until the surface is verified via dnSpy.
- **Phase F** — `1.0.0` shipped.

Post-1.0 carry-overs:

- Wire `Localization.LiveLookup` from `LiveGameState` and verify a non-English locale in a real smoke pass.
- Capture per-tab p50/p95 budgets against one representative settlement and surface a Diagnostics regression warning when p95 × 1.5 is exceeded.
- Pure-`Domain/` extraction for `BuildingProvider` / `CornerstoneDraftProvider` ranking so deterministic tests can land.
