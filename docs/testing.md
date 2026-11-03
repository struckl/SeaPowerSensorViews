# In-game test checklist (run on 0.8.7 flat map on 2026-11-03: start-up, bar buttons, board, scope, sonar, sensor lines, coverage; second run 2026-11-03 for scope/sonar cursors, bearing history, depth marks, Echoes; not run: the 3D-globe build, a popped-out window, a submarine)

Both DLLs sit in the mod folder; on each game the other one logs that it stays off.
Log: `<Sea Power>\BepInEx\LogOutput.log`. Config: `BepInEx\config\dev.sewerlabs.seapowersensorviews.cfg`.
Install the local build with `tools\package.ps1 -Install` (game closed). Needs a mission with a ship or two, contacts, and for the
sonar a submarine or a ship with a hull sonar. Do it on 0.8.7 and on the 3D-globe build (Anchor Chain needs the dev shim there).

## Start-up (both versions)
1. Log: `Sea Power: R.A.T.S. Sensor Views 0.1.0 loaded` (0.2.0)`; after a mission loads: `Windows registered: ...`. No error line.
2. Bottom bar: three buttons Board / Scope / Sonar right of the unit status toggle; the map and other layers still reach the right edge.

## Sensor board
1. Click Board (or Ctrl+Insert): a normal game window opens (header, X, resize). Grid lists your units and contacts; cells show letters.
2. Turn a radar off / on: its letter disappears from the cell, comes back; a lost contact stays dimmed with a growing age.
3. Click a cell: details below; click a unit column: its sensors listed. Select another unit in the game: the panel follows.
4. Mouse wheel scrolls many contacts.

## Radar scope
1. Open Scope (Ctrl+PageUp). Contacts appear at the right bearing and range relative to the unit; compare with the game map.
2. Wheel and +/- change the range; Heading up rotates; the altitude slider moves the horizon ring (about 1.23 * (sqrt h1 + sqrt h2) nm, heights in ft).
3. A unit with a limited search-radar arc shows a shaded blind sector. Fire-control lock: red ring and LOCK. ESM contacts: blue bearing line.
4. Let an enemy radar paint your ship: an RWR strobe at its bearing for a few seconds. Jamming: a JAM strobe.

## Sonar display
1. Open Sonar (Ctrl+PageDown) with a submarine selected. Passive contacts leave traces at their bearing relative to the bow; the baffle sector is shaded.
2. Raise speed: the own noise number rises. Depth strip: layer line, own depth marker, towed array marker when deployed, seabed.
3. < and > cycle the sonars; each keeps its own history.

## Map: sensor lines (both versions)
1. Layers menu of the tactical map: entries "Sensor lines (who sees what)" (on) and "Sensor coverage (selected unit)" (off); the checkboxes follow the keys Ctrl+Shift+PageUp/PageDown.
2. With contacts held: amber lines from the ships holding them by radar end on the contact icon; a fire-control lock is thick red with "LOCK"; ESM/passive sonar: dashed bearing line and a faint wedge.
3. Select a ship: its lines bright, the others dimmed; config "Units shown" = "Selected only" hides the others. Select nothing: all bright.
4. Pan, zoom and (globe) rotate: lines stay on their places, no lag; contacts on the far side of the globe are not drawn.
5. Switch a radar off: its line turns faint for the memory time, then goes.

## Map: coverage
1. Switch the layer on with a ship selected: amber filled radar polygon, dashed blue ESM ring, dotted visual ring; for a sonar ship a green passive polygon with a small dent in the baffle sector.
2. Change the scope's altitude slider (or the config value): the radar polygon shrinks to the horizon for that altitude (a few seconds for the terrain pass). Near an island: a notch behind the terrain.
3. Select another ship: the polygons follow within about a second. Move the ship 1+ nm: they follow.
4. The log has no `terrain check` spam; the frame rate holds (rays are worked 6 per frame).

## Pop-Out Windows (if installed)
Each window shows the pop-out button, moves to monitor 2 and keeps drawing; the bottom bar button stays lit.

## Not verified offline - look for these
- `{DynamicResource WindowLayoutNoScroll}` accepted as the window template (log line `using the default window layout` if not).
- Noesis resolves `SeaPowerSensorViews.Ui.*` by name (an error at the first open if not).
- Sign convention of the baffle sector (`_baffles`; starboard positive assumed from `Sonar.IsInBaffles`).
- Hot keys Ctrl+Insert/F6/F8 do not collide with a game binding (none in the default key list).
- Sensor line / coverage colours (`NTDS.*`, `Map_RWR`, `Map_UnitOnMapRange`) and weight (1 px base) on the globe build: the map styling moved there, so the fallback colours may show.
- `RenderedLayer.OnRender` really hands over the live map on the globe, and `MapPanel` arranges the unplaced Paths over the whole map as it does for Borders.
- XAML keys and names (`DisplayAreaGrid`, `SimpleToggleButton`, brush keys) on the globe build: only the 0.8.7 XAML was read.

## Offline checks (done)
`python tools/modtool.py compat sensorviews check lint`; `dotnet run --project tools/hook-check -c Release -- --against <Managed>` for both games (the three Harmony targets, `GizmoLayerManager+Entry`, the `MapLayer` constructor, `OwnNoiseDB`).
