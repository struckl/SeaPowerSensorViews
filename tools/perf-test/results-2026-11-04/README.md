# Frame-time test, 2026-11-04 (0.8.7, flat map)

Lukas' PC: main monitor 3840x2160 with the map full screen, the 3D World View on a second monitor, Pop-Out showing the
board and scope on a third. Mission *Action in the Gulf of Sidra 1986*, paused after 45 s at x4. Frame cap and vsync off
for the test. Before = 0.2.0 as on the Workshop; after = this change. Each case was recorded for 15 s, in two rounds per
build. The rounds line up in time after the mission load, so r1 compares with r1. Raw data is in `results.csv`.

| Case | fps before r1/r2 | fps after r1/r2 | avg change | p95 ms before | p95 ms after | 1% low before | 1% low after |
|---|---|---|---|---|---|---|---|
| idle | 91.9 / 81.8 | 92.6 / 81.9 | +0.5 % | 14.4 | 13.6 | 49.5 | 50.0 |
| idle_pan | 79.3 / 77.0 | 85.6 / 79.4 | +5.6 % | 17.6 | 16.5 | 31.6 | 33.8 |
| lines | 92.3 / 81.8 | 92.4 / 81.9 | +0.1 % | 14.3 | 13.6 | 48.6 | 49.0 |
| lines_pan | 81.8 / 78.7 | 86.0 / 79.9 | +3.4 % | 17.1 | 16.1 | 33.8 | 35.7 |
| scope | 81.5 / 81.2 | 81.7 / 81.4 | +0.2 % | 14.9 | 14.3 | 40.1 | 42.0 |
| sonar_hover | 83.6 / 78.2 | 83.2 / 81.2 | +1.6 % | 15.4 | 14.7 | 30.6 | 43.1 |
| board | 86.4 / 82.7 | 87.8 / 81.1 | -0.1 % | 15.0 | 14.4 | 42.2 | 42.4 |
| everything | 73.9 / 71.9 | 80.9 / 76.0 | +7.6 % | 19.1 | 17.3 | 32.2 | 33.7 |

The mod's own timing (*Verbose log*, ms per frame, average over 10 s):

| | before | after |
|---|---|---|
| map paths while the map moves (lines + coverage) | 0.07-0.08, rebuilt about 20 times a second | 0.00, rebuilt once a second (data) |
| window drawing with all windows open | 0.08-0.10 | 0.02-0.03 |
| sensor snapshot | 0.01 | 0.01 |

## How to read it

- **The noise is about ±5 %.** *idle_pan* has every layer and window off, so the mod does nothing in that case, yet it
  measured 5.6 % faster in the after run. The gains in *lines_pan* (+3.4 %) and *sonar_hover* (+1.6 %) are inside that
  noise. Only *everything* (+7.6 %, p95 19.1 → 17.3 ms) is clearly larger.
- **On this PC and in this mission, Sensor Views was never a heavy cost.** Its own code took at most about 0.2 ms per frame
  before and about 0.03 ms after. With everything open it costs about 3-5 fps compared with *idle_pan* (before: -5 fps,
  after: -4 fps). Most of the drop from *idle* to *everything* comes from the moving map, which is the game's own cost:
  *idle_pan* is 12-13 fps below *idle* with the mod doing nothing.
- **Where the frames go: the 3D World View.** With only the World View's *Main Camera* switched off (same paused scene),
  the game ran at 208 fps instead of 90, and GPU time fell from 10 ms to 2.3 ms per frame (`cam-on-a`, `cam-off`,
  `cam-on-b` in `results.csv`).
- Not measured: a run with the mod removed (only "loaded, idle"), the 3D-globe build, a running (unpaused) mission, and
  missions larger than this one.
