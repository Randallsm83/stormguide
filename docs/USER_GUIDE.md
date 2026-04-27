# StormGuide User Guide
A workflow-oriented walkthrough of every tab in the StormGuide overlay for *Against the Storm*.
Read this top-to-bottom on first install, or jump straight to the workflow that matches what
just happened in your settlement (low fuel, trader arriving, draft popped, etc.).

The panel is decision-time **information** — it ranks options and shows the reasoning, but
never clicks anything for you. Recommendations can be turned off entirely under
`Settings → General → Show recommendations`.

## Quick start
1. Press `G` (default) to open the panel; press `G` again to hide it.
2. The tabs are stacked left-to-right. Use `Ctrl+1` … `Ctrl+9` to jump to a tab while the
   panel is visible. Tab order matches the strip: Home, Building, Good, Villagers, Orders,
   Glades, Draft, Settings (`⚙`), Diagnostics (`⚙?`), Embark.
3. Drag the title bar to move; drag the bottom-right grip (`◢`) to resize. The reset button
   (`↺`) at the top-right snaps the panel back to its default position and size.
4. Hover almost any chip/button for a tooltip explaining what it does.

## Reading conventions
- `★` marks the top-ranked option in a list (when recommendations are on).
- `▸ why` / `▾ why` toggles the math behind any score; nothing is hidden.
- Coloured chips: green = healthy / profitable, amber = warning, red = critical.
- A **bold** substring inside a list row marks where your search query matched.
- The **alerts strip** under the tab row is a global summary — click any chip to deep-link
  into the right tab + selection.

## Hotkeys
- `G` — toggle the panel (rebindable in Settings → Hotkey).
- `Ctrl+1` … `Ctrl+9` — switch to the visible tab at that index.
- `F5` — reload the embedded catalog (mirrors the Settings button).
- `Esc` — cancel an in-progress hotkey rebind.
- Hold `Shift` while the panel is up — show a per-section frame-cost overlay
  (diagnostic only).

## Tabs

### Home — settlement at a glance
A scrolling dashboard with one section per concern. Sections render only when relevant data
is available, so an empty Home tab usually means everything's fine.
- **Pinned recipes** — recipes you've stuck from the Building tab. Each row shows
  `building → recipe: X/min · stock N · → 50 in ~Ym`. The 60-second flow sparkline next to
  the row visualises whether the produced good is climbing or sliding. The `assigned/capacity`
  chip turns red when the building has no workers. Use the `▴` `▾` arrows to reorder, or
  `unpin` to remove. Markers (`⛔ stopped`, `★ priority`) come from the Building tab recipe
  card.
- **Marked recipes** — quick-jump list mirroring whatever you've flagged. The mini list keeps
  the markers visible even when you don't have the recipe pinned to Home.
- **Fuel** — total burnable stockpile + estimated minutes left. When runway < 5 min, an
  `auto-pin top fuel producer` button appears that pins the highest-throughput fuel recipe
  in one click.
- **Village** — alive count + per-race resolve summary. Drift warnings fire when a race's
  ratio differs from the targets you set in Settings → Risk thresholds.
- **Trade** — current/next trader, top desire, combined revenue across both rotations,
  buy-list builder, and a rolling archive of recent visits.
- **Idle workshops** — top-3 idle building models with a one-click open-in-Building.
- **Worker rebalance** — suggestions like "move from Woodcutters → Brickyard" when a draining
  good has an underfilled producer.
- **Goods at risk** — sparkline + 60-second forecast cone + auto-pin button per row.
- **Race needs unmet** — capped list of `Race → need` rows with zero stockpile.
- **Orders** — active count, picked/tracked breakdown, time-pressure callouts.
- **Forest** — exploration %, active reward chases, pinned-chase reminder, best-next-chase
  pin line.
- **Cornerstones** — first three owned cornerstones; click `open ›` to jump to Draft.

### Building — what to build, who to staff, what to make
Two-column layout: scrollable list on the left, detail on the right.
- The search box matches against display name, model name, and tag chips. Bold marks the
  matching substring; an empty query means "show everything (grouped by Kind)". Fuzzy
  fallback fires if the literal query produces zero hits (`blkpr` → `Bakery Press`).
- The **idle workplaces banner** above the search box rolls up every idle building with its
  root cause (`unstaffed`, `no inputs`, `output full`, `paused`, `worker rebalance`).
- Once a building is selected:
  - `cycles` shows total + last-5-min delta + per-minute rate.
  - `uses` chips filter the recipe list to only those consuming a given input. A red chip
    means that input is currently draining.
  - `tags` chips drop the tag back into the search field for one-click filtering.
  - **Race fits** ranks workers by perk weight against this building's tag.
  - The recipes list is sorted by throughput, profit, or input availability via the `sort:`
    chips. The top-throughput recipe gets a `★` and a one-tap `pin top` button on the header.
  - Each recipe card shows live throughput, profit per cycle, input stockpile vs need,
    alternative producers, and a `▸ why` button for the math. The `☆ pin` toggle adds the
    pair to Home; `⛔ stop` and `★ pri` markers are UI-only flags surfaced on the Home pin
    row. A `→ buy from current trader` button appears when a draining input is sold by the
    trader currently in your village.

### Good — production paths, runway, prices, traders
Two-column layout again. The detail pane shows:
- **Trade value** + **flow line** with arrow (`↑ ↓ ≡`) and runway in minutes.
- **What-if burn slider** (0.5×–2×) — drag to project runway under a hypothetical change in
  consumption.
- **Price history** sparkline for the current trader's visit.
- **Production paths** ranked by cost-per-output. Non-cheapest rows show their throughput
  delta vs the cheapest path. Filter chips: `fuel`, `eatable`, `draining`.
- **Consumers** annotated with each recipe's live share of consumption.
- **Trader rotation** (current + next) with desires ranked by total settlement value,
  affordability vs your sell pot, and per-desire unit breakdown.
- Right-click any good in the list to copy its model name to the clipboard.

### Villagers — race resolve, dietary plan, best workplaces
Pick a race from the list to surface:
- Resolve range, starting value, hunger tolerance.
- **Needs supplied** — per-need stockpile, colour-coded.
- **Resolve forecast** — coarse "minutes to target" estimate.
- **Live resolve bar** + 60-second sparkline.
- **Top resolve contributors** — filterable by free-text. Each line shows total impact and
  per-stack value.
- **Resolve goal calculator** — type a target value to see how many additional stacks of
  each top contributor would close the gap.
- **Race characteristics** — every building tag the race has perks for.
- **Best-fit workplaces** — ranked list of catalog buildings whose tag matches a race
  characteristic.

### Orders — track, plan, deliver
Active reputation orders, sorted: tracked first, then picked, then time-pressure (failable +
low time-left), then by name.
- **Tier filter chips** (bronze/silver/gold) scope the list. Empty filter = no scoping.
- **Bulk track / untrack** buttons apply to whatever's currently visible.
- Each order card shows status, tier badge, time left (red <1m, amber <5m), and a
  `↗ in-game` button that asks the game to focus the order in the vanilla UI.
- Objectives with `X / Y` patterns get a progress bar plus a sparkline of recent samples and
  an ETA at current burn.
- **Plan of attack** lines (top-2 catalog producers + 2-step input chain) appear under
  tracked failable orders.
- For unpicked orders the panel renders the pick options with reward score, category badges,
  diff lines (`only here:`), and a `▸ what-if` expander that shows Δscore vs the best pick
  plus per-reward weight breakdown.

### Glades — forest exploration, reward chases
Dashboard for forest pacing.
- Discovered count + danger distribution chart.
- Per-chase rows with `mins:secs left`, elapsed %, reward preview, and a `☠ doomed` chip
  when remaining < 30s and elapsed > 80%.
- **Resolved chases** session log.
- Pinned chase persists between sessions; the next-chase line offers a `pin` button.

### Draft — cornerstone pick popup
Auto-switches when the in-game pick popup opens. Each option shows:
- Sprite icon + display name + synergy score.
- Description + synergy components (with `▸ why` math).
- `delta:` line summarising new tags + affected buildings.
- `would affect (tag): names` for the first two newly-targeted tags.
- `skip cost:` line — what you lose by passing on this option (synergy delta vs avg + lost
  unique tags).
- `▸ vs` opens a side-by-side comparison with every other option; the button's tooltip
  summarises the synergy delta against the best alternative.
- A green `★ recommended pick:` headline appears when one option scores ≥ 1.5× the
  runner-up.
- **Currently owned** + **previously drafted** sections at the bottom. The previously-drafted
  list has a free-text filter that persists across sessions.

### Settings (`⚙`) — configure everything
Free-text filter at the top scopes the listing.
- **General** — show recommendations, visible-by-default, hide-empty-recipe-buildings, plus
  a `reset general` button.
- **Tabs** — every tab toggle is reflection-driven, so new tabs appear automatically. A
  `reset tabs` button restores compiled-in defaults.
- **Hotkey** — rebind the toggle. Press `Esc` to cancel.
- **Catalog** — counts + a `reload catalog` button (mirrors `F5`).
- **Risk thresholds** — the "goods at risk" runway threshold (in minutes) and race-ratio
  targets (`race=pct,…`). `reset risk thresholds` reverts both.
- **Config sync** — export the bool/string portion of your config to JSON in the clipboard,
  or import from clipboard.
- **Pin presets** — name and save your current pin list, then load or delete by name.
- **Docs** — embedded README, AGENTS, and **this user guide** (USER_GUIDE.md), all readable
  offline from inside the panel.

### Diagnostics (`⚙?`) — for bug reports & perf
Off by default. Toggle visible under `Settings → Tabs → Diagnostics tab`.
- **Catalog-diff banner** when the embedded catalog hash changes.
- **Session stats** — settlement age, orders completed, cornerstones drafted.
- **Per-section p50/p95** frame cost across the last 120 frames.
- **Crash dumps** + **snapshots** on disk — `copy paths` and `open dir` buttons.
- **Save snapshot** button — writes the current panel state (pins, markers, archive,
  recent log lines) to a text dump alongside the BepInEx config.
- 200-line plugin log ring buffer with copy/clear.

The tab strip also gets a one-line warn/err alert chip when the plugin emits warnings or
errors in the last 60 seconds; clicking it opens this tab.

### Embark — pre-settlement helper
Off by default. Surfaces catalog-only context that's useful before clicking through embark:
- Race table sorted by min resolve.
- Starting goods ranked by need overlap × trade value.
- Cornerstone-tag advisory ranking the highest-leverage tags for the race set.
- Live weather/season hint (rendered when a settlement is loaded).

## Common workflows

### "I'm running out of fuel"
1. Open Home (`Ctrl+1`).
2. The Fuel section will already be red. Click `☆ auto-pin top fuel producer` if it appears.
3. If you need more buffer, jump to Good (`Ctrl+3`), select the fuel good in question, and
   open the **flow breakdown** to see who's eating the most.

### "A trader just arrived"
1. The panel auto-jumps to Good and selects the trader's top desire.
2. Cross-check the **affordability** line: how much currency you'd net by selling the top-3
   desires.
3. Use the **buy-list builder** in Home → Trade to tick the things you actually want to
   spend on, and watch `pot − cost` settle to a healthy positive.
4. Goods missing from your stockpile that the trader sells will surface as `→ buy from
   current trader` chips on Building tab recipe cards if the input is at-risk.

### "The cornerstone draft popup is open"
1. The panel auto-switches to Draft.
2. Read the `★ recommended pick:` headline if it's there. Otherwise scan the synergy
   numbers and `would affect (tag):` lines to find the option that touches the buildings
   you actually have.
3. Click `▸ vs` on the option you're leaning toward to see Δscore + unique tags vs the
   alternatives.
4. The `skip cost:` line under each option summarises what you lose if you don't pick it.

### "An order is failing"
1. Open Orders (`Ctrl+5`). Tracked failable orders sit at the top.
2. The objective progress bar + sparkline + ETA tell you whether you'll make it at current
   burn.
3. The `plan of attack:` line lists the top-2 producer recipes and the upstream chain. Pin
   the highest-throughput one from the Building tab.
4. The `⚠ storm hits in Xm` warning appears when the next storm phase will start before the
   order's deadline.

### "I want to plan an embark"
1. Toggle the Embark tab on under Settings → Tabs.
2. Use the race list to read each race's resolve floor and needs.
3. The starting-goods list is your "bring these on day 1" cheat sheet — it weights need
   overlap by trade value.
4. The cornerstone-tag advisory previews which tags will pay off most for your race mix.

## Troubleshooting
- **Panel won't open.** Check `Settings → Hotkey` — the toggle key may have been rebound.
  As a fallback, set `Visible By Default = true` and reload the save.
- **Tabs are missing.** Settings → Tabs lists every toggle; some (Diagnostics, Embark) are
  off by default.
- **Wrong/no values after a game patch.** Click `Settings → Catalog → reload catalog` (or
  press `F5`). If a banner says the catalog drifted, regenerate using the JSONLoader steps
  in README.md.
- **Recommendations feel wrong.** Disable them entirely under `Settings → General → Show
  recommendations` for a neutral data view; the underlying numbers don't change.
- **Bug report.** Open Diagnostics, click `save snapshot`, then `copy paths`. Attach the
  resulting `stormguide-snapshot-*.txt` to the report.

## See also
- `README.md` — feature list, build & install, packaging, dependencies.
- `AGENTS.md` — agent / contributor instructions for the codebase.
