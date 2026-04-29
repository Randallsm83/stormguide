# StormGuide screenshots

This folder is the home for committed PNG screenshots referenced from
`docs/USER_GUIDE.md` and the Thunderstore page. It is **not** gitignored
(`tools/screenshots/` is — that folder is the staging area for raw captures).

## Capturing

`tools/Capture.ps1` takes full-screen PNGs of the running game into
`tools/screenshots/`. Defaults capture 8 frames at 4 s intervals with a
`stormguide` filename prefix:

```pwsh
pwsh tools/Capture.ps1 -Count 9 -DelaySeconds 4 -Prefix stormguide
```

Cropping/trimming should happen out-of-band — the capture script is deliberately
dumb so the agent can re-run it without reasoning about window geometry.

## Layout convention

When a capture is good enough to commit, drop the resulting PNG into this
folder using a kebab-case descriptive name that mirrors the section it
illustrates. Suggested filenames (none are committed yet — this is the slot
list):

- `home-overview.png` — the full Home dashboard with several sections expanded.
- `home-trade-timeline.png` — the trader timeline mini-bar + desire heatmap +
  buy-list builder.
- `building-recipe-card.png` — a recipe card showing reputation-order ETA + pin
  toggle + why-row expansion.
- `goods-flow-breakdown.png` — the Goods tab detail with the flow breakdown
  expanded and the price-history sparkline visible.
- `villagers-housing-variety.png` — Villagers tab race detail showing dietary
  variety + housing match + resolve bar.
- `orders-best-for-me.png` — an unpicked order with the ♥ best-for-me badge.
- `glades-clear-time.png` — Glades tab with the clear-time estimator line.
- `draft-stacks-with.png` — Draft popup option showing a `↻ stacks with: …`
  overlap line.
- `settings-diagnostics-bundle.png` — Settings tab scrolled to the Diagnostics
  bundle section.
- `diagnostics-perf.png` — Diagnostics tab showing per-section p50/p95.

## Referencing from the user guide

Use plain markdown image syntax with descriptive alt text — the alt text is
what shows in the in-panel doc viewer (which renders plain text only), while
GitHub renders the image inline:

```markdown
![Home tab showing the trader timeline mini-bar, desire heatmap, and buy-list
builder with `pot − cost` settled positive](docs/screenshots/home-trade-timeline.png)
```

Keep one image per concept and avoid embedding many large PNGs — the
`USER_GUIDE.md` is also embedded into the plugin, and the embedded copy doesn't
strip image references, so disk size in the shipped DLL grows with this
folder's mass.
