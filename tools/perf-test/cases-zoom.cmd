# Zoom reference (run.ps1 -Cases cases-zoom.cmd): the same zoom levels with the sensor lines off and on, to tell the game's own
# cost of a zoomed-in map from the mod's.
sv win 0 off
sv win 1 off
sv win 2 off
sv cover off
sv lines off
focus Ticonderoga 13
wait 3
perf ROUND-z1-off_zoom13 15
sv lines on
wait 3
perf ROUND-z2-lines_zoom13 15
sv lines off
focus Ticonderoga 17
wait 3
perf ROUND-z3-off_zoom17 15
sv lines on
wait 3
perf ROUND-z4-lines_zoom17 15
focus Ticonderoga 8
