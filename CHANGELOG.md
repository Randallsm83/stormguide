# Changelog

All notable changes to StormGuide are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

`tools/Bump.ps1 -Version <x.y.z>` rolls the `Unreleased` section into a dated
heading and stamps the same version into `StormGuide/StormGuide.csproj`. New
entries between releases go under `## [Unreleased]`.

## [Unreleased]

### Added

- Home tab subsection collapse/expand: every Home section (Pinned, Marked recipes, Fuel, Village, Trade, Idle, Rebalance, Risks, Needs, Orders, Glades, Cornerstones) now renders a ▾ / ▸ caret next to its header. Click to collapse the section body; the chosen state persists across sessions via the new `PluginConfig.HomeCollapsedSections` config string. Header decoration ("open \u203a" jump buttons, dynamic styling for fuel runway colour) stays intact when the section is expanded.
- `StormGuide/UI/SidePanel.BeginHomeSection` helper: opens a `BeginHorizontal` row with the caret + label, returning whether the section is expanded so callers can early-return without rendering the body. Persists collapsed-set diffs immediately on toggle.
- Building tab recipe cards: when a recipe's produced good is the target of an active reputation order objective and the settlement is producing it net-positive, the card now surfaces a clickable `\u2192 reputation order "X" [tier]: ~N.Nm at current burn` line. ETA uses settlement-wide net flow as the denominator; clicking jumps to the Orders tab. Soonest-ETA wins when multiple orders target the same good.
- `SidePanel.PopulateRecipeOrderEtaCache` / `DrawRecipeOrderEta` helpers: populate a per-frame map of `producedGood \u2192 (order, ETA minutes)` at the top of `DrawBuildingDetail` so each recipe card looks up in O(1) instead of re-walking active orders + `MatchedGoodFor`.

## [1.0.0] - 2026-04-28

First public release. The `0.0.1` baseline already shipped every feature surface end-users see; `1.0.0` is what closes out the productionization work behind it: release plumbing (manifest template, CI, release workflow, version-bump script), a Domain-only test project (60 tests), centralised cache TTLs and per-section frame-cost percentiles, productionized Embark and Diagnostics tabs, and a localization passthrough adapter so non-English players can pick up translated catalog strings once the live text-service is wired.

### Added

- `CHANGELOG.md` (this file).
- `tools/manifest.template.json` — checked-in Thunderstore manifest template; `tools/Pack.ps1` reads it instead of building the manifest inline.
- `tools/Bump.ps1` — single command to bump the csproj `<Version>` and roll the changelog `Unreleased` section into a dated entry.
- `.github/workflows/ci.yml` — validation pipeline on PRs and `main` (catalog JSON parse, manifest template parse, `tools/CatalogTrim` build, `dotnet test` on `tests/StormGuide.Tests`, PowerShell syntax check).
- `.github/workflows/release.yml` — on `v*` tag, extracts release notes from this changelog and creates the GitHub Release. Asset upload is done from a workstation via `tools/Pack.ps1 -Publish` because the plugin DLL cannot be built on the runner without the game's reference assemblies.
- `tests/StormGuide.Tests/` — xunit project (`net10.0`) source-sharing the `Domain/` layer via `<Compile Include>`. Covers `Score` formatting and value/components reconciliation, `Catalog` lookup methods (`RecipesProducing`, `RecipesConsuming`, `RacesNeeding`), `RecipeInfo.BaseGoodsPerSec`, round-trip deserialization of the embedded catalog JSON files using the same Newtonsoft settings as `StaticCatalog`, `PerfRing` (bounded ring + nearest-rank percentile), `EmbarkScoring` (pre-settlement rankers), and `Localization` (catalog-key → display-name fallback chain). **60 tests**, no game references.
- `tests/` folder added to `StormGuide.slnx` so `dotnet build StormGuide.slnx` and `dotnet test StormGuide.slnx` pick up the test project.
- `StormGuide/Domain/PerfRing.cs` — bounded ring of frame-cost samples with `P50` / `P95` / `Percentile(p)`. Pure (game-free), so it's unit-tested in `tests/StormGuide.Tests/`.
- `StormGuide/Data/CacheBudget.cs` — single source of truth for the four `TtlCache` TTLs used by the UI plus the Diagnostics perf-ring frame-window size. Editing one constant rebalances the whole panel.
- `StormGuide/Domain/EmbarkScoring.cs` — pure pre-settlement rankers: `TopStartingGoods` (race-need overlap × trade value, with a value-floor of 1 so free goods still score) and `TopCornerstoneTags` (per-race characteristic tag × catalog-building hits). Replaces the inline aggregation that lived in `DrawEmbarkTab`.
- Settings tab "Diagnostics bundle" section: one-click `Copy diagnostics bundle` button captures plugin version + catalog snapshot + hotkey + crash-dump dir + per-section p50/p95 + active config + recent log to the clipboard. Available without enabling the Diagnostics tab.
- `StormGuide/Domain/Localization.cs` — pure adapter for resolving display strings from catalog model keys. Exposes `GoodName` / `BuildingName` / `RaceName` / `RecipeName(modelKey, catalog)`. Resolution chain: optional `LiveLookup` (live game text-service, currently unwired) → catalog `DisplayName` → raw model key. Throwing or null/whitespace lookups fall through silently so translation can never crash the UI.
- `tests/StormGuide.Tests/LocalizationTests.cs` — 11 tests covering empty key / catalog miss / catalog hit / live-lookup wins / live-lookup null|whitespace falls through / live-lookup throws is swallowed / live-lookup throws + catalog miss falls back to model key / per-domain (`Race`, `Building`, `Recipe`) accessors. Resets the static `LiveLookup` between tests via `IDisposable`.
- `StormGuidePlugin.Awake()` carries a TODO comment block documenting the future `Localization.LiveLookup = key => textService.Get(key)` wiring point pending dnSpy verification of the AtS text-service surface.

### Changed

- `ShowEmbarkTab` default flipped from `false` to `true`. Embark is no longer scaffolding-only.
- `PluginConfig.ShowEmbarkTab` description updated: "pre-settlement helper: race comparison, starting-goods overlap, cornerstone-tag leverage" (was "scaffolding only").
- `UI/SidePanel.DrawEmbarkTab` rewired to call `EmbarkScoring.TopStartingGoods` / `TopCornerstoneTags`. The header dropped the "scaffolding" / "per-biome ranking still needs MetaController join" caveats.

- `tools/Pack.ps1` reads `tools/manifest.template.json` and substitutes `version_number` instead of hard-coding the manifest body.
- `UI/SidePanel.cs` per-section perf history switched from `Dictionary<string, Queue<double>>` + private `Percentile` helper to `Dictionary<string, PerfRing>`. Reads `ring.P50` / `ring.P95` for the Diagnostics surface.
- `UI/SidePanel.cs` cache TTL literals (`0.5f`, `1.0f`) replaced with named constants from `CacheBudget` so the UI no longer carries bare timing values.
- `UI/SidePanel.cs` routes its eight catalog-backed display lookups (alerts strip, home risks, home needs, building input chips, race needs supplied, trader buy-list, embark race-needs join, village summary race naming) through the new `Localization` adapter. Score breakdowns and structural UI labels stay English-only by design.

## [0.0.1] - 2026-04-26

Baseline. Everything shipped to `main` before the changelog existed is rolled up here so the trail starts somewhere coherent.

### Added

- BepInEx 5 plugin assembly (`netstandard2.0`) deployed via `Directory.Build.props`-driven post-build copy.
- Embedded trimmed game catalog (74 goods / 7 races / 186 buildings / 243 recipes) under `StormGuide/Resources/catalog/`.
- Side panel: hotkey toggle (default `F8`), drag/resize, persisted position/size.
- Tabs: Home, Building, Good, Villagers, Orders, Glades, Cornerstone Draft, Settings; Embark and Diagnostics are off by default.
- Alerts strip with deep-link badges (idle workshops, races below resolve, goods at risk).
- Transparent score breakdowns on every recommendation (Building recipes, Good production paths, Villager resolve, Cornerstone draft synergy).
- Cornerstone draft auto-popup with `vs` compare panel.
- Hot catalog reload (`F5`).
- BepInEx config bindings auto-rendered in the ATS_API mod menu.
- `tools/Pack.ps1` packaging script producing a Thunderstore-shaped zip.
- `tools/CatalogTrim` console (`net10.0`) for regenerating the catalog from JSONLoader exports.
- MSBuild `Smoke` target validating embedded catalog file presence and built assembly existence.

[Unreleased]: https://github.com/Randallsm83/stormguide/compare/v1.0.0...HEAD
[0.0.1]: https://github.com/Randallsm83/stormguide/releases/tag/v0.0.1
[1.0.0]: https://github.com/Randallsm83/stormguide/releases/tag/v1.0.0
