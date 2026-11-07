# Changelog

## Unreleased

- Fix: zooming in on the map with the sensor lines on made the game crawl or freeze, worse the closer the zoom (reported by many
  players). The lines, wedges and coverage are now cut to the visible map plus half a view on each side before they are drawn;
  before, zoomed in, a line ran hundreds of thousands of pixels past the screen and the game drew every dash of it. Measured in
  the game on 0.8.7 (`tools/perf-test/results-2026-11-07`): zoom 13 went from 61/31 fps with frames up to 0.86 s to 74/72 fps
  with frames under about 50 ms; at zoom 17, 0.2.1 froze the game until Windows closed it, now 76/74 fps.
- Fix: the map lines were rebuilt once a second even in a paused game, because the change check depended on the order of the
  contacts. With *Verbose log* on, the log now says what changed when the lines are rebuilt.
- `tools/perf-test`: zoomed-in cases (zoom 13 and 17), `cases-zoom.cmd` (lines off vs on), `cases-running.cmd` (mission running,
  `-Running`), and `-CaseFile` to pick a case list.

## 0.2.1 - 2026-11-04

- Performance (measured in the game on 0.8.7, before/after in `tools/perf-test/results-2026-11-04`):
  - Map layers: when the map pans or zooms, the sensor lines and coverage now move with a transform instead of being projected and
    tessellated again (the flat map; the globe's turning view still rebuilds, at most 20 times a second). A zoomed layer is rebuilt
    once the view holds still. The lines are rebuilt only when they changed by more than about 0.1 nm, so a paused game or
    still contacts cost nothing on the map.
  - Radar scope: the sweep is its own layer that is only turned, so the scope picture is no longer redrawn 25 times a
    second. It is redrawn with each snapshot, and in between only when a radar warning or jam strobe comes or goes. The measuring
    cursor is its own layer too.
  - Sonar display: the measuring cursor is its own layer (moving the mouse used to redraw the whole waterfall), and the
    waterfall is drawn as one shape per colour and brightness step instead of one rectangle per dot.
  - The sensor snapshot keeps each unit's sensor rows and only reads their switches each second; it no longer builds every
    row and its texts again.
  - Result on the test PC (Gulf of Sidra, everything open, map moving): 73.9/71.9 fps → 80.9/76.0 fps; the mod's own time per frame
    went from about 0.2 ms to 0.03 ms. Most single cases are within the test's noise (about 5 %). The 3D-globe build compiles
    and passes the compat check but was not run in the game.
- `tools/perf-test`: the repeatable frame-time test (runner, cases, README).

## 0.2.0 - 2026-11-03

- Radar scope: the sweep redraws 25 times a second instead of 5 (smoother turning). Run in the game on 0.8.7 (2026-11-03): scope cursor, board bearing history strip, sonar measuring cursor, depth marks and the Echoes plot work; depth labels are right-aligned so they no longer clip.
- Sonar display: the depth strip shows the contacts this sonar holds at their estimated depth, with the track's error band and above/below the layer; an *Echoes* button switches the waterfall to a range/bearing echo plot (active = filled, passive with a range estimate = ring, fading with the fix age). Run in the game on 0.8.7.
- Radar scope: a measuring cursor (bearing and range from own ship). Sensor board: bearing-only tracks (ESM, passive sonar) get a small bearing history strip with the drift in degrees per minute, from the track estimates. Both compile for both builds, not yet run in the game.
- Sonar display: a measuring cursor over the waterfall (bearing true and relative to the bow, seconds ago) and a notice when the selected sonar is switched off. Run in the game on 0.8.7.
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
