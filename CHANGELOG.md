# Changelog

## 0.2.0 - unreleased

- Fix: the mod did not start (no bar buttons) because one config key held an apostrophe, which BepInEx refuses; the key is now *Dimmed line opacity*.
- **Map layers** in the map's Layers menu: *Sensor lines (who sees what)* and *Sensor coverage (selected unit)* (not yet run in the game).
  - Sensor lines: from each own unit to the place each of its sensors puts a contact (track estimates only). Search radar amber, fire-control
    lock thick red and labelled, active sonar blue, visual dotted grey; ESM and passive sonar as a bearing line with a wedge. The selected
    unit's lines are bright, the others dimmed (or left out: setting "Units shown"). Lost contacts keep a faint line for a while.
  - Coverage of the selected unit against an imagined target: radar with terrain shadows (the target altitude is the scope's slider), ESM,
    visual and passive sonar with the baffle dent.
- Two DLLs from one source tree, `SeaPowerSensorViews.dll` (3D-globe build) and `SeaPowerSensorViewsFlat.dll` (0.8.7), each staying off on the other game;
  the map layers differ between the two games. Checked offline against both games' assemblies, not run in the game.
- Hot keys now avoid the F-keys (the game's free camera would still pass `Ctrl+F6`/`F8` on): `Ctrl+Insert` board, `Ctrl+PageUp` scope, `Ctrl+PageDown` sonar,
  `Ctrl+Shift+PageUp` sensor lines, `Ctrl+Shift+PageDown` coverage.

## 0.1.0 - unreleased

- Sensor board, radar scope and sonar display as windows of the game's own window system (not yet run in the game).
- Bottom bar buttons and configurable hot keys to open them.
- One DLL for both game versions at this stage, checked offline against both games' assemblies (not run in the game).
