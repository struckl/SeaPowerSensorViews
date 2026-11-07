# Running-mission cases (optional third block, run.ps1 -Running): the game runs at x1, so the sensor lines change every second.
sv win 0 off
sv win 1 off
sv win 2 off
sv cover off
sv lines on
focus Ticonderoga 8
unpause
tc 1
wait 3
perf ROUND-11-running_lines 15
focus Ticonderoga 13
wait 3
perf ROUND-12-running_zoom13 15
pause
focus Ticonderoga 8
