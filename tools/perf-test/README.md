# Frame-time test

A repeatable before/after measurement of what Sensor Views costs in the running game. It needs Lukas' OK for the game
(see `docs/game-testing.md` in the workspace), the workspace `.game-lock`, and the Shot Director dev plugin with the
perf commands (`perf`, `fps`, `pan`, `wiggle`, `sv`, `focus`, `cams`, `cam`) from the workspace's `tools/perf-director`
(`dotnet build -c Release` installs it into the game; remove `StreamingAssets\ShotDirector` afterwards).

```powershell
.\run.ps1 -Label before     # installed build: launch, mission, two rounds of cases.cmd, quit
# build + deploy the new DLL (game closed)
.\run.ps1 -Label after
```

The mission is *Action in the Gulf of Sidra 1986* (42 own units incl. weapons, 26 hostile), run 45 s at x4 so the
sensors hold contacts, then paused (so each case sees the same picture), map full screen on the Ticonderoga at zoom 8, frame cap and
vsync off for the test only (`fps uncap`; `fps restore` before quitting, nothing is saved).

| Case | What runs |
| --- | --- |
| idle | layers off, windows closed (the reference) |
| idle_pan | the map swings east-west every frame (pan 1.5° / 12 s), layers off |
| lines | sensor lines on (all units, selected bright), map still |
| lines_pan | sensor lines on while the map moves |
| scope | radar scope open (popped out by Pop-Out on his setup) |
| sonar_hover | sonar display open, mouse circling over the waterfall |
| board | sensor board open |
| everything | lines + coverage + all three windows + panning + mouse over the sonar |

Each case settles 3 s and records 15 s. `BepInEx\perf\results.csv` gets one line per case: frames, fps, average /
p50 / p95 / p99 / max frame time, 1% low, frames over twice the average, GC runs, and from Unity's frame timing the
CPU main-thread time, render-thread time, GPU time and managed allocations per frame. `<case>.frames.csv` holds every
frame time. With *Verbose log* on (the runner switches it on and off), `LogOutput.log` also gets the mod's own timing
per part every 10 s.

On his PC at 4K the game is GPU-bound (GPU ~96 % with the map full screen), so fps hides most CPU cost: compare the
CPU main-thread column and the allocations, not only fps.
