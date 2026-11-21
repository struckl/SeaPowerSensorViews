using System;
using SeaPower;
using SeaPowerSensorViews.Data;
using UnityEngine;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// Sensor coverage of the unit selected in the game against an imagined target (never a real contact): the same maths the game's
/// own Sensor Info window uses for its range graphs.
/// - Radar: radar equation for a target of the chosen RCS, capped by the radar's maximum range, the radar horizon against the
/// target altitude and the radar's altitude band; then, per ray, the first place the terrain blocks the line of sight
/// (<c>TerrainUtils.IsLineOfSightBetween</c>, which uses shared static state and Unity physics - main thread only, so a few rays per
/// frame).
/// - ESM: the game's closed form for an emitter like the unit's own search radar, capped by the horizon times the ESM's
/// over-horizon multiplier.
/// - Visual: <c>SensorUtils.CalculateMaxVisualRange</c> for the chosen size class, capped by the visual horizon.
/// - Passive sonar: the game's own passive range estimate for the sonar and the unit's noise at its speed; a hull sonar loses
/// the sector of its baffles (the game ignores contacts in them).
/// The arithmetic is trivial (a few hundred operations), so there is no worker thread; the only expensive part is the terrain
/// check and that cannot leave the main thread. A new job starts when the unit, its place (about 1 nm), the target altitude or a
/// setting changes, at most once a second; the previous result stays on the map until the new one is done.
/// </summary>
internal sealed class CoverageJob
{
    private const int RaysPerFrame = 6;
    private const int SamplesPerRay = 14;
    /// <summary>A target at or below this altitude (ft) is a surface target: on the sea, under a surface-only radar's band.</summary>
    private const float SurfaceTargetFt = 30f;
    /// <summary>The ESM over-horizon multiplier when the ESM gives none.</summary>
    private const float DefaultOverHorizon = 1.3f;
    /// <summary>Eye height above the unit's origin for the visual horizon (game units, about 2 m).</summary>
    private const float EyeHeightUnits = 0.03f;
    /// <summary>What is left of the passive sonar range inside the baffles: a dent, not a hole, so the ring stays one shape.</summary>
    private const float BaffleDent = 0.08f;

    /// <summary>What the coverage is worked out from (the settings, read by MapSet).</summary>
    public struct Inputs
    {
        public int Version;             // Settings.CoverageInputs: a change starts a new job
        public int Rays;
        public float AltitudeFt, Rcs;
        public string VisualSize;
        public bool TerrainShadows, Radar, Esm, Visual, Sonar;

        public static Inputs FromSettings() => new Inputs
        {
            Version = Settings.CoverageInputs.Version,
            Rays = Settings.Rays.Value,
            AltitudeFt = Settings.CoverageAltitude.Value,
            Rcs = Settings.TargetRcs.Value,
            VisualSize = Settings.VisualSize.Value,
            TerrainShadows = Settings.TerrainShadows.Value,
            Radar = Settings.CoverRadar.Value,
            Esm = Settings.CoverEsm.Value,
            Visual = Settings.CoverVisual.Value,
            Sonar = Settings.CoverSonar.Value,
        };
    }

    public readonly CoverageSet Result = new CoverageSet();

    private struct Key : IEquatable<Key>
    {
        public int Uid, Lat, Lon, Settings, Altitude;
        public bool Equals(Key o) => Uid == o.Uid && Lat == o.Lat && Lon == o.Lon && Settings == o.Settings && Altitude == o.Altitude;
        public override bool Equals(object o) => o is Key k && Equals(k);
        public override int GetHashCode() => Uid ^ (Lat << 8) ^ (Lon << 16) ^ Settings;
    }

    private Key shown, wanted;
    private bool haveShown;
    private double nextStart;
    private ObjectBase job;           // the unit the running job is for
    private Inputs inputs;            // of the running job
    private int rayIndex;
    private float[] limit;            // radar range per ray in Unity units, shrinking as terrain is found
    private float[] radarMax;
    private float[] sonarRange;
    private int rays;
    private GeoPosition origin;
    private float sensorY, targetY, radarTop;
    private float esmUnits, visualUnits;
    private bool haveRadar, haveEsm, haveVisual, haveSonar;
    private bool running;

    // What the sensor scan found for the job being set up.
    private float radarD, bestEmitter, emitterLambda, esmGain, esmOverHorizon, esmY, visualRange, sonarNm;
    private SensorSystemSonar bestSonar;

    public void Reset()
    {
        running = false;
        haveShown = false;
        job = null;
        Result.Unit = null;
        Result.Rings.Clear();
        Result.Version++;
    }

    /// <summary>Called every frame while the layer is on: decides about a new job and works a few rays of the running one.</summary>
    public void Tick(double now, UnitRow unit, Inputs settings)
    {
        if (unit == null || unit.Unit == null || unit.Unit.IsDestroyed)
        {
            if (haveShown || running)
                Reset();
            return;
        }
        GeoPosition pos = unit.Unit.getGeoPosition();
        var key = new Key
        {
            Uid = unit.Uid,
            Lat = (int)Math.Round(pos._latitude * 60.0),       // 1 arc minute = 1 nm
            Lon = (int)Math.Round(pos._longitude * 60.0 * Math.Cos(pos._latitude * Math.PI / 180.0)),
            Settings = settings.Version,
            Altitude = (int)settings.AltitudeFt,
        };
        if (!running && now >= nextStart && (!haveShown || !key.Equals(shown)))
        {
            nextStart = now + 1.0;
            Start(unit, pos, key, settings);
        }
        if (running)
            Work();
    }

    private void Start(UnitRow unit, GeoPosition pos, Key key, Inputs settings)
    {
        wanted = key;
        job = unit.Unit;
        inputs = settings;
        origin = pos;
        rays = settings.Rays;
        limit = new float[rays];
        radarMax = new float[rays];
        sonarRange = new float[rays];
        bool surfaceTarget = settings.AltitudeFt <= SurfaceTargetFt;
        targetY = surfaceTarget ? 0f : settings.AltitudeFt / Conv.FeetPerUnit;
        sensorY = unit.SensorHeightUnits;
        radarTop = sensorY;
        ScanSensors(unit, surfaceTarget);
        esmUnits = visualUnits = 0f;
        haveEsm = false;
        if (esmGain > 0f && bestEmitter > 0f)
        {
            float units = (float)Math.Sqrt(bestEmitter * esmGain * emitterLambda * emitterLambda / 157.9136792061263 / Globals.RcsValues._minDetectableReturnPowerESM) * Conv.UnitsPerMeter;
            esmUnits = Mathf.Min(units, esmOverHorizon * RadarCalculator.getRadarHorizon(esmY, sensorY));
            haveEsm = esmUnits > 1f;
        }
        if (haveVisual)
            visualUnits = Mathf.Min(visualRange, RadarCalculator.getRadarHorizon(unit.Unit.transform.position.y + EyeHeightUnits, targetY, true));
        haveSonar = sonarNm > 0f && bestSonar != null;
        if (haveSonar)
            SonarRays(unit.Heading);
        for (int i = 0; i < rays; i++)
            radarMax[i] = limit[i] = haveRadar ? radarD : 0f;
        rayIndex = 0;
        running = true;
    }

    private void ScanSensors(UnitRow unit, bool surfaceTarget)
    {
        haveRadar = haveVisual = false;
        radarD = bestEmitter = emitterLambda = esmGain = visualRange = sonarNm = 0f;
        esmOverHorizon = DefaultOverHorizon;
        esmY = sensorY;
        bestSonar = null;
        var systems = unit.Unit._obp?._sensorSystems;
        if (systems == null) return;
        foreach (SensorSystem s in systems)
        {
            if (s == null || s.Inoperable.Value) continue;
            switch (s)
            {
                case SensorSystemRadar r: ScanRadar(r, surfaceTarget); break;
                case SensorSystemESM e: ScanEsm(e); break;
                case SensorSystemVisual v: ScanVisual(v); break;
                case SensorSystemSonar so: ScanSonar(so, unit); break;
            }
        }
    }

    private void ScanEsm(SensorSystemESM e)
    {
        if (e._esm?._ep == null) return;
        esmGain = Mathf.Max(esmGain, e._esm._ep._gainFactor);
        esmOverHorizon = e._esm._ep._overHorizonMultiplier > 0f ? e._esm._ep._overHorizonMultiplier : esmOverHorizon;
        esmY = e.SensorPosition.y;
    }

    /// <summary>A passive sonar that is on: the game's own range estimate for it at the unit's speed.</summary>
    private void ScanSonar(SensorSystemSonar so, UnitRow unit)
    {
        if (so._sonar == null || so._sonar.IsActive.Value || !so.IsOn.Value) return;
        float nm = PointValueSensorScorer.EstimatePassiveSonarRangeNmi(so._systemName, unit.Unit._sensorData, unit.SpeedKts, unit.Unit._type == ObjectBase.ObjectType.Submarine);
        if (nm > sonarNm) { sonarNm = nm; bestSonar = so; }
    }

    /// <summary>A search radar: its range against the imagined target, and its power as the emitter the ESM ring is worked out for.</summary>
    private void ScanRadar(SensorSystemRadar r, bool surfaceTarget)
    {
        if (r._type != SensorSystemRadar.RadarType.Search || r._radar?._rp == null) return;
        RadarParameters rp = r._radar._rp;
        bool inBand = rp._maxAltitude <= 0f ? surfaceTarget : (targetY <= rp._maxAltitude && targetY >= Mathf.Max(rp._minAltitude, 0f));
        float power = rp._peakPower * rp._gainFactor;
        if (power > bestEmitter) { bestEmitter = power; emitterLambda = rp._waveLength; }
        if (!inBand) return;
        float eq = (float)Math.Pow(rp._peakPower * 1000f * Mathf.Pow(rp._gainFactor, 2f) * Mathf.Pow(rp._waveLength, 2f) * inputs.Rcs /
                                   (Globals.RcsValues._minDetectableReturnPowerRadar * 1000.0 * Math.Pow(Math.PI * 4.0, 3.0)), 0.25);
        if (surfaceTarget && Singleton<SeaPower.Environment>.Instance.SeaState > 1)
            eq *= 1f - 0.05f * Singleton<SeaPower.Environment>.Instance.SeaState;
        float range = Mathf.Min(eq * Conv.UnitsPerMeter, rp._maxRange);
        float y = r._mountObject != null ? r.SensorPosition.y : sensorY;
        range = Mathf.Min(range, RadarCalculator.getRadarHorizon(y, targetY));
        if (range > radarD) { radarD = range; radarTop = y; haveRadar = true; }
    }

    private void ScanVisual(SensorSystemVisual v)
    {
        SensorUtils.VisualSizeClass size = Enum.TryParse(inputs.VisualSize, out SensorUtils.VisualSizeClass parsed) ? parsed : SensorUtils.VisualSizeClass.Medium;
        float range = SensorUtils.CalculateMaxVisualRange(size, v._nightVisionLevel) * v._maxRangeMultiplier;
        if (range > visualRange) { visualRange = range; haveVisual = true; }
    }

    private void SonarRays(float heading)
    {
        SonarParameters sp = bestSonar._sonar._sp;
        bool towed = bestSonar.IsTowedSensor || bestSonar.IsVDS;
        bool baffles = !towed && sp != null && sp._baffles != Vector2.zero;
        for (int i = 0; i < rays; i++)
        {
            // The ray i points at the compass bearing i * 360 / rays; the baffles are fixed to the hull, so the dent sits where that
            // bearing lies relative to the bow.
            float rel = SensorPicture.Wrap360(i * 360f / rays - heading);
            if (rel > 180f) rel -= 360f;
            bool dent = baffles && GeoMath.InBaffles(sp._baffles.x, sp._baffles.y, rel);
            sonarRange[i] = sonarNm * Conv.UnitsPerNm * (dent ? BaffleDent : 1f);
        }
    }

    private void Work()
    {
        if (job == null || job.IsDestroyed) { running = false; return; }
        int done = 0;
        bool shadows = inputs.TerrainShadows && haveRadar;
        // At most RaysPerFrame rays, and no more than about a millisecond of terrain checks per frame (at least one ray).
        long until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 1000;
        while (rayIndex < rays && done < RaysPerFrame && (done == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < until))
        {
            if (shadows)
            {
                try { limit[rayIndex] = Shadow(rayIndex); }
                catch (Exception ex) { Rig.Log.Debug("terrain check: " + ex.Message); limit[rayIndex] = radarMax[rayIndex]; }
            }
            rayIndex++;
            done++;
        }
        if (rayIndex >= rays)
            Finish();
    }

    // First blocked sample along the ray, then a few bisections between the last clear and the first blocked sample.
    private float Shadow(int ray)
    {
        float max = radarMax[ray];
        if (max <= 0f) return 0f;
        float bearing = ray * 360f / rays;
        float clear = 0f;
        GeoPosition from = new GeoPosition(origin._latitude, origin._longitude, radarTop);
        for (int s = 1; s <= SamplesPerRay; s++)
        {
            float d = max * s / SamplesPerRay;
            if (Visible(from, bearing, d))
            {
                clear = d;
                continue;
            }
            float lo = clear, hi = d;
            for (int k = 0; k < 4; k++)
            {
                float mid = 0.5f * (lo + hi);
                if (Visible(from, bearing, mid)) lo = mid; else hi = mid;
            }
            return lo;
        }
        return max;
    }

    private bool Visible(GeoPosition from, float bearing, float units)
    {
        GeoPosition to = from.OffsetByDistanceOnHeadingUnity(Conv.KmOfUnits(units), bearing);
        to._height = targetY;
        LineOfSightResult r = TerrainUtils.IsLineOfSightBetween(from, to);
        return !r._isValid || r._los;          // no terrain data there = nothing known to block it
    }

    private void Finish()
    {
        running = false;
        shown = wanted;
        haveShown = true;
        CoverageSet set = Result;
        set.Rings.Clear();
        set.Unit = job;
        set.Origin = origin;
        if (haveRadar && inputs.Radar) AddPolygon(set, CoverageKind.Radar, limit);
        if (haveSonar && inputs.Sonar) AddPolygon(set, CoverageKind.Sonar, sonarRange);
        if (haveEsm && inputs.Esm) AddCircle(set, CoverageKind.Esm, esmUnits);
        if (haveVisual && inputs.Visual) AddCircle(set, CoverageKind.Visual, visualUnits);
        set.Version++;
    }

    /// <summary>A filled area and its outline (the outline closed back to its first point).</summary>
    private void AddPolygon(CoverageSet set, CoverageKind kind, float[] ranges)
    {
        int n = ranges.Length;
        var lat = new double[n];
        var lon = new double[n];
        for (int i = 0; i < n; i++)
        {
            GeoPosition p = origin.OffsetByDistanceOnHeadingUnity(Conv.KmOfUnits(ranges[i]), i * 360.0 / n);
            lat[i] = p._latitude;
            lon[i] = p._longitude;
        }
        set.Rings.Add(new CoverageSet.Ring { Kind = kind, Fill = true, Lat = lat, Lon = lon });
        var closedLat = new double[n + 1];
        var closedLon = new double[n + 1];
        Array.Copy(lat, closedLat, n);
        Array.Copy(lon, closedLon, n);
        closedLat[n] = lat[0];
        closedLon[n] = lon[0];
        set.Rings.Add(new CoverageSet.Ring { Kind = kind, Fill = false, Lat = closedLat, Lon = closedLon });
    }

    /// <summary>An outline only, round at the same range.</summary>
    private void AddCircle(CoverageSet set, CoverageKind kind, float units)
    {
        int n = Math.Max(36, rays);
        var lat = new double[n + 1];
        var lon = new double[n + 1];
        float km = Conv.KmOfUnits(units);
        for (int i = 0; i <= n; i++)
        {
            GeoPosition p = origin.OffsetByDistanceOnHeadingUnity(km, (i % n) * 360.0 / n);
            lat[i] = p._latitude;
            lon[i] = p._longitude;
        }
        set.Rings.Add(new CoverageSet.Ring { Kind = kind, Fill = false, Lat = lat, Lon = lon });
    }
}
