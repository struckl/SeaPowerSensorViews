# R.A.T.S. Sensor Views: building

Build notes and the layout of the repo. Short form in the [README](../README.md).

## Building

`dotnet build src/SeaPowerSensorViews -c Release -p:Deploy=false -p:ManagedDir=<Sea Power_Data\Managed>`

- Needs the R.A.T.S. Sea Power workspace around the repo, with the Rigging repo (`SeaPowerRigging`, R.A.T.S. Rigging) next to it
  - Rigging's `build\Rats.Mod.props` is the shared project part (references, deploy, the reference to Rigging's DLL)
  - Rigging's library holds the start, log, host, hooks, settings, Layers menu entries and the Options tab; it is built on demand as a project reference
- One build, for the game's current main version: `SeaPowerSensorViews.dll`; the plugin names Rigging with `[ACDependency]`
- `-p:RatsWorkspace=<workspace>` builds from a clone elsewhere; `-p:RatsRigging=<folder>\` uses another copy of the Rigging repo
- `tools\package.ps1` stages the release with Rigging's package steps (`-Workspace` and `-RatsRigging` work the same way) and adds the built-in copy of `SeaPowerRigging.dll`
- `src/SeaPowerSensorViews/Rig.cs` holds the mod's one Rigging object (`Rig.Log`, `Rig.Settings`, `Rig.Hooks`, `Rig.Mission`, `Rig.Options`)

## Layout

| Path | What |
|---|---|
| `src/SeaPowerSensorViews/Ui/` | the three windows, the bottom-bar flyout, the Options tab rows |
| `src/SeaPowerSensorViews/Map/` | the sensor lines and coverage layers |
| `src/SeaPowerSensorViews/Data/` | what is read from the game's plotting table |
| `src/SeaPowerSensorViews/Plugin.cs`, `Rig.cs` | the entry point and the mod's R.A.T.S. Rigging object |
| `src/SeaPowerSensorViews/Settings.cs` | the config entries and their change counters |
| `docs/windows.md` | how the windows are hosted |
| `docs/map-layers.md` | the map layers |
| `docs/testing.md` | the in-game test list |
| `tools/perf-test/` | frame-time test ([README](tools/perf-test/README.md)) |
| `workshop/` | Workshop description, pictures, change note |
| [HANDOVER.md](HANDOVER.md) | notes and plan |
