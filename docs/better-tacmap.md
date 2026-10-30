# Better TacMap (Steam Workshop 3768036424) – notes

Looked at 2026-10-28: the Workshop page and changelog page (anonymous fetch) and the installed/decompiled copy
(`E:\Games\steamapps\workshop\content\1286220\3768036424\`, decompile in `E:\Project\Mods\Sea Power Lab\_decomp\ws_BetterTacMap`, **for understanding only**).

- **Author:** Bananaduck (Steam profile 76561198076223373). Requires Anchor Chain. Mod type Scripting. ~175 KB.
- **Stats (when read):** 5,649 subscribers, 341 favourites, 116 ratings, 40 comments. Posted 19 Jul, last updated 18 Oct (no year shown; changelog shows only dates). Installed copy built 18 Oct, `ApproximateVersion=0.8.5`.
- **Description (condensed):** replaces the older "show depth" and "show TTG on minimap" mods. Features: threat circles by right-clicking identified contacts; firing arcs by right-clicking contacts;
  information labels (TTG, depth, speed, altitude etc.) via a menu top right, distress beacons (yellow circle); waypoint TTG instead of ETA/distance; intercept-course plotting (select own unit, right-click a friendly or Ctrl+right-click a hostile).
- **From the code (feature level):** its own map layers `BETTERMINIMAP_DEPTH / TTG / SPEED / ALTITUDE / DISTRESS_BEACONS / PLAN_VIEW` registered in the game's layer system; Harmony patches on context menus, contact info labels, waypoint countdown/ETA mode; modules for depth + thermal-layer relation (Above/Below enum),
  firing-arc calculator, weapon range reader, intercept solver, scaled plan-view silhouettes (`PlanViewGizmo`).
- **Licence:** none stated → treat as all rights reserved. Do not copy or fork code. Features are ideas anyone may implement independently.
- **Status:** the Steam page shows the banners "removed from the community because it violates Steam Community & Content Guidelines. It is only visible to you." and "incompatible with Sea Power"; an anonymous fetch cannot tell whether the item is publicly live. Per the workspace notes the game's map-layer API changed in the 3D-globe build
  (`GizmoLayerManager` is static, layers belong to `MapDisplayState`), so its layer registration patches cannot work there unchanged; Anchor Chain also does not start on the globe build yet.
- **Comments reported by users (unverified):** threat circle wrong for a Sturgeon (anti-sub circle 40 nm), subs surface when given an intercept course against a surface ship, a Dark/Light/Auto map button does not hide in screenshot mode, possible conflict with picture-in-picture world view, some errors.
- **Overlap with Sensor Views:** almost none – it covers weapons and kinematics, Sensor Views covers sensors and detection. Only weapon-range circles and the above/below-layer note overlap. Coexistence: it adds its own layers (earlier recon: 500–509), Topo uses 600.
