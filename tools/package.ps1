<#
.SYNOPSIS
    Builds a release of Sea Power: R.A.T.S. Sensor Views and stages the folder that goes to the Steam Workshop.

.DESCRIPTION
    Release builds without the automatic deploy, then dist\SeaPowerSensorViews\ with exactly what players get:
    SeaPowerSensorViews.dll (the 3D-globe game) and SeaPowerSensorViewsFlat.dll (Sea Power 0.8.7's flat map), the same sources
    built against each game's assemblies, each staying off on the other game; _info.ini, LICENSE.txt, CHANGELOG.md, preview.jpg (the
    Workshop preview, if present).
    The flat build needs a copy of 0.8.7's Sea Power_Data\Managed (-FlatManagedDir; default: _gamerefs in the workspace), the
    globe build the globe game's (-GlobeManagedDir when the installed game is 0.8.7).
    Every DLL is checked against both games' assemblies with the workspace's tools\compat-check: it must have nothing missing on
    its own game, and on the other game its entry point nothing missing and every type loadable. -NoFlat leaves the flat build out.
    With -Install the staged folder also replaces <GameDir>\Sea Power_Data\StreamingAssets\SeaPowerSensorViews\, the folder to
    pick in the game's Create Mod screen. Sea Power must be closed for that. -Zip also writes dist\SeaPowerSensorViews-<version>.zip.
    The steps are the shared ones of every R.A.T.S. mod (SeaPowerKeel\shared\tools\RatsPackage.psm1); the workspace is found
    above this repo, -Workspace names it for a clone elsewhere, -RatsShared another copy of the shared folder.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\package.ps1 -Install
#>
param(
    [string]$GameDir = $(if ($env:GameDir) { $env:GameDir } else { 'E:\Games\steamapps\common\Sea Power' }),
    [string]$Workspace = $env:RatsWorkspace,
    [string]$RatsShared = $env:RatsShared,
    [string]$FlatManagedDir = $env:FlatManagedDir,
    # The globe build's game assemblies when the installed game isn't the globe version (a saved copy of its Managed).
    [string]$GlobeManagedDir = $env:GlobeManagedDir,
    [switch]$NoFlat,
    [switch]$Install,
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Workspace) {
    $Workspace = $root
    while ($Workspace -and -not (Test-Path (Join-Path $Workspace 'mods.json'))) { $Workspace = Split-Path -Parent $Workspace }
    if (-not $Workspace) { throw 'No workspace (a folder with mods.json) above this repo - pass -Workspace.' }
}
if (-not $RatsShared) { $RatsShared = Join-Path $Workspace 'SeaPowerKeel\shared' }
Import-Module (Join-Path $RatsShared 'tools\RatsPackage.psm1') -Force

$name = 'SeaPowerSensorViews'
$project = Join-Path $root "src\$name\$name.csproj"
$dist = Join-Path $root "dist\$name"
if (-not $FlatManagedDir) { $FlatManagedDir = Join-Path $Workspace '_gamerefs\SeaPower-0.8.7-public\Managed' }
if (-not $GlobeManagedDir) { $GlobeManagedDir = Join-Path $GameDir 'Sea Power_Data\Managed' }

$env:RatsShared = $RatsShared.TrimEnd('\') + '\'      # the csproj's -p:RatsShared, also for the flat build
Build-RatsMod -Project $project -Dist $dist -GameDir $GameDir -GlobeManagedDir $GlobeManagedDir -FlatManagedDir $FlatManagedDir -Workspace $Workspace -NoFlat:$NoFlat | Out-Null
Test-RatsCompat -Workspace $Workspace -Dist $dist -RootNamespace $name -GlobeManagedDir $GlobeManagedDir -FlatManagedDir $FlatManagedDir
Copy-RatsCommon -Root $root -Dist $dist -InfoIni (Join-Path $root "src\$name\mod\_info.ini")
$version = Get-RatsModVersion -PluginFile (Join-Path $root "src\$name\Plugin.cs")
Write-Host "Staged Sea Power: R.A.T.S. Sensor Views $version in $dist"
Show-RatsStage -Dist $dist
if ($Zip) { New-RatsZip -Dist $dist -Version $version }
if ($Install) { Install-RatsMod -Dist $dist -GameDir $GameDir }
