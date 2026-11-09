# Sea Power: R.A.T.S. Sensor Views

*Reconnaissance, Acoustics, Terrain & Signals*

A mod for [Sea Power: Naval Combat in the Missile Age](https://store.steampowered.com/app/1286220/) that shows **which of your
units sees which contact with which sensor**, in three windows that look and behave like the game's own - and pop out to a second
monitor with *R.A.T.S. Pop-Out Windows*. Needs a mod loader, either one: [R.A.T.S. Keel](https://steamcommunity.com/sharedfiles/filedetails/?id=3835527459) or [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757).

**Status: 0.4.1, corrected bearing lines and passive sonar without a range** ([Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3833019654)). 0.4.1 was run in the game on the 3D-globe beta (build 261107) on 2026-11-09 at Narvik: map lines, the board, the Echoes plot; its flat build for 0.8.7 passes the offline checks. 0.4.0 was run in the game on the 3D-globe beta (build 261107) on 2026-11-09: the bottom-bar button and its flyout, the windows, the map switches, coverage, clicks on the radar scope. Run in the game on 0.8.7 (flat map) on 2026-11-03: windows, bar buttons, sensor lines, coverage, cursors,
bearing history, depth marks and the echo plot. 0.3.0 was run in the game on the 3D-globe beta (build 261107) on 2026-11-08: Options tab, all three windows, sensor lines, coverage, sonar seabed depth, zoom ([results](tools/perf-test/results-2026-11-08/README.md)). The 0.3.0 flat build for 0.8.7 passes the offline checks; 0.8.7 itself last ran 0.2.2.
0.2.2 fixes the freeze when zooming in with the sensor lines on ([results](tools/perf-test/results-2026-11-07/README.md)); 0.2.1 was the performance update (map layers follow pans and zooms without a rebuild, scope sweep and cursors on their own layers),
measured before/after in the game on 0.8.7: [tools/perf-test/results-2026-11-04](tools/perf-test/results-2026-11-04/README.md).

- **Sensor board** - your units across, the contacts down; each cell lists the sensors that hold the contact (R search radar,
  F fire control, E ESM, S passive sonar, A active sonar, V visual / infrared, M MAD) with the age of the freshest fix; a lower-case
  letter means bearing only; dimmed letters are sensors that lost the contact a moment ago. Double-click a contact's row to bring the game's camera to where your sensors put it (the real object is never used; a bearing-only contact with no plotted place goes to the unit that holds it), a unit's header to bring it to that unit. Click a cell for bearing, range and fix
  age per sensor, with a bearing history strip and drift per minute for bearing-only contacts. Below: the selected unit's sensors with on / off, damage, arcs and EMCON.
- **Radar scope** - a scope centred on the selected unit with only what its own radar holds, and nothing at all while no search radar is on: contacts seen only by eye, sonar or another sensor stay off it; the blind arcs of its search radars; the radar horizon against a target altitude you set with a slider (square-root scale, fine at low altitudes) or type in as feet (the ring is drawn only when it lies inside the scope's range, else its distance is written at the bottom); your other units as small diamonds under the same rule as any contact, so only when this radar would really detect them (switch: *Own ships*); strobes for radars warning you (RWR) and jammers while the radar is on; a ring and "LOCK" for contacts held by a fire-control radar.
  The sweep line follows the antenna and is decoration only. A measuring cursor shows bearing and range.
  Radar contacts appear when the sweep passes over them and fade until the next pass (setting to switch it off); one the radar has lost goes at the next pass. When no search radar is on, the scope is empty and says so.
- **Sonar display** - bearing over time (relative to the bow, newest at the top) for the selected unit's sonar, the baffle sector,
  the unit's own noise at its speed, a measuring cursor (bearing, seconds ago), and a depth strip with the thermal layer, own depth, the array, the seabed and the contacts the sonar holds at the depth the track estimates (with above / below the layer). The *Echoes* button swaps the waterfall for a bearing / range plot of active sonar echoes; passive sonar gives a bearing only, so it appears on the waterfall and not there.

Everything is **display only**. The windows show what your units' sensors already report to the game's plotting table (track
bearing, range and age estimates); they change nothing in a mission, and the mission's own rules decide what a sensor can detect.
They never show a contact's real position, only the estimates your sensors have. The radar horizon ring and the depth strip use
the game's own radar-horizon function, thermal-layer data and terrain depth for your own unit; the horizon is computed for a
target altitude you choose, not for a hidden contact.

- **Sensor lines on the map** - a *Sensor lines (who sees what)* entry in the map's Layers menu: a line from each of your units to the place each of its
  sensors puts a contact (amber search radar, thick red fire-control lock, blue active sonar, dotted visual); ESM and passive sonar as a bearing line with a
  wedge. The selected unit's lines are bright, the others dimmed - or only the selected unit's, by a setting. Only track estimates are used.
- **Sensor coverage on the map** - a *Sensor coverage (selected unit)* entry (off by default): where the selected unit's sensors would hold an imagined
  target - radar against the target altitude of the scope's slider with terrain shadows, ESM, visual range, passive sonar with the dent of its baffle sector.

## Opening the windows

- One icon button (a radar scope) at the right end of the bottom bar, next to the game's window toggles; its flyout opens **Board**, **Radar scope** and **Sonar** one by one and holds the map switches (sensor lines, coverage, selected unit or all units, lines per sensor);
  they light up while their window is open.
- Hot keys (configurable): `Ctrl+Insert` board, `Ctrl+PageUp` scope, `Ctrl+PageDown` sonar, `Ctrl+Shift+PageUp` sensor lines, `Ctrl+Shift+PageDown` coverage.

The windows use the game's own window layout (title bar, close button, resizing); with Pop-Out Windows they get its pop-out button.
Config: `BepInEx/config/dev.sewerlabs.seapowersensorviews.cfg`.

## Build

`dotnet build src/SeaPowerSensorViews -c Release -p:Deploy=false -p:ManagedDir=<Sea Power_Data\Managed>`. Two DLLs
from one source tree - `SeaPowerSensorViews.dll` for the 3D-globe build and `SeaPowerSensorViewsFlat.dll` (`-p:GameBuild=Flat`) for 0.8.7 - each stays off on the
other game; checked offline against the assemblies of both; run in the game on both: 0.8.7 up to 0.2.2, the 3D-globe beta (261107) with 0.3.0.
How the windows are hosted: [docs/windows.md](docs/windows.md); the map layers: [docs/map-layers.md](docs/map-layers.md). Test list: [docs/testing.md](docs/testing.md). Frame-time test: [tools/perf-test](tools/perf-test/README.md). Notes and plan:
[HANDOVER.md](HANDOVER.md).

MIT licence. *R.A.T.S. - Ratte's Assorted Tactical Systems*
