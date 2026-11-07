# Sensor Views frame-time cases (Shot Director with the perf commands, see README.md here).
# Precondition: Gulf of Sidra loaded, map full screen on Ticonderoga at zoom 8, paused, "fps uncap".
# Each case: set the state, settle 3 s, record 15 s. ROUND is replaced by the runner (r1, r2).
sv win 0 off
sv win 1 off
sv win 2 off
sv cover off
sv lines off
sv who all
wait 3
perf ROUND-01-idle 15
pan 1.5 12
wait 3
perf ROUND-02-idle_pan 15
pan stop
sv lines on
wait 3
perf ROUND-03-lines 15
pan 1.5 12
wait 3
perf ROUND-04-lines_pan 15
pan stop
sv lines off
sv win 1 on
wait 3
perf ROUND-05-scope 15
sv win 1 off
sv win 2 on
wiggle 1060 880 300
wait 3
perf ROUND-06-sonar_hover 15
wiggle stop
sv win 2 off
sv win 0 on
wait 3
perf ROUND-07-board 15
sv lines on
sv cover on
sv win 1 on
sv win 2 on
pan 1.5 12
wiggle 1060 880 300
wait 3
perf ROUND-08-everything 15
wiggle stop
pan stop
sv win 0 off
sv win 1 off
sv win 2 off
sv cover off
sv lines on
focus Ticonderoga 13
wait 3
perf ROUND-09-lines_zoom13 15
focus Ticonderoga 17
wait 3
perf ROUND-10-lines_zoom17 15
focus Ticonderoga 8
