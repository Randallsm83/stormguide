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
                              FlowRow, VillageSummary, PerfRing, EmbarkScoring… (no game refs)
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

Version comes from `<Version>` in `StormGuide/StormGuide.csproj`. `tools/dist/` and `tools/screenshots/` are gitignored.

Current caveats (resolved by Phase A of the 1.0 plan):

- `Pack.ps1` builds the Thunderstore manifest **inline** with a placeholder `website_url` (`https://github.com/example/stormguide`).
- No real `tools/icon.png` — `Pack.ps1` falls back to a 1×1 transparent PNG so the layout still validates.
- There is no `CHANGELOG.md` and no GitHub Actions — every release is currently driven by hand from a workstation.

> **Planned (1.0 plan, Phase A):** extract the manifest to a checked-in template with the real repo URL, ship a real `tools/icon.png`, add `CHANGELOG.md` (Keep-a-Changelog), and wire up `.github/workflows/{ci,release}.yml`. `ci.yml` runs `dotnet build /t:Build;Smoke -c Release` + `dotnet test` on PRs and `main`. `release.yml` triggers on `v*` tags and attaches the Pack zip to the GitHub Release. A `tools/Bump.ps1` (or similar) updates `<Version>` in the csproj and prepends a new section to `CHANGELOG.md` in one step.

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

## Planned for 1.0

Tracked in plan `503ea85f-d552-4ae2-baae-2b894fb2bc18` ("StormGuide 1.0 — Productionize, Performance, Release"). Items below are **forward-looking**; treat the existing sections above as the source of truth for what's actually implemented. When a phase lands, fold its details into the relevant section above and trim the bullet here.

### Phase A — Release plumbing

- `tools/manifest.template.json` (or similar) replaces the inline Thunderstore manifest in `Pack.ps1`. Real `website_url`, real `dependencies` list.
- Real `tools/icon.png`. Drop the 1×1 stub fallback for tagged release builds.
- `CHANGELOG.md` in Keep-a-Changelog format — `Unreleased` section plus a seeded `0.0.1` entry covering everything shipped to date.
- `.github/workflows/ci.yml` — `windows-latest` runner; runs `dotnet build /t:Build;Smoke -c Release` + `dotnet test` on PRs and pushes to `main`. Game/ATS_API reference assemblies are not available on the runner, so CI must build with stub paths or skip targets that require them; the Smoke target itself works without the game.
- `.github/workflows/release.yml` — triggers on `v*` tags. Runs Pack, then `gh release create` to attach `tools/dist/StormGuide-<version>.zip`.
- `tools/Bump.ps1` — single command: bumps `<Version>` in `StormGuide/StormGuide.csproj`, rolls `CHANGELOG.md` `Unreleased` into a dated section, and stages both files for the same commit.

### Phase B — Test scaffolding (landed)

Landed as `tests/StormGuide.Tests/` (`net10.0`, xunit) on commit — see Test / smoke section above. Outstanding follow-ups owned by later phases:

- **Provider-level tests.** `BuildingProvider` / `CornerstoneDraftProvider` inputs are still typed against `EffectModel` / `BuildingsService` from `Assembly-CSharp`. Until pure scoring helpers are extracted into `Domain/`, ranking-determinism tests have to wait. Extracting them is also the prerequisite for Phase C cache budgeting.

### Phase C — Performance budgets (centralisation landed)

Landed:

- `StormGuide/Data/CacheBudget.cs` — single source of truth for cache TTLs (`AlertsTtlSec`, `SummaryTtlSec`, `OwnedCornerstonesTtlSec`, `TraderDesiresTtlSec`) and `PerfRingFrames`. `UI/SidePanel.cs` no longer carries bare timing literals.
- `StormGuide/Domain/PerfRing.cs` — bounded ring + percentile math extracted into Domain so it's pure and unit-tested. Replaces the inline `Dictionary<string, Queue<double>>` plus private `Percentile` helper. The Diagnostics tab now reads `ring.P50` / `ring.P95` directly.

Still pending (require a live mid-game profiling pass):

- Per-tab p50/p95 budgets, recorded in this section. Diagnostics tab surfaces them live; CI does not gate on them. Capture the baseline against one representative settlement, then set budgets at observed p95 × 1.5 so a regression flips a Diagnostics warning.
- Tune the `CacheBudget` constants once the baseline is captured — today's values (0.5s / 1.0s / 120 frames) are hand-tuned defaults, not measured ones.

### Phase D — Productionize Embark + Diagnostics (landed)

Landed:

- `StormGuide/Domain/EmbarkScoring.cs` — pure pre-settlement rankers (`TopStartingGoods`, `TopCornerstoneTags`). `UI/SidePanel.DrawEmbarkTab` now calls into them; the inline aggregation is gone.
- `ShowEmbarkTab` defaults to `true`. The tab description in `PluginConfig.cs` no longer says "scaffolding only".
- The Embark tab header drops the scaffolding caveat and now reads as production guidance.
- Settings tab gains a **Diagnostics bundle** section with a one-click "copy diagnostics bundle" button (`SidePanel.BuildDiagnosticsBundle`). Bundle includes plugin version, catalog snapshot, hotkey, crash-dump dir + count, per-section p50/p95, active config (via `ExportConfigJson`), and the recent log tail.
- Crash-dump output already lands in `BepInEx.Paths.ConfigPath` (`stormguide-crash-*.txt`); the bundle includes that path so bug-report flow doesn't require Diagnostics-tab discovery.
- `ShowDiagnosticsTab` stays `false` by default — the Settings bundle is the new sanctioned bug-report path.

### Phase E — Localization passthrough

- New thin adapter (`StormGuide/Data/Localization.cs` or similar) wraps the game's text-service lookup. Unknown keys / unavailable service → fallback to the embedded English from the trimmed catalog.
- Catalog-backed UI labels in `UI/SidePanel.cs` route through the adapter. Score breakdowns (which use English-only structural labels) stay as-is for now.

### Phase F — 1.0 release

- Bump to `1.0.0` via `tools/Bump.ps1`.
- Capture fresh Thunderstore screenshots via `Capture.ps1`.
- Tag `v1.0.0` → `release.yml` attaches the zip → manual upload of the same artifact to `thunderstore.io/c/against-the-storm/`.
