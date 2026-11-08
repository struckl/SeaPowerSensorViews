using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerSensorViews.Data;
using UnityEngine;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// Sensor coverage of the unit selected in the game against an imagined target (never a real contact): the same maths the game's
/// own Sensor Info window uses for its range graphs.
/// <list type="bullet">
/// <item>Radar: radar equation for a target of the chosen RCS, capped by the radar's maximum range, the radar horizon against the
/// target altitude and the radar's altitude band; then, per ray, the first place the terrain blocks the line of sight
/// (<c>TerrainUtils.IsLineOfSightBetween</c>, which uses shared static state and Unity physics - main thread only, so a few rays per
/// frame).</item>
/// <item>ESM: the game's closed form for an emitter like the unit's own search radar, capped by the horizon times the ESM's
/// over-horizon multiplier.</item>
/// <item>Visual: <c>SensorUtils.CalculateMaxVisualRange</c> for the chosen size class, capped by the visual horizon.</item>
/// <item>Passive sonar: the game's own passive range estimate for the sonar and the unit's noise at its speed; a hull sonar loses
/// the sector of its baffles (the game ignores contacts in them).</item>
/// </list>
/// The arithmetic is trivial (a few hundred operations), so there is no worker thread; the only expensive part is the terrain
/// check and that cannot leave the main thread. A new job starts when the unit, its place (about 1 nm), the target altitude or a
/// setting changes, at most once a second; the previous result stays on the map until the new one is done.
/// </summary>
internal sealed class CoverageJob
{
    private const int RaysPerFrame = 6;
    private const int SamplesPerRay = 14;
    private const float UnitsPerNm = 27.5603f;

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

    public bool Running => running;

    public void Reset()
    {
        running = false;
        haveShown = false;
        job = null;
        Result.Rings.Clear();
        Result.Version++;
    }

    /// <summary>Called every frame while the layer is on: decides about a new job and works a few rays of the running one.</summary>
    public void Tick(double now, UnitRow unit)
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
            Lat = (int)Math.Round(pos._latitude * 60.0 / 1.0),       // 1 arc minute = 1 nm
            Lon = (int)Math.Round(pos._longitude * 60.0 * Math.Cos(pos._latitude * Math.PI / 180.0)),
            Settings = Settings.Version,
            Altitude = (int)Settings.CoverageAltitude.Value,
        };
        if (!running && now >= nextStart && (!haveShown || !key.Equals(shown)))
        {
            nextStart = now + 1.0;
            Start(unit, pos, key);
        }
        if (running)
            Work();
    }

    private void Start(UnitRow unit, GeoPosition pos, Key key)
    {
        wanted = key;
        job = unit.Unit;
        origin = pos;
        rays = Settings.Rays.Value;
        limit = new float[rays];
        radarMax = new float[rays];
        sonarRange = new float[rays];
        float altFt = Settings.CoverageAltitude.Value;
        bool surfaceTarget = altFt <= 30f;
        targetY = surfaceTarget ? 0f : altFt / SensorPicture.FeetPerUnit;
        float rcs = Settings.TargetRcs.Value;
        haveRadar = haveEsm = haveVisual = haveSonar = false;
        float radarD = 0f, bestEmitter = 0f, emitterGain = 0f, emitterLambda = 0f;
        sensorY = unit.SensorHeightUnits;
        radarTop = sensorY;
        esmUnits = visualUnits = 0f;
        float esmGain = 0f, esmOverHorizon = 1.3f, esmY = sensorY;
        float visY = unit.Unit.transform.position.y + 0.03f;
        float visualRange = 0f;
        var systems = unit.Unit._obp?._sensorSystems;
        float sonarNm = 0f;
        SensorSystemSonar bestSonar = null;
        if (systems != null)
        {
            foreach (SensorSystem s in systems)
            {
                if (s == null || s.Inoperable.Value) continue;
                switch (s)
                {
                    case SensorSystemRadar r when r._type == SensorSystemRadar.RadarType.Search && r._radar?._rp != null:
                    {
                        RadarParameters rp = r._radar._rp;
                        bool surface = surfaceTarget;
                        bool inBand = rp._maxAltitude <= 0f ? surface : (targetY <= rp._maxAltitude && targetY >= Mathf.Max(rp._minAltitude, 0f));
                        float power = rp._peakPower * rp._gainFactor;
                        if (power > bestEmitter) { bestEmitter = power; emitterGain = rp._gainFactor; emitterLambda = rp._waveLength; }
                        if (!inBand) break;
                        float eq = (float)Math.Pow(rp._peakPower * 1000f * Mathf.Pow(rp._gainFactor, 2f) * Mathf.Pow(rp._waveLength, 2f) * rcs /
                                                   (Globals.RcsValues._minDetectableReturnPowerRadar * 1000.0 * Math.Pow(Math.PI * 4.0, 3.0)), 0.25);
                        if (surface && Singleton<SeaPower.Environment>.Instance.SeaState > 1)
                            eq *= 1f - 0.05f * Singleton<SeaPower.Environment>.Instance.SeaState;
                        float range = Mathf.Min(eq * 0.0148809375f, rp._maxRange);
                        float y = r._mountObject != null ? r.SensorPosition.y : sensorY;
                        range = Mathf.Min(range, RadarCalculator.getRadarHorizon(y, targetY));
                        if (range > radarD) { radarD = range; radarTop = y; haveRadar = true; }
                        break;
                    }
                    case SensorSystemESM e when e._esm?._ep != null:
                        esmGain = Mathf.Max(esmGain, e._esm._ep._gainFactor);
                        esmOverHorizon = e._esm._ep._overHorizonMultiplier > 0f ? e._esm._ep._overHorizonMultiplier : esmOverHorizon;
                        esmY = e.SensorPosition.y;
                        break;
                    case SensorSystemVisual v:
                    {
                        SensorUtils.VisualSizeClass size = Enum.TryParse(Settings.VisualSize.Value, out SensorUtils.VisualSizeClass parsed) ? parsed : SensorUtils.VisualSizeClass.Medium;
                        float range = SensorUtils.CalculateMaxVisualRange(size, v._nightVisionLevel) * v._maxRangeMultiplier;
                        if (range > visualRange) { visualRange = range; haveVisual = true; }
                        break;
                    }
                    case SensorSystemSonar so when so._sonar != null && !so._sonar.IsActive.Value && so.IsOn.Value:
                    {
                        float nm = PointValueSensorScorer.EstimatePassiveSonarRangeNmi(so._systemName, unit.Unit._sensorData, unit.SpeedKts, unit.Unit._type == ObjectBase.ObjectType.Submarine);
                        if (nm > sonarNm) { sonarNm = nm; bestSonar = so; }
                        break;
                    }
                }
            }
        }
        if (esmGain > 0f && bestEmitter > 0f)
        {
            float units = (float)Math.Sqrt(bestEmitter * esmGain * emitterLambda * emitterLambda / 157.9136792061263 / Globals.RcsValues._minDetectableReturnPowerESM) * 0.0148809375f;
            esmUnits = Mathf.Min(units, esmOverHorizon * RadarCalculator.getRadarHorizon(esmY, sensorY));
            haveEsm = esmUnits > 1f;
        }
        if (haveVisual)
            visualUnits = Mathf.Min(visualRange, RadarCalculator.getRadarHorizon(visY, targetY, true));
        if (sonarNm > 0f && bestSonar != null)
        {
            haveSonar = true;
            SonarParameters sp = bestSonar._sonar._sp;
            bool towed = bestSonar.IsTowedSensor || bestSonar.IsVDS;
            for (int i = 0; i < rays; i++)
            {
                // The ray i points at the compass bearing i * 360 / rays; the baffles are fixed to the hull, so the dent sits where that
                // bearing lies relative to the bow. (It used the compass bearing as the relative one: the dent was always aft of north.)
                float rel = SensorPicture.Wrap360(i * 360f / rays - unit.Heading);
                if (rel > 180f) rel -= 360f;
                bool dent = !towed && sp != null && sp._baffles != Vector2.zero && InBaffles(sp._baffles, rel);
                sonarRange[i] = sonarNm * UnitsPerNm * (dent ? 0.08f : 1f);
            }
        }
        for (int i = 0; i < rays; i++)
            radarMax[i] = limit[i] = haveRadar ? radarD : 0f;
        rayIndex = 0;
        running = true;
        if (bestEmitter <= 0f) { emitterGain = 0f; }
    }

    // The same test as Sonar.IsInBaffles; "rel" is the bearing from the bow, starboard positive.
    private static bool InBaffles(Vector2 baffles, float rel) =>
        baffles.x < baffles.y ? (baffles.x < rel && rel < baffles.y) : (baffles.y >= rel || rel >= baffles.x);

    private void Work()
    {
        if (job == null || job.IsDestroyed) { running = false; return; }
        int done = 0;
        bool shadows = Settings.TerrainShadows.Value && haveRadar;
        // At most RaysPerFrame rays, and no more than about a millisecond of terrain checks per frame (at least one ray).
        long until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 1000;
        while (rayIndex < rays && done < RaysPerFrame && (done == 0 || System.Diagnostics.Stopwatch.GetTimestamp() < until))
        {
            if (shadows)
            {
                try { limit[rayIndex] = Shadow(rayIndex); }
                catch (Exception ex) { ModLog.Debug("terrain check: " + ex.Message); limit[rayIndex] = radarMax[rayIndex]; }
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
        GeoPosition to = from.OffsetByDistanceOnHeadingUnity(units / UnitsPerNm * 1.852f, bearing);
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
        if (haveRadar && Settings.CoverRadar.Value) AddPolygon(set, CoverageKind.Radar, limit, true);
        if (haveSonar && Settings.CoverSonar.Value) AddPolygon(set, CoverageKind.Sonar, sonarRange, true);
        if (haveEsm && Settings.CoverEsm.Value) AddCircle(set, CoverageKind.Esm, esmUnits);
        if (haveVisual && Settings.CoverVisual.Value) AddCircle(set, CoverageKind.Visual, visualUnits);
        set.Status = (haveRadar ? "radar " : "") + (haveEsm ? "ESM " : "") + (haveVisual ? "visual " : "") + (haveSonar ? "sonar" : "");
        set.Version++;
    }

    private void AddPolygon(CoverageSet set, CoverageKind kind, float[] ranges, bool fill)
    {
        int n = ranges.Length;
        var lat = new double[n];
        var lon = new double[n];
        for (int i = 0; i < n; i++)
        {
            GeoPosition p = origin.OffsetByDistanceOnHeadingUnity(ranges[i] / UnitsPerNm * 1.852f, i * 360.0 / n);
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

    private void AddCircle(CoverageSet set, CoverageKind kind, float units)
    {
        var ranges = new float[Math.Max(36, rays)];
        for (int i = 0; i < ranges.Length; i++) ranges[i] = units;
        int n = ranges.Length;
        var lat = new double[n + 1];
        var lon = new double[n + 1];
        for (int i = 0; i <= n; i++)
        {
            GeoPosition p = origin.OffsetByDistanceOnHeadingUnity(units / UnitsPerNm * 1.852f, (i % n) * 360.0 / n);
            lat[i] = p._latitude;
            lon[i] = p._longitude;
        }
        set.Rings.Add(new CoverageSet.Ring { Kind = kind, Fill = false, Lat = lat, Lon = lon });
    }
}
