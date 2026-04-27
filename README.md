# StormGuide
Decision-time game guide overlay for [Against the Storm](https://store.steampowered.com/app/1336490/).
Surfaces production math, synergy counts, resolve breakdowns, and ranked
recommendations with their reasoning visible — the player keeps the click.
## Features
A togglable side panel (default hotkey `G`) with up to ten tabs and a global alerts strip.
- **Alerts strip** — single row of clickable badges summarising the settlement: idle workshops, races below resolve, goods with short runway. Each badge deep-links to the right tab + selection.
- **Home** — dashboard: village summary, top trader desire, idle workshops, goods at risk, race needs unmet, orders summary, forest exploration %, owned cornerstones. One-click jumps into every other tab.
- **Building** — search/select a building (matches name + tags, **bolded substring on match**, fuzzy fallback); ranked recipes with `★` on the top output, race-fit "best workers" list, tag chips and recipe-input chips that filter the list, draining-input flags, root-cause idle banner (`unstaffed` / `no inputs` / `output full` / `paused`), live cycles counter (`X total · Y in last 5 min`), "alternative producers" line per recipe, and an inline worker-rebalance panel.
- **Good** — production paths ranked by cost; live `● flow` (production/consumption/net/runway) with breakdown and **per-consumer % share**; current+next trader rotation with desires ranked by total value, live price chart, affordability, and a tickable buy-list builder under the current trader; producer chips for fuel-only / eatable-only / draining-only.
- **Villagers** — village summary header plus per-race detail with live resolve bar, **60s resolve trajectory sparkline**, race-ratio drift warnings, top resolve contributors, dietary stockpile rows, and best-fit workplaces.
- **Orders** — active reputation orders sorted by tracked → picked → time-pressure → name. Tier badge (bronze/silver/gold), red/amber timer for failable orders, objective progress bar, reward score + categories, **plan-of-attack list** (top-2 catalog recipes + 2-step input chain with stockpiles) on tracked failable orders, storm-vs-deadline warning, and unpicked-pick decision diff.
- **Glades** — explored %, dangerous/forbidden counts, danger-distribution chart, per-chase reward windows, and a **"next chase" pin** sorted by reward-value-per-second-remaining.
- **Cornerstone Draft** — auto-switches on the in-game pick popup. Options ranked by tag-match synergy + cross-run pick history; auto-pick headline when one option dominates; per-option `vs` compare panel with Δscore / Δbuildings / unique-tag deep-dive enumerating affected buildings; sprite icon next to each row.
- **Home** dashboard — pinned recipes (with storm-clock chip, upstream rollup, auto-pin from at-risk rows), worker rebalance hints, fuel runway, race-ratio drift, trader buy-list and visit-countdown, goods-at-risk with sparkline + 60s forecast cone, glade chase priority, owned cornerstones, session stats.
- **Settings** (`⚙`) — free-text filter, reflection-driven tab toggles, hotkey rebinder, risk thresholds, race-ratio targets, config import/export, named **pin presets**, catalog reload, embedded README/AGENTS doc viewer.
- **Diagnostics** (`⚙?`, off by default) — 200-line plugin log ring buffer, session stats, **per-section p50/p95** frame-cost breakdown, catalog-diff banner, crash-dump auto-write on GUI exceptions.
- **Embark** (off by default) — catalog-driven race comparison and starting-goods recommendation ranked by need overlap × trade value.
## Hotkeys
- `G` (rebindable) — toggle panel.
- `Ctrl+1` … `Ctrl+9` — jump to the visible tab at that position.
- `F5` — reload the embedded catalog (mirrors the Settings button).
## Transparency principle
Every score has an expandable breakdown showing the inputs that produced it.
Nothing is auto-applied; the panel never clicks anything in the game on your
behalf. Recommendations can be turned off entirely (`UI > Show Recommendations`)
for a neutral data display.
## Build & install
```
dotnet build -c Release
```
The post-build target copies `StormGuide.dll` into the BepInEx profile's
`plugins\StormGuide\` folder. Edit `Directory.Build.props` to point at your AtS
install and BepInEx profile.
## Packaging & release
- `tools/Pack.ps1` builds Release and produces a Thunderstore-shaped zip in `tools/dist/StormGuide-<version>.zip`. Pass `-Publish` to also run `gh release create` (or upload to an existing tag).
- `tools/Capture.ps1` captures full-screen PNGs into `tools/screenshots/` for the Thunderstore page; pause between shots to switch tabs (`Ctrl+1..8`).
- `tools/SmokeRun.ps1` builds, deploys to r2modman, and tails the BepInEx log.
## First-time data
StormGuide bundles a static catalog (74 goods, 7 races, 186 buildings, 243
recipes) under `StormGuide/Resources/catalog/`. To regenerate after a game
patch:
1. Install [JSONLoader](https://thunderstore.io/c/against-the-storm/p/ATS_API_Devs/JSONLoader/) and toggle `Export On Game Load = true` in its config.
2. Launch AtS once to dump data to `%USERPROFILE%\AppData\LocalLow\Eremite Games\Against the Storm\JSONLoader\Exported`.
3. Run `dotnet run --project tools/CatalogTrim` to trim the export into `Resources/catalog/`.
4. Rebuild.
## Dependencies
- BepInEx 5.4.23.4 (Mono)
- [ATS_API_Devs/API](https://thunderstore.io/c/against-the-storm/p/ATS_API_Devs/API/) (≥ 3.6)
## Config
All settings live under `BepInEx/config/stormguide.cfg` and surface in the
in-game `Options → Mods → StormGuide` menu via ATS_API. Includes hotkey,
panel position/size, per-tab visibility toggles, recommendations on/off,
and last-selected building/good/race for cross-launch persistence.
