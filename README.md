# StormGuide
Decision-time game guide overlay for [Against the Storm](https://store.steampowered.com/app/1336490/).
Surfaces production math, synergy counts, resolve breakdowns, and ranked
recommendations with their reasoning visible — the player keeps the click.
## Documentation
- [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md) — player-facing walkthrough of every tab, hotkeys, common workflows ("I'm running out of fuel", "a trader just arrived", "an order is failing", etc.), and a troubleshooting section. Also embedded into the plugin and readable offline from `Settings → Docs → USER GUIDE`.
- [`AGENTS.md`](AGENTS.md) — contributor / agent instructions for the codebase: solution layout, dependency direction (Resources → Domain → Providers → UI), build/test/smoke commands, hard invariants (read-only, transparent recommendations, defensive live access, no prescriptive UI, no direct legacy `UnityEngine.Input` calls), where to add features, the game-API hook map, and the catalog-regeneration recipe.
- [`CHANGELOG.md`](CHANGELOG.md) — release history. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/); current work lives under `[Unreleased]` until `tools/Bump.ps1` rolls it into a dated heading.
- [`docs/screenshots/README.md`](docs/screenshots/README.md) — capture workflow + naming convention for committed PNGs referenced from the user guide and Thunderstore page.
## Features
A togglable side panel (default hotkey `F8`) with up to ten tabs and a global alerts strip.
- **Alerts strip** — single row of clickable badges summarising the settlement: idle workshops, races below resolve, goods with short runway. Each badge deep-links to the right tab + selection.
- **Home** — collapsible-section dashboard: village summary, fuel runway, trader timeline mini-bar with desire heatmap and visit countdown, idle workshops, worker rebalance hints, goods-at-risk with sparkline + 60s forecast cone, race needs unmet, orders summary, forest exploration %, owned cornerstones, marked recipes, pinned recipes (storm-clock chip, upstream rollup, reorder + worker chip, auto-pin from at-risk rows). Section collapse state persists across sessions.
- **Building** — search/select a building (matches name + tags, **bolded substring on match**, fuzzy fallback); ranked recipes with `★` on the top output, race-fit "best workers" list, tag chips and recipe-input chips that filter the list, draining-input flags, root-cause idle banner (`unstaffed` / `no inputs` / `output full` / `paused`), live cycles counter (`X total · Y in last 5 min`), "alternative producers" line per recipe, an inline worker-rebalance panel, and a clickable **reputation-order ETA** line under recipes that feed an active order.
- **Goods** — production paths ranked by cost; live `● flow` (production/consumption/net/runway) with breakdown and **per-consumer % share**; what-if burn slider; current+next trader rotation with desires ranked by total value, live price-history chart with **trend extrapolation** (slope in `currency/min`, ~3-min projection), affordability, and a tickable buy-list builder under the current trader; producer chips for fuel-only / eatable-only / draining-only.
- **Villagers** — village summary header plus per-race detail with live resolve bar, **60s resolve trajectory sparkline**, race-ratio drift warnings, top resolve contributors with free-text filter, resolve-goal calculator, dietary stockpile rows, **🍴 dietary variety** score, **housing match indicator** (preferred / shelter-only / none), and best-fit workplaces.
- **Orders** — active reputation orders sorted by tracked → picked → time-pressure → name. Tier badge (bronze/silver/gold), red/amber timer for failable orders, objective progress bar + sparkline + ETA at current burn, reward score + categories, **plan-of-attack list** (top-2 catalog recipes + 2-step input chain with stockpiles) on tracked failable orders, storm-vs-deadline warning, unpicked-pick decision diff with `▸ what-if` Δscore breakdown, and a `♥ best for me` badge driven by your owned cornerstones.
- **Glades** — explored %, dangerous/forbidden counts, danger-distribution chart, per-chase reward windows, a **"next chase" pin** sorted by reward-value-per-second-remaining, and a **⏱ clear-time estimator** when at least one Resource Gathering scout is assigned.
- **Cornerstone Draft** — auto-switches on the in-game pick popup. Options ranked by tag-match synergy + cross-run pick history; auto-pick headline when one option dominates; per-option `vs` compare panel with Δscore / Δbuildings / unique-tag deep-dive enumerating affected buildings; **`↻ stacks with`** overlap line for every owned cornerstone whose `usabilityTags` overlap; sprite icon next to each row.
- **Settings** (`⚙`) — free-text filter, reflection-driven tab toggles, hotkey rebinder, risk thresholds, race-ratio targets, config import/export, named **pin presets**, catalog reload, **diagnostics bundle** copy action, embedded README/AGENTS/USER_GUIDE doc viewer.
- **Diagnostics** (`⚙?`, off by default) — 200-line plugin log ring buffer, session stats, **per-section p50/p95** frame-cost breakdown, catalog-diff banner, crash-dump auto-write on GUI exceptions.
- **Embark** (on by default) — catalog-driven race comparison and starting-goods recommendation ranked by need overlap × trade value.
## Hotkeys
- `F8` (rebindable) — toggle panel. Goes through BepInEx's `KeyboardShortcut.IsDown()`, so it works on current AtS builds (which disable the legacy `UnityEngine.Input` class).
- `Ctrl+1` … `Ctrl+9`, `F5`, and Shift-held debug overlay — read legacy `UnityEngine.Input` and silently no-op on current builds. Each tab button shows its `·N` index hint as a reminder; reach the same actions via the panel's mouse UI (tab strip click, `Settings → Catalog → reload catalog`, `Diagnostics → per-section p50/p95`).
- `Esc` — cancel an in-progress hotkey rebind.
## Transparency principle
Every score has an expandable breakdown showing the inputs that produced it.
Nothing is auto-applied; the panel never clicks anything in the game on your
behalf. Recommendations can be turned off entirely (`UI > Show Recommendations`)
for a neutral data display.
## Build & install
```
dotnet build -c Release
```
Requires a .NET SDK on `PATH` (the runtime-only install at `C:\Program Files\dotnet\dotnet.exe` is *not* enough — `dotnet build` will fail with `No .NET SDKs were found`). On Windows, `scoop install dotnet-sdk` is the easiest path; the bundled `tools/SmokeRun.ps1` and `tools/RegenCatalog.ps1` wrappers will probe the scoop install + the standard install locations and pin `DOTNET_ROOT` for child processes.
The post-build target copies `StormGuide.dll` into the BepInEx profile's
`plugins\StormGuide\` folder. Edit `Directory.Build.props` to point at your AtS
install and BepInEx profile.
## Packaging & release
- `tools/Pack.ps1` builds Release and produces a Thunderstore-shaped zip in `tools/dist/StormGuide-<version>.zip`. Pass `-Publish` to also run `gh release create` (or upload to an existing tag).
- `tools/Capture.ps1` captures full-screen PNGs into `tools/screenshots/` for the Thunderstore page; pause between shots to switch tabs.
- `tools/SmokeRun.ps1` builds, deploys to r2modman, and tails the BepInEx log.
- `tools/RegenCatalog.ps1` is the wrapper around `dotnet run --project tools/CatalogTrim` (see *First-time data* below).
## First-time data
StormGuide bundles a static catalog (74 goods, 7 races, 186 buildings, 243
recipes) under `StormGuide/Resources/catalog/`. To regenerate after a game
patch:
1. Install [JSONLoader](https://thunderstore.io/c/against-the-storm/p/ATS_API_Devs/JSONLoader/) and toggle `Export On Game Load = true` in its config.
2. Launch AtS once to dump data to `%USERPROFILE%\AppData\LocalLow\Eremite Games\Against the Storm\JSONLoader\Exported`.
3. Run `pwsh tools/RegenCatalog.ps1` from anywhere in the repo. The wrapper picks an SDK-bearing `dotnet` (scoop versioned install, scoop shim, Program Files, LocalAppData) so a runtime-only install on `PATH` can't hijack the build, then runs `tools/CatalogTrim` to trim the export into `Resources/catalog/`.
4. Rebuild.
## Dependencies
- BepInEx 5.4.23.4 (Mono)
- [ATS_API_Devs/API](https://thunderstore.io/c/against-the-storm/p/ATS_API_Devs/API/) (≥ 3.6)
## Config
All settings live under `BepInEx/config/stormguide.cfg` and surface in the
in-game `Options → Mods → StormGuide` menu via ATS_API. Includes hotkey,
panel position/size, per-tab visibility toggles, recommendations on/off,
and last-selected building/good/race for cross-launch persistence.
