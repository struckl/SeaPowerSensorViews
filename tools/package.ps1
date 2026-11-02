<#
.SYNOPSIS
    Builds a release of Sea Power: R.A.T.S. Sensor Views and stages the folder that goes to the Steam Workshop.

.DESCRIPTION
    Release builds without the automatic deploy, then dist\SeaPowerSensorViews\ with exactly what players get:
    SeaPowerSensorViews.dll (the 3D-globe game) and SeaPowerSensorViewsFlat.dll (Sea Power 0.8.7's flat map), the same sources
    built against each game's assemblies, each staying off on the other game; _info.ini, LICENSE.txt, preview.jpg (the
    Workshop preview, if present).
    The flat build needs a copy of 0.8.7's Sea Power_Data\Managed (-FlatManagedDir; default: _gamerefs next to the mod
    folders in the workspace), the globe build the globe game's (-GlobeManagedDir when the installed game is 0.8.7).
    When the workspace's tools\compat-check is there, every DLL is checked against both games' assemblies: it must
    have nothing missing on its own game, and its entry point nothing missing on either. -NoFlat leaves the flat build out.
    With -Install the staged folder also replaces <GameDir>\Sea Power_Data\StreamingAssets\SeaPowerSensorViews\, the
    folder to pick in the game's Create Mod screen. Sea Power must be closed for that.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install
#>
param(
    [string]$GameDir = $(if ($env:GameDir) { $env:GameDir } else { 'E:\Games\steamapps\common\Sea Power' }),
    [string]$FlatManagedDir = $(if ($env:FlatManagedDir) { $env:FlatManagedDir } else { [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\_gamerefs\SeaPower-0.8.7-public\Managed')) }),
    # The globe build's game assemblies when the installed game isn't the globe version (a saved copy of its Managed).
    [string]$GlobeManagedDir = $env:GlobeManagedDir,
    [switch]$NoFlat,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\SeaPowerSensorViews\SeaPowerSensorViews.csproj'
$dist = Join-Path $root 'dist\SeaPowerSensorViews'
$globeManaged = if ($GlobeManagedDir) { $GlobeManagedDir } else { Join-Path $GameDir 'Sea Power_Data\Managed' }

# The two builds differ only in the assemblies they compile against and one define: --no-incremental, or the second
# build may reuse the first one's output.
$out = Join-Path $root 'src\SeaPowerSensorViews\bin\Release\netstandard2.1'
dotnet build $project -c Release --no-incremental -p:Deploy=false -p:GameDir="$GameDir" -p:ManagedDir="$globeManaged"
if ($LASTEXITCODE -ne 0) { throw 'Globe build failed (with the game on 0.8.7, pass -GlobeManagedDir with the globe version''s assemblies).' }
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $out 'SeaPowerSensorViews.dll') $dist
if (-not $NoFlat) {
    if (-not (Test-Path (Join-Path $FlatManagedDir 'Seapower-Scripts.dll'))) { throw "No 0.8.7 assemblies in $FlatManagedDir (a copy of its Sea Power_Data\Managed, or pass -NoFlat)." }
    dotnet build $project -c Release --no-incremental -p:Deploy=false -p:GameDir="$GameDir" -p:GameBuild=Flat -p:FlatManagedDir="$FlatManagedDir"
    if ($LASTEXITCODE -ne 0) { throw 'Flat-map build failed.' }
    Copy-Item (Join-Path $out 'SeaPowerSensorViewsFlat.dll') $dist
}

# Each DLL against both games' assemblies (the workspace's compat-check): nothing missing on its own game, and its
# entry point (which runs on both) nothing missing on either.
$tool = Join-Path $root '..\tools\compat-check'
if (Test-Path $tool) {
    dotnet build $tool -c Release -v q | Out-Null
    $gates = @('--gate', 'SeaPowerSensorViews.Plugin::TriggerEntryPoint', '--gate', 'SeaPowerSensorViews.Plugin::.ctor')
    foreach ($b in @(@('SeaPowerSensorViews.dll', $globeManaged, $FlatManagedDir), @('SeaPowerSensorViewsFlat.dll', $FlatManagedDir, $globeManaged))) {
        $dll = Join-Path $dist $b[0]
        if (-not (Test-Path $dll)) { continue }
        dotnet run --project $tool -c Release --no-build -- $dll --against $b[1] --quiet
        if ($LASTEXITCODE -ne 0) { throw "$($b[0]) references things its own game does not have." }
        # --classload: Unity's ECS start-up loads EVERY type of every loaded assembly, so a field of a type the other game lacks
        # (not just the entry point) stops all missions from loading there, although the DLL stays off (found 2026-10-28).
        dotnet run --project $tool -c Release --no-build -- $dll --against $b[2] --quiet --classload @gates
        if ($LASTEXITCODE -ne 0) { throw "$($b[0]): the entry point or a type's fields would fail on the other game." }
    }
}
else { Write-Warning 'tools\compat-check not found (the workspace next to this repo) - the DLLs were not checked against both games.' }
Copy-Item (Join-Path $root 'src\SeaPowerSensorViews\mod\_info.ini') $dist
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $dist 'LICENSE.txt')
$preview = Join-Path $root 'workshop\preview.jpg'
if (Test-Path $preview) { Copy-Item $preview $dist } else { Write-Warning 'workshop\preview.jpg missing - nothing to pick as the Workshop preview.' }
$version = (Select-String -Path (Join-Path $root 'src\SeaPowerSensorViews\Plugin.cs') -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
Write-Host "Staged Sea Power: R.A.T.S. Sensor Views $version in $dist"
Get-ChildItem -Recurse $dist | Where-Object { -not $_.PSIsContainer } | ForEach-Object { Write-Host ("  {0,10:N0}  {1}" -f $_.Length, $_.FullName.Substring($dist.Length + 1)) }

if ($Install) {
    if (Get-Process -Name 'Sea Power' -ErrorAction SilentlyContinue) { throw 'Sea Power is running - close it first.' }
    $target = Join-Path $GameDir 'Sea Power_Data\StreamingAssets\SeaPowerSensorViews'
    if (Test-Path $target) { Remove-Item -Recurse -Force $target }
    Copy-Item -Recurse $dist $target
    Write-Host "Installed to $target - pick this folder in Mods > Create Mod."
}
