# R.A.T.S. Sensor Views: building

Build notes and the layout of the repo. Short form in the [README](../README.md).

## Building

`dotnet build src/SeaPowerSensorViews -c Release -p:Deploy=false -p:ManagedDir=<Sea Power_Data\Managed>`

- Needs the R.A.T.S. Sea Power workspace around the repo, with the R.A.T.S. Shared repo (`SeaPowerShared`) next to it
  - the shared project part (references, flat-map build, deploy) and the shared skeleton (entry, log, host, hooks, settings, Layers menu entries, Options tab) come from `SeaPowerShared` and are compiled into this DLL
- `-p:RatsWorkspace=<workspace>` builds from a clone elsewhere; `-p:RatsShared=<folder>\` uses another copy of the shared repo
- `-p:GameBuild=Flat` builds `SeaPowerSensorViewsFlat.dll` for 0.8.7
- `tools\package.ps1` stages the release with the shared package steps (`-Workspace` and `-RatsShared` work the same way)

## Layout

| Path | What |
|---|---|
| `src/SeaPowerSensorViews/Ui/` | the three windows, the bottom-bar flyout, the Options tab rows |
| `src/SeaPowerSensorViews/Map/` | the sensor lines and coverage layers |
| `src/SeaPowerSensorViews/Data/` | what is read from the game's plotting table |
| `src/SeaPowerSensorViews/Settings.cs` | the config entries and their change counters |
| `docs/windows.md` | how the windows are hosted |
| `docs/map-layers.md` | the map layers |
| `docs/testing.md` | the in-game test list |
| `tools/perf-test/` | frame-time test ([README](tools/perf-test/README.md)) |
| `workshop/` | Workshop description, pictures, change note |
| [HANDOVER.md](HANDOVER.md) | notes and plan |
