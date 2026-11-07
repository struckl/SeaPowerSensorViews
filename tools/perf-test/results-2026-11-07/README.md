# Zoom test, 2026-11-07 (0.8.7, flat map)

Players reported that the game crawls or freezes when the map is zoomed in with the sensor lines on, worse the closer the zoom.
Same PC, mission and method as `results-2026-11-04` (Gulf of Sidra, paused after 45 s at x4, frame cap and vsync off), with
the zoomed-in cases added (`focus Ticonderoga 13` / `17`). Runs: `zb` = 0.2.1 as on the Workshop, `za` = paths cut to the view,
`zc` = cut to a smaller area plus an order-independent change check, `zr` = zoom reference with the `zc` build (lines off vs on).
Raw data: `results.csv`.

| Case | 0.2.1 (zb) r1 / r2 | first fix (za) | final (zc) | max frame 0.2.1 | max frame final |
|---|---|---|---|---|---|
| lines, zoom 8 | 91.7 / 85.9 | 94.2 / 82.8 | 93.9 / 82.7 | 30 ms | 36 ms |
| lines, zoom 13 | 61.5 / 30.8 | 70.7 / 64.5 | 74.0 / 72.3 | 855 ms | 51 ms |
| lines, zoom 17 | froze ~3 min, Windows closed the game | 71.7 / 66.7 | 75.8 / 74.1 | - | 41 ms |
| running x1, lines, zoom 13 | - | 61.7 | 73.9 | - | 67 ms |

Zoom reference (`zr`, final build, same session):

| | lines off | lines on |
|---|---|---|
| zoom 13 | 95.1 / 93.6 fps | 85.6 / 84.2 fps, max 35 ms |
| zoom 17 | 94.8 / 94.6 fps | 86.5 / 85.5 fps, max 29 ms |

## What it shows

- **The freeze was the mod.** With the lines off the game runs as fast zoomed in as at zoom 8. In 0.2.1 the lines were drawn
  in full length: zoomed in, one line ran hundreds of thousands of pixels past the screen and every dash of the dotted and
  dashed ones was drawn. The mod's own computing stayed tiny (under 0.01 ms per frame in its log); the time went into the game's
  UI drawing. At zoom 17 the game stopped answering and Windows closed it.
- **Fix 1, cut to the view:** lines, wedges and coverage are cut to the visible map plus a margin before they are drawn. No
  freeze; single frames still reached 100-140 ms, about once a second.
- **Fix 2:** the verbose log showed the lines rebuilt once a second in a paused game - the change check depended on the order
  of the contacts. It no longer does, and the margin is half a view instead of a whole one. Frames stay under about 50 ms.
- What is left zoomed in is about 10 fps (95 → 85) with the lines on: the map is covered by long lines and filled bearing wedges.
- The track estimates still move by a few dozen metres per second in a paused game (log), so the lines are rebuilt about once a
  second; at zoom 8 that costs nothing measurable.
- Not measured: the 3D-globe build, missions with many more units and sensors.
