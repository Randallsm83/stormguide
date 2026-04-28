# Changelog

All notable changes to StormGuide are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

`tools/Bump.ps1 -Version <x.y.z>` rolls the `Unreleased` section into a dated
heading and stamps the same version into `StormGuide/StormGuide.csproj`. New
entries between releases go under `## [Unreleased]`.

## [Unreleased]

### Added

- `CHANGELOG.md` (this file).
- `tools/manifest.template.json` — checked-in Thunderstore manifest template; `tools/Pack.ps1` reads it instead of building the manifest inline.
- `tools/Bump.ps1` — single command to bump the csproj `<Version>` and roll the changelog `Unreleased` section into a dated entry.
- `.github/workflows/ci.yml` — validation pipeline on PRs and `main` (catalog JSON parse, manifest template parse, `tools/CatalogTrim` build, `dotnet test` on `tests/StormGuide.Tests`, PowerShell syntax check).
- `.github/workflows/release.yml` — on `v*` tag, extracts release notes from this changelog and creates the GitHub Release. Asset upload is done from a workstation via `tools/Pack.ps1 -Publish` because the plugin DLL cannot be built on the runner without the game's reference assemblies.
- `tests/StormGuide.Tests/` — xunit project (`net10.0`) source-sharing the `Domain/` layer via `<Compile Include>`. Covers `Score` formatting and value/components reconciliation, `Catalog` lookup methods (`RecipesProducing`, `RecipesConsuming`, `RacesNeeding`), `RecipeInfo.BaseGoodsPerSec`, round-trip deserialization of the embedded catalog JSON files using the same Newtonsoft settings as `StaticCatalog`, and `PerfRing` (bounded ring + nearest-rank percentile). **41 tests**, no game references.
- `tests/` folder added to `StormGuide.slnx` so `dotnet build StormGuide.slnx` and `dotnet test StormGuide.slnx` pick up the test project.
- `StormGuide/Domain/PerfRing.cs` — bounded ring of frame-cost samples with `P50` / `P95` / `Percentile(p)`. Pure (game-free), so it's unit-tested in `tests/StormGuide.Tests/`.
- `StormGuide/Data/CacheBudget.cs` — single source of truth for the four `TtlCache` TTLs used by the UI plus the Diagnostics perf-ring frame-window size. Editing one constant rebalances the whole panel.

### Changed

- `tools/Pack.ps1` reads `tools/manifest.template.json` and substitutes `version_number` instead of hard-coding the manifest body.
- `UI/SidePanel.cs` per-section perf history switched from `Dictionary<string, Queue<double>>` + private `Percentile` helper to `Dictionary<string, PerfRing>`. Reads `ring.P50` / `ring.P95` for the Diagnostics surface.
- `UI/SidePanel.cs` cache TTL literals (`0.5f`, `1.0f`) replaced with named constants from `CacheBudget` so the UI no longer carries bare timing values.

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

[Unreleased]: https://github.com/Randallsm83/stormguide/compare/v0.0.1...HEAD
[0.0.1]: https://github.com/Randallsm83/stormguide/releases/tag/v0.0.1
