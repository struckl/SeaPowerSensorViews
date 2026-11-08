# Map layers: sensor lines and coverage

Both layers are paths in screen pixels laid into the map control (`Map/MapView.cs`), the way Borders draws its lines on the globe: the
geometry is kept as longitude/latitude, projected through the map on every change of the view or the data, and drawn as a few Noesis
`Path`s in a `MapPanel` right after the graticule (below the unit icons). One method for both map kinds: `MapBase.LocationToView` on the flat
map, `TryLocationToView` on the globe (far-side points are dropped).

## Where the two games differ (why two DLLs)

| | 0.8.7 flat | 3D-globe build 26223125 |
|---|---|---|
| Layers menu | static `GizmoLayerManager.Instance`, `new MapLayer(number, name, true)` | `MapDisplayState.Tactical`, internal `MapLayer(MapDisplayState, in GizmoLayerManager.Entry, bool)` by reflection (`GameApi.NewLayer`) |
| Projection to the screen | `MapBase.LocationToView` | `MapBase.TryLocationToView` |

Everything else (windows, snapshot, coverage maths) is shared. Layer numbers: sensor lines 603, coverage 604 (Topo 600, Borders 601, Cities 602).
Plugin GUID of the flat build: `dev.sewerlabs.seapowersensorviews.flatmap`; entry gate on `Type.GetType("SeapowerUI.MapDisplayState, Seapower-Scripts")`.

## Hooks (checked by reflection in both games: `tools/hook-check`)

- `MainGameViewModel.OnStart()`, `GameMain.OnDestroy()` - mission ready / ending.
- `MapControl.RenderedLayer.OnRender(DrawingContext)` (postfix, once NoesisGUI runs) - hands over the map control that shows the live map.
- Reflection: `GizmoLayerManager+Entry`, `MapLayer(MapDisplayState, ref Entry, bool)` (globe), `SonarAcoustics.OwnNoiseDB` (any overload, filled with its defaults).

`dotnet run --project tools/hook-check -c Release -- --against "<...>\Managed"` for each game; both print "all present".

## Look

Colours are the game's: `NTDS.UnknownColour` (amber, search radar), `Map_RWR` (the red of its radar-warning vectors, fire control), `NTDS.FriendColour`
(ESM), `NTDS.NeutralColour` (passive sonar), `Map_UnitOnMapRange` (active sonar), `Brush.Foreground.Normal` (visual), looked up from the map control with
fallbacks. Weight: the game's map styling ini (`TacticalMap_Geometry`) defaults every line to 1 px; that is the base, a lock is 3x. The
globe build no longer has `UI_Parameters.TM_*Thickness`, so the base is a constant times the "Line weight" setting. Labels are 12 pt text blocks (the size of
`MapOverlay.FontSize`) in the line's colour. Opacity is in the brushes, not on the paths (an element opacity makes Noesis render offscreen).

## Sensor lines (`Map/LineBuilder.cs`)

For every hit of the snapshot (own unit, contact, sensor kind): a ranged track becomes a polyline from the unit along the track's bearing for the track's
range (a chord about every 25 nm); a bearing-only track (ESM, passive sonar) becomes a bearing line plus a filled wedge of the bearing error, length
"Bearing wedge length". The contact's real position is never read.

## Coverage (`Map/CoverageJob.cs`)

For the unit selected in the game, against an imagined target: see the class comment. The terrain check (`TerrainUtils.IsLineOfSightBetween`) uses static state
and Unity physics, so it runs 6 rays per frame on the main thread (14 samples + 4 bisections per ray); a new job starts when the unit, its place (1 nm),
the target altitude or a setting changes, at most once a second, and the old result stays up until the new one is done. The rest of the maths is a few
hundred operations; there is no worker thread because nothing is expensive enough to need one.

## What the lines and cones mean

- A **dashed line with a label (ESM T7004)** is the compass bearing from one of your units to a contact it hears on ESM or passive sonar: a direction only, no distance. The **filled cone** round it is that bearing's uncertainty (1 to 15 degrees either side).
- Line and cone run to the distance of the contact's map marker (plus 10 %) when the game plots it, else to the 'Bearing wedge length'. The marker is the game's combined estimate from all your sensors, so a single unit's line can pass beside it: that is the disagreement between the sensors, not a bug.
- **Dotted or solid lines to a contact** (radar, visual, active sonar) end on the contact's marker.
- The orange ellipses are the game's own error ellipses, not drawn by this mod.
