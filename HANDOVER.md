# Sea Power Sensor Views – handover

Written 2026-10-30 by the Claude session that did the analysis (it is switching accounts; this file is everything the next
session needs). Read this file first, then `docs/`, then open `mockups/index.html` in a browser.

**Status: nothing is built yet.** No code, no GitHub repo, no Workshop item, nothing deployed, `mods.json` not updated. This
folder holds research, mockups and a plan. Lukas's last question was "should we start with the sensor board?" – he had not
answered yet when this was written; my recommendation (below) is yes.

## 1. What Lukas wants (his words, shortened)

- 2026-10-28, after seeing the Steam Workshop mod *Better TacMap* (item 3768036424, author Bananaduck): "I think we can build
  something like that and build on that or expand it, with all the overlays and radar and so on."
- Before that, the idea that started it: when a ship is selected it has radar / sonar / ESM, and it is hard to see **which ship
  sees what with which sensor**. With the Pop-Out Windows mod there is now a whole second monitor to fill with views – "something
  like a radar or sonar view" – and I should bring my own ideas.
- I analysed what is possible (section 4, `docs/`), drew five mockups (`mockups/`) and recommended a **new mod next to Better
  TacMap, not built on its code** (section 3). Working title: **Sea Power: Rats Sensor Views**, folder/repo/install folder
  `SeaPowerSensorViews`, plugin GUID `dev.sewerlabs.seapowersensorviews` (follow the naming rule in `../mods.json`).

## 2. The five views (mockups are in `mockups/`)

| # | View | What it shows | Where it lives | Risk |
|---|---|---|---|---|
| 1 | **Sensor board** | grid: your units × contacts; each cell lists the sensors holding that contact (R radar, F fire control, E ESM, S passive sonar, A active sonar, V visual/IR) with fix age and "bearing only"; below it the selected unit's sensors (on/off, EMCON, channels, arcs) | a game window (pop-out-able) | low |
| 2 | **Sensor lines on the map** | thin lines from each own unit to each contact it holds, styled by sensor; ESM / passive sonar bearing wedges; fire-control lock thick red; the selected unit's lines bright, others dimmed | map overlay | medium (globe projection) |
| 3 | **Radar scope** | ship-centred scope, contacts fading with fix age, blind arc, radar-horizon ring that follows a target-altitude slider, strobes for radars painting you (RWR) and jammers, lock marker | a game window | low–medium |
| 4 | **Sonar display** | bearing-over-time waterfall (traces brighten with signal excess, baffle sector shaded, own speed raises the noise floor), depth strip (surface, layer, own depth, towed array, seabed) | a game window | medium |
| 5 | **Coverage on the map** | for the selected unit: radar coverage against a chosen target altitude with terrain shadows, ESM listening range, visual range, passive sonar range with a baffle dent | map overlay (reuse Topo's overlay renderer) | high |

Order I recommended: **1 → 3 → 4 → 2 → 5**. Windows first because they do not touch the map, so the 3D-globe change does not affect
them and they pop out to monitor 3 for free; the map parts last because they need the globe-specific work Borders/Topo already did.

**Fair-play rule (important):** only show what the player's side actually knows. `TrackComponent.TrueTargetPosition` is
ground truth – never display it. Use the track's bearing/range estimates. Predictions against an *imagined* target (coverage,
radar horizon, sonar range) are fine: the game's own Sensor Info window already does exactly that.

## 3. Better TacMap – why alongside, not on top

Details in `docs/better-tacmap.md`. Short version: by Bananaduck, ~5,600 subscribers, needs Anchor Chain. Right-click threat circles
and firing arcs, info labels (TTG, depth, speed, altitude), distress beacons, waypoint TTG, intercept solver, scaled ship
silhouettes. **No licence stated → all rights reserved; do not copy or fork its code** (a decompiled copy exists in
`E:\Project\Mods\Sea Power Lab\_decomp\ws_BetterTacMap` for understanding only). It plugs into the game's map layer system, which
changed in the globe build, and Steam shows "removed … incompatible with Sea Power" banners on its page (an anonymous fetch could not
tell whether it is live). It covers weapons/kinematics; Sensor Views covers sensors/detection – complementary, almost no overlap
(only weapon-range circles and a small "above/below the layer" note for submarines). Later a combined view (coverage next to threat
circles) is possible.

## 4. What the game offers (verified in code – details in `docs/recon-*.md`)

- **Contacts:** every unit has its own `PlottingTable` (`unit.PlottingTable`), one more is shared per side. A contact is a `Vehicle`;
  its tracks (`TrackComponent`) record `Source` (the unit), `Sensor` (which sensor system), `SensorType`, `Epoch`, `MaxAge`,
  bearing / range / altitude estimates with error. So "who sees what with which sensor" is directly readable. Each `ISensor` is also an
  `IObservable<Entity>` (`Sonar.Subscribe`, `SensorSystemVisual.Subscribe`, …) – subscribe to get every track live, read it inside
  `OnNext`. Fusion and TMA run in Burst/ECS (not patchable); all scans and range maths are managed C#.
- **Detection maths is callable for an imagined target:** `RadarCalculator.GetFCRDetectionRange(...)` (radar equation + jamming),
  `RadarCalculator.getRadarHorizon(radarY, targetY, visual, terrainAlt)`, `SensorUtils.CalculateMaxVisualRange(...)`, ESM closed form in
  `SensorSystemESM.cs`, sonar `SonarContactMath.Evaluate(in SonarContactData)` (pure struct maths, safe off the main thread) with
  `SonarAcoustics.*`. The game's `SensorInfoViewModel.PlotSonar/PlotRadar/PlotVisual` is the ready-made recipe for building the inputs.
- **Terrain line of sight:** `TerrainUtils.IsLineOfSightBetween(GeoPosition, GeoPosition, ObjectBase = null, ObjectBase = null)` returns a
  `LineOfSightResult` (checked: exists with this signature in both game builds; main thread only).
- **Not what you'd expect:** radar sweep is cosmetic (detection does not depend on beam azimuth; the antenna mount does rotate, so
  `_mountObject.transform.eulerAngles.y` is a live bearing); sonar "max range" fields are cosmetic (detection is pure signal excess, no
  ping interval); there is **no sonar waterfall anywhere in the game**; the map bearing fans for passive sonar/ESM exist but are off
  behind `DM_Sensors._allowTMAFans` (default false).
- **Extras for the scope:** `unit._rwrBearings` / `_jamBearings` (cleared each datalink cycle – snapshot before; postfix point
  `ObjectBase.RegisterRadarLockWarningSource`), fire-control state `_isGuiding / _associatedTargets`, `unit._ECMSystemsTryingToJamMe`,
  `ESM._esmObservationData`.
- **Units:** 1 Unity unit = 67.2 m, 1 nm = 27.56 units, 1 km = 14.88 units; `RadarParameters` ranges/altitudes are in Unity units; ft → Unity
  units ×0.00453571. Radar horizon (4/3 earth) ≈ 1.23·(√h₁ ft + √h₂ ft) nm – used in the mockups only as an approximation.
- **Thermal layer:** Topo already has it (`SeaPowerTopo/src/SeaPowerTopo/Terrain/LayerMap.cs`).

## 5. Verification status – read before trusting any line number

The two research reports (`docs/recon-radar-esm-visual.md`, `docs/recon-sonar.md`) were written by sub-agents from the decompiled
**0.8.7** sources; I spot-checked these entry points and they exist in **both** `_gamerefs/decomp-0.8.7` and `_gamerefs/decomp-beta`
(globe build 26223125): `SonarContactMath.Evaluate`, `RadarCalculator.GetFCRDetectionRange`, `RadarCalculator.getRadarHorizon`,
`TerrainUtils.IsLineOfSightBetween(GeoPosition,…)`, `SensorInfoViewModel`, `ObjectBaseParameters._sonarSystems`,
`ObjectBase.PlottingTable`, `ObjectBase._rwrBearings`, `Environment.getLayerHeight`, `DM_Sensors._allowTMAFans`,
`Sonar.Subscribe(IObserver<Entity>)`. **Line numbers moved by roughly +30 in the globe build.** Everything else in the reports is
unverified; re-check each member you use against **both** decompiles, and – per the workspace lesson – check hook targets and fields **by
reflection**, not only by compiling (a field that became a property compiles fine and breaks at run time).

## 6. Rules that apply (full text: `../CLAUDE.md`, `../docs/both-versions.md`)

- Start in `E:\Project\Mods\Sea Power`, run `python tools/modtool.py status` first. Game folder holds the **globe build 26223125** since
  2026-10-27; the flat 0.8.7 refs are in `_gamerefs\SeaPower-0.8.7-public`.
- **Both game versions (his rule):** one DLL if nothing referenced differs, else two builds of one tree (`-p:GameBuild=Flat`,
  `FLATMAP`, `…Flat.dll`, plugin GUID `<guid>.flatmap`, entry gate on `Type.GetType("SeapowerUI.MapDisplayState, Seapower-Scripts")`,
  body in a `[NoInlining] Start()`). A DLL that stays off must still be class-loadable on the other game (use `object`/`Delegate`
  fields, see Topo's `GameApi.cs`). `python tools/modtool.py compat` must pass before any release; never claim "works on both" in a
  public text before an in-game test.
- **Anchor Chain 1.1.0 does not start on the globe build** (AnchorChain#19; our fix is PR #20, unmerged). Globe testing needs Topo's
  dev shim (`SeaPowerTopo/tools/anchorchain-beta-shim`). On 0.8.7 everything loads normally.
- **The game is his.** Never start/close it, never send clicks/keys without his OK; he plays while you work and tests himself. Other
  sessions share the game ("taking the game now" / "done"). Never pop up windows or send simulated keys while he works. Load missions
  only from a settled main menu. Notes: `docs/game-testing.md` in the workspace.
- **Git:** repo-local identity `MasterChickenHD <30601936+struckl@users.noreply.github.com>`, never his gmail; **no `Co-Authored-By`
  trailer**; keep README + CHANGELOG in step; push so `git status` is clean. New repos are created **private**, flipped to public only
  on his OK. The workspace repo git-ignores the mod folders.
- **Public texts** (Workshop, Reddit, README, credits): title "Sea Power: Rats <Name>"; no AI wording; say what is display-only; he posts
  to Reddit/Steam/Discord himself. Workshop work only through `python tools/modtool.py workshop …` (running Steam client, never start the
  game); outward steps print a plan and need `--yes` after his OK. First upload hidden.
- `_gamerefs` (game files) never committed or published.
- Style he likes: lead with the answer and a recommendation, research things himself instead of handing lookups back, trust what he
  names ("assume it exists, look it up").

## 7. Plan (proposed – confirm with him before the outward steps)

**M0 – scaffold (no game needed).** Copy the layout of the smallest sibling (`../SeaPowerPopOut`: `src/`, `tools/package.ps1`, `docs/`,
`workshop/`, `CHANGELOG.md`, `LICENSE` MIT, `README.md`), `git init -b main` with the noreply identity, register in `../mods.json`,
build for both games, `modtool compat` green. Create the GitHub repo **private** only after he agrees.

**M1 – data layer + sensor board window.** `SensorPicture` (read-only snapshot of tracks per own unit per sensor, refreshed ~1 Hz, uses
`PlottingTable.VehicleForObject(c).CurrentTracks()` and filters `TrackComponent.Sensor == sensorSystem.InstanceEntity`; optional
`Subscribe` on sensors) and the board window. **First thing to research:** how a mod adds a *new* window type to the game's `WindowHost`
(a view-model class + a `DataTemplate` registered in the main view's resources + `WindowHostCommands.OpenWindowCommand`; see
`../SeaPowerPopOut/src/SeaPowerPopOut/PopOutScreen.cs` (it keys everything on the content's view-model type name, so any new window
should get the pop-out button for free), `../SeaPowerPopOut/src/SeaPowerPopOut/SmartWindows/`, and Radio's `WhyNot/QueueOverlay.cs` as precedent for an own window; fall
back to Noesis elements laid over the map as Topo's `Ui/MapDecoration.cs` does). Also decide how it is opened (a bottom-bar toggle? a key? an
entry in the unit's context menu?). Likely one DLL for both game versions – prove it with `compat`.

**M2 – radar scope** (own window; sweep decoration + age fade; RWR/jam strobes; horizon ring from `getRadarHorizon`).

**M3 – sonar display** (waterfall from `RawPassiveSonarReadings` / sonar `Subscribe`; baffle sector from `SonarParameters._baffles`; depth
strip from `Environment.getLayerHeight`, sub depth, towed-array state).

**M4 – map lines** (two builds; draw like Borders' `Map/ProjectedLines.cs` / Topo's overlay; Layers-menu entry via Topo's `GameApi.NewLayer`;
possibly switch on the game's own bearing fans via `DM_Sensors._allowTMAFans`).

**M5 – coverage overlays** (reuse Topo's `OverlayRenderer`/`Cutout`; compute rays on a worker thread, terrain via `IsLineOfSightBetween` on the
main thread in slices; ≤1 Hz refresh).

**Release:** package, README/CHANGELOG, `modtool check lint compat`, tests in the game on his go (flat and globe), Workshop hidden → public on his OK.

Performance notes from the research: each sensor scan is O(all objects); coverage = 360 bearings × ~8 raycasts, recompute at ~1 Hz or on
change; sonar `Evaluate` ×36k for 360° × 100 ranges → worker thread, but sample `TerrainUtils.GetHeightAt` and `Environment` getters on the main
thread first; `Environment.getXLayerAttenuation()` reseeds `UnityEngine.Random` – call once and cache.

## 8. Open questions for Lukas

1. Go for the new mod and start with the board (M0–M1)? (my recommendation: yes)
2. Name: "Sea Power: Rats Sensor Views" OK?
3. How should the board / scope open – bottom-bar toggle, a key, the unit's context menu?
4. Should map lines show for all own units or only the selected one by default? (mockup: selected bright, others dimmed)

## 9. State of related work (so you do not redo it)

- **SeaPowerTopo** (public, 1.4.1, Workshop 3826122523) already contains everything built in the preceding conversation: pre-drawn mission-area
  layers, parallel colouring, the submarine thermal-layer / keel-clearance overlay (`Terrain/LayerMap.cs`, `Render/Cutout.cs`, `OverlayRenderer.cs`) and the
  globe/flat split (`GameApi.cs`). The old copies in `E:\Project\Mods\Sea Power Lab\…\worktrees\…` (branches `claude/topo-wide-surround`, `claude/popout-free-windows`) are superseded.
- **SeaPowerPopOut** (public, Workshop 3826396311): see-through pop-out monitor, pointer fix, wide resize corners, snapping – in its repo.
- **SeaPowerBorders** public since 2026-10-28; **SeaPowerRadio** hotfix 1.0.5 live. Memory notes of these live in
  `C:\Users\sgtmr\.claude\projects\E--Project-Mods-Sea-Power\memory\` (`sea-power-workspace.md`, `game-update-2026-10-27.md`, `both-game-versions-rule.md`, …).
- Game-testing helpers: `../tools/game-driver/`, Shot Director (in `../SeaPowerTopo/tools/shot-director`), notes in `../docs/game-testing.md`.

## 10. Files in this folder

| File | What |
|---|---|
| `HANDOVER.md` | this file |
| `README.md` | project stub (status: planning) |
| `docs/recon-radar-esm-visual.md` | radar, ESM, visual/IR model, callable entry points |
| `docs/recon-sonar.md` | sonar and acoustic model, callable entry points, existing sonar UI |
| `docs/better-tacmap.md` | what Better TacMap does, licence/compat status, overlap |
| `mockups/*.html` | five standalone mockups (invented data) + `index.html` |
