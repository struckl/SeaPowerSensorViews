# Frame-time test of Sensor Views in the running game (see README.md here). Needs Lukas' OK, the workspace .game-lock and the
# Shot Director with the perf commands installed. Starts the game, loads the Gulf of Sidra mission, lets it run 45 s at x4, then
# runs cases.cmd twice (rounds LABEL-r1 and LABEL-r2) and quits. Results: BepInEx\perf\results.csv.
#
#   run.ps1 -Label before
param([Parameter(Mandatory)][string]$Label, [switch]$KeepOpen)
$ErrorActionPreference = 'Stop'
$game = 'E:\Games\steamapps\common\Sea Power'
$bep = Join-Path $game 'BepInEx'
$cmdFile = Join-Path $bep 'shotdirector.cmd'
$logFile = Join-Path $bep 'shotdirector.log'
$driver = Join-Path $PSScriptRoot '..\..\..\tools\game-driver\game.ps1'

function Send([string[]]$lines) { Set-Content -Path $cmdFile -Value $lines -Encoding ascii }
function WaitLog([string]$text, [int]$max, [int]$from) {
    $t0 = Get-Date
    while (((Get-Date) - $t0).TotalSeconds -lt $max) {
        if (Test-Path $logFile) {
            $new = Get-Content $logFile | Select-Object -Skip $from
            if ($new | Where-Object { $_ -like "*$text*" }) { return }
        }
        Start-Sleep 2
    }
    throw "timed out waiting for '$text'"
}
function Lines { if (Test-Path $logFile) { (Get-Content $logFile).Count } else { 0 } }

& $driver start
Start-Sleep 10
$n = Lines
Send @('load missions/NATO/Action in the Gulf of Sidra 1986.ini', 'waitload', 'wait 8', 'play', 'wait 3', 'press Start mission', 'wait 2',
       'mapfull', 'select Ticonderoga', 'focus Ticonderoga 8', 'unpause', 'tc 4', 'wait 45', 'pause', 'tc 1', 'fps uncap', 'sv verbose on',
       'wait 3', 'marker setup-done')
WaitLog 'unknown command: marker setup-done' 400 $n

$cases = Get-Content (Join-Path $PSScriptRoot 'cases.cmd')
$n = Lines
Send (($cases -replace 'ROUND', "$Label-r1") + ($cases -replace 'ROUND', "$Label-r2") + @('sv verbose off', 'marker cases-done'))
WaitLog 'unknown command: marker cases-done' 900 $n

if ($KeepOpen) { "done: $Label, game left open"; return }
$n = Lines
Send @('fps restore', 'quit')
Start-Sleep 5
$t0 = Get-Date
while ((Get-Process 'Sea Power' -ErrorAction SilentlyContinue) -and ((Get-Date) - $t0).TotalSeconds -lt 90) { Start-Sleep 2 }
"done: $Label, game running: $([bool](Get-Process 'Sea Power' -ErrorAction SilentlyContinue))"
