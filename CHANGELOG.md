# Changelog

## 0.2.0 - unreleased

- Sonar display: a measuring cursor over the waterfall (bearing true and relative to the bow, seconds ago) and a notice when the selected sonar is switched off. Compiles for both builds, not yet run in the game.
- Performance: the bar buttons no longer re-ask the game's window commands on every mouse move and key press; map paths are rebuilt at most 20 times a second while the view moves; the coverage terrain pass takes at most about 1 ms per frame. With *Verbose log* on, the log gets a timing line per part every 10 s.
- Run in the game on 0.8.7 (flat map, 2026-11-03): the three windows, the bar buttons, the sensor lines and the coverage layer work; the 3D-globe build is not run yet.
- Fix: the bar buttons' labels (Board / Scope / Sonar) were dark on the dark bar; they are grey now, orange while their window is open.
- Fix: the sonar window kept showing "no sonar"; its overlapping labels are moved. The scope's tool row wraps in a narrow window (Heading up was cut off). The board's selected unit column is a light tint with an underline.
- Fix: the mod did not start (no bar buttons) because one config key held an apostrophe, which BepInEx refuses; the key is now *Dimmed line opacity*.
- **Map layers** in the map's Layers menu: *Sensor lines (who sees what)* and *Sensor coverage (selected unit)* (run in the game on 0.8.7).
  - Sensor lines: from each own unit to the place each of its sensors puts a contact (track estimates only). Search radar amber, fire-control
    lock thick red and labelled, active sonar blue, visual dotted grey; ESM and passive sonar as a bearing line with a wedge. The selected
    unit's lines are bright, the others dimmed (or left out: setting "Units shown"). Lost contacts keep a faint line for a while.
  - Coverage of the selected unit against an imagined target: radar with terrain shadows (the target altitude is the scope's slider), ESM,
    visual and passive sonar with the baffle dent.
- Two DLLs from one source tree, `SeaPowerSensorViews.dll` (3D-globe build) and `SeaPowerSensorViewsFlat.dll` (0.8.7), each staying off on the other game;
  the map layers differ between the two games. Checked offline against both games' assemblies; run in the game on 0.8.7, not on the 3D-globe build.
- Hot keys now avoid the F-keys (the game's free camera would still pass `Ctrl+F6`/`F8` on): `Ctrl+Insert` board, `Ctrl+PageUp` scope, `Ctrl+PageDown` sonar,
  `Ctrl+Shift+PageUp` sensor lines, `Ctrl+Shift+PageDown` coverage.

## 0.1.0 - unreleased

- Sensor board, radar scope and sonar display as windows of the game's own window system (run in the game on 0.8.7).
- Bottom bar buttons and configurable hot keys to open them.
- One DLL for both game versions at this stage, checked offline against both games' assemblies.
