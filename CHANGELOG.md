# Changelog

## 0.4.2 - 2026-11-09 - radar scope and sonar display show only what those sensors detect

Run in the game on the globe beta (build 261107) on 2026-11-09, Intro 02 Narvik: radar contacts and own units paint and fade with the sweep, EMCON empties the scope at once and passive sonar keeps listening, an own ship appears on the sonar waterfall in passive and active mode, no errors from the mod in the log. A sonar switched off or a retracted towed array, and the Echoes plot with an own ship, not run in the game yet. The flat build for 0.8.7 passes the offline checks, not run in the game.

- **A contact seen only by eye, sonar or another sensor no longer shows on the radar scope.** Only a live track from this unit's own radar does. ESM and passive sonar bearing lines left the scope too; they stay on the map, the board and the sonar display.
- **Nothing on the scope while the radar is off.** With no search radar radiating (switched off, EMCON, broken) the scope is empty and says so; blips go at once, and RWR and jam strobes show only while the radar is on. A fire-control radar alone does not count.
- **Your own ships follow the same rule.** The game makes no radar tracks of your own task force, so the diamonds used to show every ship at all times. They now appear only when this radar would detect the ship by the game's own search-radar tests (range, horizon, terrain, arcs, altitude, jamming), when the sweep passes over them. *Own ships* still hides them.
- **A blip the radar has lost goes at the next pass.** It used to flash up again on every turn of the sweep, at its old place.
- **LOCK ring in sweep mode.** A fire-control lock is now marked on the blip too; the sweep layer used to leave it out.
- Clicks on the scope pick only what the radar shows.
- **Sonar display, same idea.** Nothing new appears while the sonar is off, broken or a towed array is retracted; the rows already on the waterfall scroll away. Your own ships, which never appeared there, now show when this sonar would pick them up: the game makes no sonar tracks of your own task force, so they go through its per-contact sonar sums with the same baffles, line of sight and detection threshold. Those sums never let a surface ship hear a surface ship, so that pair uses the same equations without the block. The game's newer batch sonar model is private, so their detection ranges can differ a little from enemy contacts'.

## 0.4.1 - 2026-11-09 - corrected bearing lines, passive sonar without a range

Run in the game on the globe beta (build 261107) on 2026-11-09, Intro 02 Narvik at 68 N: radar and passive sonar lines end on the map markers, the board shows passive sonar in lower case, the Echoes plot stays empty with only passive contacts, no errors in the log. The flat build for 0.8.7 passes the offline checks, not run in the game.

- **Fix: lines pointing 5 to 20 degrees away from a contact.** Bearings to the plotted position were worked out on a great circle, while the game's sensors and the lines' own drawing use the game's map measure. The two differ more the farther from the equator (on the flat map up to about 10 degrees at 45 N and 18 at 60 N). Map lines, scope marks and the scope's click targets now use the game's measure.
- **Fix: a bearing line stayed on the sensor's old bearing after a ping.** Once radar, active sonar or another ranging sensor gives a live position, the ESM and passive sonar lines of that contact point at it.
- **Fix: passive sonar showed the exact range.** The game's passive track carries a range value that is the true distance (or nothing), and the Echoes plot, the board and the scope read it. Passive sonar is bearing-only everywhere now: it no longer appears on the Echoes plot, the board shows a lower-case S with the bearing history, the scope draws a bearing line. A passive contact the game has plotted still jumps to its map marker on double-click.

## 0.4.0 – 2026-11-09 – one button with a flyout, click a contact on the radar to jump to it, snappier map layers

Run in the game on the globe beta (build 261107) on 2026-11-09: the bottom-bar button and its flyout, the three windows from it, the map switches, coverage with terrain shadows, a click on a contact and on an own ship on the radar scope. The flat build for 0.8.7 passes the offline checks, not run in the game.

- **Snappier map layers while panning.** Lines and coverage kept their last position once the view left the area they were cut to and waited for the next rebuild (up to 50 ms, and a frame late). They now keep moving with the map in any case, the cut area is a whole view size wide on each side, and what a transform cannot follow (a turning globe, a long pan) is rebuilt in the frame the map moved, about once per frame.
- **Flyout is a panel on the map, not a popup** (the map stays usable around it); window chips follow the real window state; the flyout has the coverage options (radar, ESM, visual, sonar, terrain shadows).
- **Radar scope: click a contact to jump to it.** A click on the scope picks the contact nearest the pointer (within 14 px), rings it, and brings the game's camera to where the sensors place it (never the real object). Not run in the game yet.
- **One bottom-bar button with a flyout.** The three text buttons (Board, Scope, Sonar) are one icon button in the style of the game's own bar icons (a radar scope; peach while the flyout or a window is open). Its flyout has the three window toggles - each window still opens on its own - and the map switches: Sensor lines, Coverage, lines for the selected unit or all units, lines of radar / ESM / visual / sonar, hide tracked ESM. The same map switches are in a bar at the top of the sensor board. The six extra entries (all units, radar, ESM, visual, sonar, hide tracked ESM) are gone from the map's Layers menu; *Sensor lines* and *Sensor coverage* stay there. Built and checked offline; not yet run in the game.
- **Radar, sonar and board windows can be made smaller again.** They had no minimum size, and the game's resize code only lets a window shrink down to its minimum: with none it could grow but never shrink. Minimums now: radar 340x380, sonar and board 420x240. A size probe (verbose log only) writes the window sizes while resizing.
- **Radar scope:** the target altitude slider runs on a square-root scale (it moved by 460 ft per pixel) and has a box next to it to type the altitude in feet. The radar horizon ring is only drawn when it lies inside the scope's range (it used to run out of the window); otherwise its distance is written at the bottom. Your other units show as small diamonds with their names (checkbox *Own ships*).
- **Radar scope follows the game's clock:** with the scope open the snapshot is taken whenever the game's clock moved (at most 10 a second) instead of once a second, so lines and contacts are from the same moment as the world instead of up to a second behind a running game and exact only when paused. Board and sonar stay at one a second (the sonar waterfall takes a row per second).
- **Lines end on the map marker:** the map's sensor lines and the scope's contact marks now end at the place the game's plotting table puts the contact (the marker you see on the map: the track estimate, extrapolated between fixes) instead of at one sensor's own older bearing and range, so they no longer sit beside the marker. Still an estimate, never the real object. The map lines follow the game's clock like the scope (10 snapshots a second at most).
- **Map cones:** overlapping bearing cones of one colour cancelled each other out (blue edges, empty inside); they fill as one now. A bearing line and its cone run to the distance of the contact's marker (plus 10 %) when the game plots the contact; the 'Bearing wedge length' setting is only the fallback. Lines to ranged contacts end on the position the marker follows.
- **Cones:** a fainter fill (7 % instead of 16 %) with a firm outline, so they read on bright terrain and where they overlap.
- **Map lines follow a moving map in the same frame:** the lines and cones were moved by the next frame's update, so they trailed the map by a frame while it was dragged or zoomed; the map's viewport event moves them now.
- **Own ships on the scope** are drawn in a lighter blue (the game's friendly blue was hard to see on the dark scope).
- **Lag when the map moves:** the map lines are rebuilt at most twice a second however often the snapshot comes (the 10-a-second snapshot of the scope made every line rebuild 10 times a second).
- **Line labels** stand a quarter of the way back along the line instead of on the contact marker's own text.
- **Scope:** 'SEARCH RADAR OFF' is written over the scope when none of the unit's search radars is on (the sweep is not drawn then).
- **Quick switches in the map's Layers menu:** 'Sensor lines: all units', 'radar', 'ESM', 'visual', 'sonar' and 'hide ESM of tracked contacts' (also in the Options tab). An ESM bearing line and cone is left out for a contact that a radar holds as a live track.
- **Radar scope like a radar:** a radar contact is painted when the sweep passes over it, at its position then, and fades until the next pass; the scope's contacts from other sensors stay always current. Setting 'Radar scope: blips appear when the sweep passes' (Options tab / config) switches back to the old always-current look.
- **Period look for radar and sonar** (setting 'Radar and sonar: period look', on by default; Options tab): the scope and the sonar display are drawn like green phosphor tubes - near-black ground, dim rings, bright traces - and the radar sweep has a glowing trail. Off = the game's own colours. Applies to windows opened afterwards.
- **Sonar coverage dent faces the right way:** the baffle dent of the passive sonar coverage was worked out as if north were the bow, so it was always aft of north; it follows the unit's heading now.
- **Ranged lines show their uncertainty:** a line is solid up to the nearest range the track could have and fades on to the farthest (the range error of the track), instead of ending at one exact point.
- **Visual contacts are lost sooner:** a contact held only by eyes or infrared counts as lost 30 s after its last sighting (setting 'Visual contact lifetime', 0 = the game's own track age) and is erased after the lost-contact memory.
- **Scope range readout follows the mouse wheel:** the 'Range ... nm' text in the tool row did not change when the range was changed with the wheel; the window now tells the row.
- **Zoom animation on the radar scope:** when the range changes the contacts glide to the new scale (about a quarter of a second) instead of jumping, so the overview is not lost at a step. Setting 'Radar scope: zoom animation' (on by default; Options tab).
- **Smoother radar sweep trail:** the glow behind the sweep line is made of 1 degree slices that fade out smoothly (it was one flat wedge, or six coarse steps with visible edges).
- **Sensor board:** double-click a contact's row to bring the camera to where your sensors estimate it (never the real object), a unit's header to bring it to that unit.

## 0.3.0 - 2026-11-08

- Fix: on the 3D-globe build the Board, Scope and Sonar windows failed (repeated error window, Sonar showing "no sonar"), because Noesis resolves XAML types by full name and picked the idle flat DLL's classes. The globe build's UI now has its own namespace (`SeaPowerSensorViews.Globe.Ui`); the flat build is unchanged. Run in the game on beta 261107 (`tools/perf-test/results-2026-11-08`): Options tab, windows, selected-only lines, wedges, coverage, seabed depth; zoom 13/17 about 100 fps with lines on, 106 off.

- Fix for the new 3D-globe beta (build 261107): the sonar display called TerrainUtils.GetHeightAt, which gained a parameter there; the call now works on every build (found by the compat check against the new build).
- Sensor lines: Units shown defaults to Selected only (nothing while no unit is selected); configs from before keep their value only if it was changed from the old default.
- Sensor lines are rebuilt only when a line moved by about a screen pixel (or came, went or changed), not once a second.
- Zoomed in so far that a bearing wedge is longer than the screen, only its bearing line is drawn (setting Fill wedges when zoomed in).
- Settings in the game: a Sensor Views tab in the Options menu (fallback: a group in the Audio tab).

## 0.2.2 - 2026-11-07

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
