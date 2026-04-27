# Game API Surface Notes
Decompiled with `ilspycmd -p` against `Assembly-CSharp.dll`. Source tree lives under
`research/AssemblyCSharp/` (not committed; regenerate from a current game install
when AtS patches).
## Singletons / static accessors
- `GameMB` — main game controller. Common entry points:
  - `GameMB.GameInputService.PickedObject` (UniRx `IReactiveProperty`) — the currently-selected map object. Subscribe for selection events; no Harmony needed.
  - `GameMB.ModeService` — mode (build / move / harvest / fire / select…).
  - `GameMB.CornerstonesService` — cornerstone draft state.
  - `GameMB.GameBlackboardService` — game-wide event bus (UniRx subjects).
  - `GameMB.GameModelService.GetEffect(string id)` → `EffectModel`.
  - `GameMB.CalendarService` — year / season / quarter.
- `Serviceable.*` — settlement-scoped services:
  - `Serviceable.StorageService.GetStorage().GetAmount(string goodName)` — current stockpile.
  - `Serviceable.StorageService.GetStorage().IsAvailable(name, amount)`.
  - `Serviceable.Settings.Goods` — array of `GoodModel` (live-game catalog).
  - `Serviceable.HearthService` / `NeedsService` / `ActorsService`.
- `MB.Settings` — global game settings; `GetCornerstonesViewConfiguration(...)` etc.
## Building selection (Phase 3 live joins)
- Abstract base: `Eremite.Buildings.UI.BuildingPanel`
- Concrete: `WorkshopPanel`, `ProductionBuildingPanel`, `HousePanel`, `HearthPanel`,
  `StoragePanel`, `CampPanel`, `FarmPanel`, `MinePanel`, `CollectorPanel`,
  `FishingHutPanel`, `GathererHutPanel`, `InstitutionPanel`, `RainCatcherPanel`,
  `BlightPostPanel`, `ExtractorPanel`, `DecorationPanel`, `RelicPanel`,
  `ShrinePanel`, `AltarPanel`, `PortPanel`, `PoroPanel`.
- Hooks:
  - `BuildingPanel.SetUpBuilding(Building)` — patch **postfix** to learn "panel opened with this building". `currentBuilding` is also exposed as a public static.
  - `BuildingPanel.Hide()` — postfix for "panel closed".
- Building name lookup for catalog joins:
  `building.BuildingModel.Name` (or `.displayName.Text` for human-readable).
## Cornerstone pick popup (Phase 5)
- Class: `Eremite.View.HUD.RewardPickPopup`
- Open trigger: `GameMB.GameBlackboardService.OnRewardsPopupRequested` (UniRx
  `IObservable<Unit>`). Subscribe instead of patching `Show()`.
- After-show event: `GameMB.GameBlackboardService.OnCornerstonesPopupShown.OnNext()`.
- Read state: `GameMB.CornerstonesService.GetCurrentPick()` → `RewardPickState`
  with `options : List<string>` (effect ids).
- Each option becomes an `EffectModel` via `GameMB.GameModelService.GetEffect(id)`.
- Other popup variants we may want later:
  - `CornerstonesLimitPickPopup` — when over the cornerstone draw cap.
  - `OrderPickPopup` — order rewards.
  - `CycleEffectsPickPopup` — between-cycles effects.
  - `ReputationRewardsPopup` — the blueprint pick on reputation thresholds.
## Settlement state (live joins)
- Stockpile: `Serviceable.StorageService.GetStorage().GetAmount(goodName)`.
- Production rates: derive from `ProductionBuilding.workers`, recipe progress, and
  hearth fuel modifiers (TODO: trace exact path; `BuildingWorkersPanel` is a
  starting point).
- Trader rotation: `Eremite.Services.TradeService` (haven't read it yet).
## Plan to wire live joins (next step)
1. New `LiveGameState` adapter exposing pure read-only methods over the above
   accessors. One reflection-light file per concept (selection, stockpile,
   trader). Defensive try/catch — log+disable on missing symbols.
2. Subscribe to `GameMB.GameInputService.PickedObject` once `MainController.Activate`
   (or similar) fires — feed into `BuildingProvider`/`VillagerProvider`.
3. Subscribe to `GameMB.GameBlackboardService.OnRewardsPopupRequested` to
   auto-switch the panel to the Draft tab.
## Reflection note
We can't reference these types yet because StormGuide's csproj only `Reference`s
the game DLL with `Private=false`. That's already enough to *resolve* them at
compile time. So the patches/subscribes can be plain typed code, no reflection.
