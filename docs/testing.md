# In-game test checklist (not run yet)

Log: `<Sea Power>\BepInEx\LogOutput.log`. Config: `BepInEx\config\dev.sewerlabs.seapowersensorviews.cfg`.
Install the local build with `tools\package.ps1 -Install` (game closed). Needs a mission with a ship or two, contacts, and for the
sonar a submarine or a ship with a hull sonar. Do it on 0.8.7 and on the 3D-globe build (Anchor Chain needs the dev shim there).

## Start-up (both versions)
1. Log: `Sea Power: R.A.T.S. Sensor Views 0.1.0 loaded.`; after a mission loads: `Windows registered: ...`. No error line.
2. Bottom bar: three buttons Board / Scope / Sonar right of the unit status toggle; the map and other layers still reach the right edge.

## Sensor board
1. Click Board (or Ctrl+F4): a normal game window opens (header, X, resize). Grid lists your units and contacts; cells show letters.
2. Turn a radar off / on: its letter disappears from the cell, comes back; a lost contact stays dimmed with a growing age.
3. Click a cell: details below; click a unit column: its sensors listed. Select another unit in the game: the panel follows.
4. Mouse wheel scrolls many contacts.

## Radar scope
1. Open Scope (Ctrl+F6). Contacts appear at the right bearing and range relative to the unit; compare with the game map.
2. Wheel and +/- change the range; Heading up rotates; the altitude slider moves the horizon ring (about 1.23 * (sqrt h1 + sqrt h2) nm, heights in ft).
3. A unit with a limited search-radar arc shows a shaded blind sector. Fire-control lock: red ring and LOCK. ESM contacts: blue bearing line.
4. Let an enemy radar paint your ship: an RWR strobe at its bearing for a few seconds. Jamming: a JAM strobe.

## Sonar display
1. Open Sonar (Ctrl+F8) with a submarine selected. Passive contacts leave traces at their bearing relative to the bow; the baffle sector is shaded.
2. Raise speed: the own noise number rises. Depth strip: layer line, own depth marker, towed array marker when deployed, seabed.
3. < and > cycle the sonars; each keeps its own history.

## Pop-Out Windows (if installed)
Each window shows the pop-out button, moves to monitor 2 and keeps drawing; the bottom bar button stays lit.

## Not verified offline - look for these
- `{DynamicResource WindowLayoutNoScroll}` accepted as the window template (log line `using the default window layout` if not).
- Noesis resolves `SeaPowerSensorViews.Ui.*` by name (an error at the first open if not).
- Sign convention of the baffle sector (`_baffles`; starboard positive assumed from `Sonar.IsInBaffles`).
- Hot keys Ctrl+F4/F6/F8 do not collide with a game binding (none in the default key list).
- XAML keys and names (`DisplayAreaGrid`, `SimpleToggleButton`, brush keys) on the globe build: only the 0.8.7 XAML was read.
