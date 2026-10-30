# Radar, ESM and visual/IR – what a mod can read and call

Source: a code-reading sub-agent, 2026-10-28, on the decompiled **0.8.7** sources (`E:\Project\Comms & Causes\_decomp\Seapower-Scripts\Seapower\`,
same content as `_gamerefs/decomp-0.8.7`). Paths below are relative to `Seapower\`. **Line numbers are 0.8.7; the globe build (`_gamerefs/decomp-beta`)
is shifted by roughly +30.** Spot-checked in both builds: `RadarCalculator.GetFCRDetectionRange`, `getRadarHorizon`, `TerrainUtils.IsLineOfSightBetween(GeoPosition,…)`,
`ObjectBase._rwrBearings`, `ObjectBase.PlottingTable`. Everything else: re-check before relying on it.

Verdict: all radar / ESM / visual detection runs in managed C#; only the later fusion is Burst/ECS. Most of the maths is in public static,
side-effect-free functions, so coverage for a hypothetical target can be computed.

Units: 1 Unity unit = 67.2 m; 1 nm = 27.56 units; 1 km = 14.88 units. `RadarParameters` ranges/altitudes are stored in Unity units. Despite its
name `ISensor.MaxRangeInMeters` is in Unity units.

## 1. Enumerating a unit's sensors
- `unit._obp._sensorSystems` = all (`ObjectBaseParameters.cs:179`); typed lists `:183-203`: `_visualSensors`, `_airSearchRadars`, `_surfaceSearchRadars`,
  `_searchRadarSystems`, `_targetingRadarSystems`, `_esmSystems`, `_ecmSystems` (sonars: `_sonarSystems` `:193`, `_activeSonarSystems` `:195`).
- Type: `_systemType` + subclass; IR = `SensorSystemVisual` with `SensorInfrared` (`ObjectBaseLoader.cs:1980`); radar kind `SensorSystemRadar._type`
  {Search, DirectedSearch, Targeting} (`SensorSystemRadar.cs:25`).
- State: `IsOn`, `Inoperable` (`BaseSystem.cs:187-189`), `CurrentDamageType` (`:232`), `CurrentStateEnum()` (`SensorSystem.cs:1012`), `CanProduceContacts`
  (`:228`). Unit level: `ObjectBase.Emcon`, `IsAirSearchRadarsOn`, `IsSurfaceSearchRadarsOn`, `IsOffensivelyJamming` (`ObjectBase.cs:1510, 1673-1681`);
  EMCON just turns off search radars, active sonar and offensive jamming (`:6054`).
- Mount/arcs: `_mountObject`, `SensorPosition` (`SensorSystem.cs:246`); `_horizontalViewArc` / `_verticalViewArc` in degrees, default ±181/±91 =
  unrestricted (`:188-190`). Radar arcs are measured from the **mount** forward (`Radar.cs:224`, `IsObjectInFOV` `SensorSystem.cs:1325`); visual arcs from the **hull** forward
  (`SensorSystemVisual.cs:398-428`). No other baffles.
- Rotation: `ScanPeriodSeconds` = 60/RPM, `_degreesPerSecond` (`SensorSystemRadar.cs:47, 176`); the search-radar mount is physically rotated each frame (`:324`), so
  `_mountObject.transform.eulerAngles.y` is a live antenna bearing – **cosmetic only**, detection does not depend on beam azimuth.
- Parameters `_radar._rp` (`RadarParameters.cs:15-55`): `_minRange`, `_maxRange`, `_minAltitude`, `_maxAltitude`, `_beamWidth`, `_rangeResolution`, `_peakPower`,
  `_gainFactor`, `_frequency` (band enum), `_waveLength`, `_role`, `_jamResistance`.
- Channels: `_targetChannels`, `_weaponChannels`, `EffectiveWeaponChannels`, `TotalTargetChannels`, `_associatedTargets`, `_associatedWeapons` (`SensorSystem.cs:160-162, 273-303`).
- ESM: `_esm._ep` (`_frequencies`, `_angularResolutionDegrees`, `_scanPeriod`, `_overHorizonMultiplier` default 1.3, `_isRWR`).

## 2. Detection maths
**Search radar** (`SearchRadar.cs:80-156`, `Radar.cs:63-253`) is deterministic, no probability per scan. Order of checks: role/type filters → altitude brackets
(`AltitudeOverClutter`) → min/max range → radar horizon + terrain LOS → field of view → look-down (hard cutoff at `_lookDownRange` when `LookDownMultiplier` < 1) →
`RadarCalculator.createRadarEcho` (`RadarCalculator.cs:91-148`):
- range = (Pt·G²·λ²·σ / (Pmin·(4π)³))^¼, Pmin = `Globals.RcsValues._minDetectableReturnPowerRadar` (2e-14)
- RCS by aspect `Lerp(minRCS, maxRCS, t)`, max at broadside (`SensorUtils.cs:140-145`); raised submarine masts 0.01 m² each, scaled down by sea state
- sea state: surface targets ×(1 − 0.05·SeaState) when SeaState > 1 (`:128-133`)
- low-altitude multipath/clutter (`RadarLowAltitude`) only for non-airborne radars vs air targets over water, **off by default** (`Globals._lowAltitudeClutter`, `Session.cs:68`)
- jamming: burn-through range (`ECM.cs:172/202/251`); **no rain/weather attenuation for radar**

Timing: scans are round-robined over the datalink cycle, ~1 s (`SensorSystemsLink.cs:162-269`). Scan-period gating only with the reaction-time ("OODA") options on
(`_oodaPlayerScale` / `_oodaHostileScale` default 0, `Globals.cs:128-130`): then each radar paints all bearings at once, random phase, once per scan period, and a contact must
be painted on two consecutive sweeps (`SearchRadar.cs:32-61, 146-154`; verified in code: the `_sweepGating` / `_nextPaintTime` logic exists).

**Fire-control radar:** only geometry checks on its `_associatedTargets`, no radar equation (`FireControlRadar.cs:18-80`).

**Horizon:** `RadarCalculator.getRadarHorizon(radarY, targetY, visual, terrainAlt)` (`:27-43`): 4/3-earth for radar, true earth for visual; inputs/outputs in Unity units.

**Callable for a hypothetical target (no ObjectBase needed):**
- `RadarCalculator.GetFCRDetectionRange(Vector3 pos, float rcs, float waveLength, float gainFactor, float peakPower, List<SensorSystemECM> jammers, Globals.Frequency f, Crew crew, float jamResistance, ObjectBase selfScreen = null, ObjectBase ignoreOwnedBy = null, bool ignoreSupport = false)` (`:160`; verified signature) – pure radar equation + jamming, Unity units. **Mutates the jammer list** (`RemoveUnusableJammers`): pass a copy of `unit._ECMSystemsTryingToJamMe` or an empty list.
- then take min with `getRadarHorizon(...)` and `_rp._maxRange`, and apply the sea-state factor yourself.
- optional `RadarLowAltitude.PropagationFactor(...)`, `ClutterRangeFactor(...)` (`:26, :88`, metres).
- `SensorSystem.GetRange(ObjectBase target, SensorRangeOptions)` (radar `SensorSystemRadar.cs:383`, visual `:186`, ESM `SensorSystemESM.cs:23`) needs a real target; returns `SensorDistanceData {Distance, Issue}`; radar uses maximum RCS.

**ESM:** one-way received power Pt·Gt·Gr·λ²/(4πR)² ≥ `_minDetectableReturnPowerESM` (4e-10) (`RadarCalculator.cs:221-328`, `ESM.cs:388`); also limited by horizon × `_overHorizonMultiplier`
and LOS (`ESM.cs:95`). Only guiding FCRs count as emitters, plus offensive/noise jammers. Closed-form range in `SensorSystemESM.cs:71`.

**Visual/IR:** range = target `VisualIdentificationRange` × aspect factor × environment × `_maxRangeMultiplier`, capped ~15 nm × multiplier (`SensorUtils.cs:358-397`, `SensorSystemVisual.cs:55, 319`);
environment = clouds, rain ×0.75, snow ×0.5, fog ×0.33, sun/moon light + `_nightVisionLevel` (`SensorUtils.cs:399-525`; the game's `SensorInfoViewModel.PlotVisual` shows the same formula). IR skips
the light factor. Hypothetical-friendly: `SensorUtils.CalculateMaxVisualRange(VisualSizeClass, nightVision)` (`:279`), `ModifyVisualIdentificationRangeByEnvironmentalConditions(null, nv, ownPos, tgtPos, baseRange, …)` (`:399`, accepts a null target).

The game's own radar plot (`SeapowerUI.ViewModels/SensorInfoViewModel.cs` `PlotRadar`, ~line 1004; read in this session) uses: range = (peakPower·1000 · gain² · λ² · RCS / (minDetectableReturnPowerRadar·1000 · (4π)³))^¼
× 0.0148809375 (→ Unity units, then min with `_maxRange`), sea-state factor, and `RadarCalculator.getRadarHorizon(sensor Y, target altitude)`.

## 3. Terrain masking
- Unit pairs: `UnitSensorCache.UnitsHaveRadarlLOS / UnitsHaveVisualLOS` (`UnitSensorCache.cs:25-51`): horizon check then `UnitsHaveDirectlLOS` = `Physics.Raycast` on layer mask 1024, hull to hull at +3 m (not antenna height); cached per pair per frame (cleared `GameUpdater.cs:51`).
- Arbitrary points: **`TerrainUtils.IsLineOfSightBetween(GeoPosition a, GeoPosition b, ObjectBase observer = null, ObjectBase target = null)` → `LineOfSightResult`** (`TerrainUtils.cs:682`; exists in both builds): raycasts loaded terrain chunks, DEM line elsewhere; shared static state → main thread only.
- Terrain height for the horizon: `Utils.TerrainAltForHorizon(Vector3, Vector3)` (`Utils.cs:1931`; in the globe build the overloads are `(ObjectBase, ObjectBase = null)`).

## 4. What a detection produces
- `TrackComponent` (`TrackComponent.cs:6-30`): `Source`, `Sensor` (= `sensorSystem.InstanceEntity`), `SensorType`, `Epoch`, `MaxAge`, `OwnPosition`, **`TrueTargetPosition` (ground truth – never show)**, `Kind`, `Confidence`.
- Position components `TrackBearing`, `TrackRangeInMeters`, `TrackAltitude`, each a `PointEstimate {Estimate, Error}`. Radar error ≈ 0 undamaged (damage → 5/10/20/115 nm, 5/10/15/20°; radar `Error` is a variance). Visual error 0. **ESM is bearing-only**, `Error` = angular resolution (degrees, not squared); jammer tracks 5× that (`ESM.cs:271-278, 348`).
- Signal strength: `IncomingSignalData._incomingSignalValue` (search radar: aspect RCS; FCR: gain; ESM: received power in W; visual: aspect factor).
- Read path: `unit._obp._plottingTable.VehicleForObject(C)` (also `unit.PlottingTable`) → `.CurrentTracks()` / `.ValidTracks()` (`Vehicle.cs:729-787`) → keep tracks with `TrackComponent.Sensor == S.InstanceEntity` (the game does this itself at `SensorSystemVisual.cs:302-306`).
- Simpler: `S._detectedObjects` / `_identifiedObjects` (ground truth, last scan – internal use only); or **subscribe**: each `ISensor` is a UniRx `IObservable<Entity>` (`S.Sensor.Subscribe(IObserver<Entity>)`, `ObjectBaseLoader.cs:2054`; `Sonar.Subscribe`, `SensorSystemVisual.Subscribe`, `SensorSystemMAD.Subscribe` exist in both builds). Read inside `OnNext` – entities with `Refs <= 0` are destroyed by `TrackCleanerUpper`.

## 5. Burst vs managed
Managed (callable/patchable): all scans (`runVisualScan`, `CalculateRawSensorContacts`, `createPassiveESMContacts`), range maths, LOS, ECM, `PlottingTable`/`Vehicle` APIs; `SensorSystemsLinkJob` is an empty stub.
Burst: only fusion (`Vehicle*Update`, `TrackCleanerUpper`, `TrackSynthesizerUpdater`). Cost: each scan is O(all objects) per sensor; `GetRange`/`createRadarEcho` draw from the pooled `Globals._sensorEchoes` (reset each datalink cycle, `TaskforceManager.cs:49`) – heavy mod use just grows the pool.
Coverage polygon: 360 bearings × binary search with ~8 raycasts; recompute ~1 Hz or on change, not per frame.

## 6. Other scope data
- RWR / who paints us: `unit._rwrBearings` and `_jamBearings` are cleared each datalink cycle (`Taskforce.cs:667-673`) → snapshot before `updateDataLinkStart`; RWR only for player units (`ESM.cs:106`); `RegisterRadarLockWarningSource(source, bearing)` (`ObjectBase.cs:1952`) is the postfix point. Ground truth alternative: any enemy `_targetingRadarSystems` entry with `_isGuiding && _associatedTargets.Contains(unit)`.
- Fire-control lock: `_isGuiding`, `_targetObject`, `_associatedTargets`, `_associatedWeapons`, `_solution`.
- Jamming: `unit._ECMSystemsTryingToJamMe`; `SensorSystemECM.Jams`, `HeadingOfJam`, `_ecm._ep._jamConeFov`, `_maxRange` (`SensorSystemECM.cs:19-29, 121-183`).
- ESM emitters: `ESM._esmObservationData` keys = the observed emitter `SensorSystem`s (`ESM.cs:64`); bearings from tracks; `Vehicle.BearingFan` / `TryGetLatestBearingTrack` (`Vehicle.cs:885`) = origin, bearing, half-angle.
- Free diagnostics (ground truth, debugging only): `DM_Sensors._debugRadar` / `_debugVisual` = one sensor; its next scan fills `_debugRadarData` (`DM_Sensors.cs:49-71`).
