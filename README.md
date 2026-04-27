# StormGuide
Decision-time game guide overlay for [Against the Storm](https://store.steampowered.com/app/1336490/).
Surfaces production math, synergy counts, resolve breakdowns, and ranked
recommendations with their reasoning visible — the player keeps the click.
## Features
A togglable side panel (default hotkey `G`) with four tabs and a global alerts strip.
- **Alerts strip** — single row of clickable badges that summarise the settlement: idle workshops, races below resolve, goods with short runway. Each badge deep-links to the right tab + selection.
- **Building tab** — search/select a building; recipes ranked by effective goods/min with a `★` on the top output and a `▸ why` row-by-row breakdown. Live joins: worker count, idle flag, per-input stockpile + at-risk markers when stock is < 2 cycles.
- **Good tab** — production paths ranked by cost-per-output, consumers, racial-need flags, current+next trader rotation with live currency multipliers and travel progress, and a live `● flow` line (production / consumption / net / runway) with an expandable per-building breakdown.
- **Villagers tab** — village summary header (race counts + resolve at a glance) plus per-race detail with live resolve bar (current vs target, color-coded) and the top resolve contributors.
- **Cornerstone Draft tab** — auto-switches when the in-game cornerstone pick popup opens. Options ranked by structural synergy (counts of your buildings whose tags match the effect's `usabilityTags`); breakdown shows per-tag hits. Currently-owned cornerstones listed alongside.
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
