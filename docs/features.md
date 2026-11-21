# R.A.T.S. Sensor Views: feature detail

Everything the mod does, in full. The short list is in the [README](../README.md).

## Sensor board

- Your units across, the contacts down
- Each cell lists the sensors that hold the contact, with the age of the freshest fix:
  - R search radar, F fire control, E ESM, S passive sonar, A active sonar, V visual / infrared, M MAD
  - a lower-case letter means bearing only; a dimmed letter is a sensor that lost the contact a moment ago
- Click a cell for bearing, range and fix age per sensor
  - bearing-only contacts also get a bearing history strip and the drift per minute
- Double-click a contact's row to move the game's camera to where your sensors put it
  - a bearing-only contact with no plotted place goes to the unit that holds it
  - a contact only your other units range goes to where the freshest ranging unit puts it
- Double-click a unit's header to move the camera to that unit
- Below the grid: the selected unit's sensors with on / off, damage, arcs and EMCON

## Radar scope

- Centred on the selected unit, showing only what its own radar holds
  - contacts seen only by eye, sonar or another sensor stay off it
  - with no search radar on (switched off, EMCON, broken) the scope is empty and says so
- A search radar that does not look all round (a fighter's nose radar, say) has its covered arc shaded green
- Radar contacts are painted when the sweep passes over them and fade until the next pass; a contact the radar has lost goes at the next pass
  - the setting *Radar scope: blips appear when the sweep passes* turns this off
- Radar horizon ring for a target altitude you set with a slider or type in as feet, up to 80,000 ft
  - the ring is drawn only when it lies inside the scope's range; otherwise its distance is written at the bottom
- Your other units appear as small diamonds, only where this radar would detect them (switch *Own ships*)
- Strobes for radars warning you (RWR) and for jammers while the radar is on
- A ring and "LOCK" mark a contact held by a fire-control radar
- A measuring cursor shows bearing and range; *Heading up* turns the scope
- The range changes with the mouse wheel and the - and + buttons

## Sonar display

- Bearing over time (relative to the bow, newest at the top) for the selected unit's sonar, with the baffle sector
- Your own noise at its speed, and a measuring cursor (bearing, seconds ago)
- A depth strip with the thermal layer, own depth, the array, the seabed and the contacts the sonar holds
  - contacts are placed at the depth the track estimates, with above / below the layer
- *Echoes* swaps the waterfall for a bearing / range plot of active sonar echoes
  - passive sonar gives a bearing only, so it appears on the waterfall and not on that plot
- Your own torpedoes in the water show when this sonar would hear them, so a wire-guided torpedo can be followed by ear
  - switch: *Sonar display: own torpedoes*
- Only what the sonar hears shows
  - nothing new appears while it is off, broken or a towed array is retracted; older rows scroll away
  - your own ships appear under the same rule as any contact
- The < and > buttons switch between the unit's sonars

## Sensor lines on the map

- Layers menu entry *Sensor lines (who sees what)*
- A line from each of your units to the place each of its sensors puts a contact:
  - amber for search radar, thick red for a fire-control lock, blue for active sonar, dotted for visual
  - ESM and passive sonar as a bearing line with a wedge
- By default only the selected unit's lines are drawn (*Units shown*); the alternative is all units, the selected one bright and the others dimmed
- Lines per sensor can be switched on and off, and ESM lines of contacts that a radar already tracks can be hidden
- Only track estimates are used

## Sensor coverage on the map

- Layers menu entry *Sensor coverage (selected unit)*, off by default
- Shows where the selected unit's sensors would hold an imagined target
  - radar against the target altitude of the scope's slider, with terrain shadows
  - ESM, visual range and passive sonar (with the dent of its baffle sector)
- The rings follow the unit; their shape is worked out again about every nautical mile

## Opening the windows

- One icon button (a radar scope) at the right end of the bottom bar opens a flyout
  - the flyout has toggles for the three windows and the map switches (sensor lines, coverage, selected unit or all units, lines per sensor)
  - the window toggles light up while their window is open
- Hot keys:

| Window or layer | Default key |
|---|---|
| Sensor board | `Ctrl+Insert` |
| Radar scope | `Ctrl+PageUp` |
| Sonar display | `Ctrl+PageDown` |
| Sensor lines layer | `Ctrl+Shift+PageUp` |
| Coverage layer | `Ctrl+Shift+PageDown` |

- The windows are titled R.A.T.S. Sensor Board, R.A.T.S. Radar Scope and R.A.T.S. Sonar Display, with the game's own title bar, close button and resizing

## Settings in detail

- Most are in the game's Options screen, on the *Sensor Views* tab
- All of them are in `BepInEx/config/dev.sewerlabs.seapowersensorviews.cfg`
  - sections: Opening the windows, Views, Map: sensor lines, Map: coverage
  - hot keys are written as modifiers joined with `+`, then a Unity key name; an empty value means no key
- Defaults worth knowing:
  - sensor lines on, for the selected unit only; coverage off
  - lost contacts stay on the board and scope for 60 s, dimmed
  - a contact seen only by eye or infrared counts as lost 30 s after its last sighting
  - target altitude 100 ft, target radar cross section 10 m2

## Compatibility in detail

- Sea Power 0.8.7 (flat map) up to every 0.9.x; `_info.ini` declares `GreaterThanEqualToVersion=0.8.7` and `LessThanVersion=0.10.0`
- Two DLLs from one source tree: `SeaPowerSensorViews.dll` for the 3D-globe build and `SeaPowerSensorViewsFlat.dll` for 0.8.7
- Run in the game:
  - 0.8.7 (flat map): windows, bar buttons, sensor lines, coverage, cursors and the echo plot, last run with 0.2.2; later versions are checked offline there
  - 3D-globe beta, build 261107: versions 0.3.0 to 0.4.3 (Options tab, the three windows, lines, coverage, own torpedoes, radar scope fixes)
  - game 0.9.1, build 261112: version 0.4.5, the in-game name and the coverage ring following a moving unit
  - 0.4.4 was checked offline against 0.9.0
- Loader: R.A.T.S. Keel; the entry point is Anchor Chain's, which Keel carries on
