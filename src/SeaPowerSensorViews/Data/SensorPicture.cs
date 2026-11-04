using System;
using System.Collections.Generic;
using SeaPower;
using Unity.Entities;
using UnityEngine;

namespace SeaPowerSensorViews.Data;

/// <summary>
/// Read-only snapshot of what each own unit's sensors currently hold, refreshed about once a second while a view is
/// open. Source: the player side's plotting table; every vehicle (contact) keeps its tracks, and a track names the unit
/// that made it (Source), the sensor and its estimates. Only estimates are read: TrueTargetPosition and the contact's
/// real object position are never touched. Map overlays added later read the same snapshot.
/// </summary>
internal sealed class SensorPicture
{
    public static readonly SensorPicture Current = new SensorPicture();

    public const float NmPerUnit = 1f / 27.5603f;
    public const float MetersPerUnit = 67.2f;
    public const float FeetPerUnit = 220.47266f;

    public readonly List<UnitRow> Units = new List<UnitRow>();
    public readonly List<ContactRow> Contacts = new List<ContactRow>();
    public double Time;
    public int Version;               // bumped on every refresh
    public bool InMission;

    private readonly Dictionary<ObjectBase, UnitRow> rows = new Dictionary<ObjectBase, UnitRow>();
    private readonly Dictionary<Entity, UnitRow> byEntity = new Dictionary<Entity, UnitRow>();
    private readonly Dictionary<long, Hit> hits = new Dictionary<long, Hit>();
    private readonly Dictionary<int, ContactRow> contactRows = new Dictionary<int, ContactRow>();
    private readonly List<long> stale = new List<long>();
    private readonly List<ObjectBase> gone = new List<ObjectBase>();
    private int stamp;
    private double nextRefresh;

    public UnitRow RowFor(ObjectBase unit) => unit != null && rows.TryGetValue(unit, out UnitRow r) ? r : null;

    public void Reset()
    {
        Units.Clear(); Contacts.Clear(); rows.Clear(); byEntity.Clear(); hits.Clear(); contactRows.Clear();
        InMission = false; nextRefresh = 0;
    }

    /// <summary>Refreshes when due (about once a second of real time). True when a new snapshot was made.</summary>
    public bool Tick(double realNow, double interval = 1.0)
    {
        if (realNow < nextRefresh)
            return false;
        nextRefresh = realNow + interval;
        try { Refresh(); }
        catch (Exception ex) { ModLog.Error("sensor picture: " + ex); return false; }
        return true;
    }

    public void Refresh()
    {
        Taskforce player = Globals._playerTaskforce;
        PlottingTable table = player?.PlottingTable;
        if (player == null || table == null)
        {
            InMission = false;
            return;
        }
        InMission = true;
        stamp++;
        Time = GameTime.missionSessionTime;
        Version++;
        float memory = Settings.MemorySeconds.Value;

        // ---- own units -----------------------------------------------------------------------------------------
        Units.Clear();
        foreach (ObjectBase unit in player._taskforceUnits)
        {
            if (unit == null || unit.IsDestroyed || !unit.isUnit() || unit._type == ObjectBase.ObjectType.Biologic)
                continue;
            if (unit._obp != null && unit._obp._unitRoles.Contains(ObjectBaseParameters.UnitRoles.LifeRaft))
                continue;
            if (!rows.TryGetValue(unit, out UnitRow row))
            {
                row = new UnitRow { Unit = unit, Uid = unit.UniqueID };
                rows[unit] = row;
            }
            row.Entity = unit.Entity;
            byEntity[row.Entity] = row;
            row.Stamp = stamp;
            ReadUnit(row);
            Units.Add(row);
        }
        gone.Clear();
        foreach (var kv in rows)
            if (kv.Value.Stamp != stamp) gone.Add(kv.Key);
        foreach (ObjectBase g in gone)
        {
            byEntity.Remove(rows[g].Entity);
            rows.Remove(g);
        }

        // ---- tracks --------------------------------------------------------------------------------------------
        EntityManager em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        var vehicles = table.Vehicles;
        for (int i = 0; i < vehicles.Count; i++)
        {
            Vehicle v = vehicles[i];
            if (v == null || v.BaseObject == null || v.BaseObject._taskforce == player)
                continue;
            foreach (Entity e in v.CurrentTracks())
            {
                if (!em.HasComponent<TrackComponent>(e)) continue;
                TrackComponent tc = em.GetComponentData<TrackComponent>(e);
                if (!KindInfo.TryMap(tc.SensorType, out Kind kind)) continue;
                if (!byEntity.TryGetValue(tc.Source, out UnitRow unitRow)) continue;
                if (!em.HasComponent<TrackBearing>(e)) continue;

                long key = ((long)unitRow.Uid << 32) ^ ((long)v.Id << 4) ^ (long)kind;
                double fix = tc.Epoch.ToLive();
                if (!hits.TryGetValue(key, out Hit hit))
                {
                    hit = new Hit { Unit = unitRow, ContactId = v.Id, Kind = kind };
                    hits[key] = hit;
                }
                else if (hit.Stamp == stamp && hit.FixTime >= fix)
                    continue;                                   // another track of the same sensor kind, older
                hit.Unit = unitRow;
                hit.Stamp = stamp;
                hit.Live = true;
                hit.FixTime = fix;
                hit.MaxAge = tc.MaxAge;
                if (hit.SensorName == null || !hit.SensorNameRaw.Equals(tc.SensorDisplayName))
                {
                    hit.SensorNameRaw = tc.SensorDisplayName;
                    hit.SensorName = tc.SensorDisplayName.ToString();
                }
                PointEstimate b = em.GetComponentData<TrackBearing>(e).Estimate;
                hit.Bearing = Wrap360(b.Estimate);
                if (hit.History.Count == 0 || hit.History[hit.History.Count - 1].Key < (float)fix)
                {
                    hit.History.Add(new KeyValuePair<float, float>((float)fix, hit.Bearing));
                    while (hit.History.Count > 1 && (float)fix - hit.History[0].Key > 600f) hit.History.RemoveAt(0);
                }
                hit.BearingError = kind == Kind.Esm ? b.Error : Mathf.Sqrt(Mathf.Max(0f, b.Error));
                hit.BearingOnly = !em.HasComponent<TrackRangeInMeters>(e);
                hit.RangeM = hit.BearingOnly ? float.NaN : em.GetComponentData<TrackRangeInMeters>(e).Estimate.Estimate;
                hit.AltitudeUnits = em.HasComponent<TrackAltitude>(e) ? em.GetComponentData<TrackAltitude>(e).Estimate.Estimate : float.NaN;
                hit.AltitudeError = em.HasComponent<TrackAltitude>(e) ? em.GetComponentData<TrackAltitude>(e).Estimate.Error : 0f;
                hit.Signal = em.HasComponent<IncomingSignalData>(e) ? (float)em.GetComponentData<IncomingSignalData>(e)._incomingSignalValue : float.NaN;
            }
        }

        // ---- remembered / expired, then group by contact -------------------------------------------------------
        foreach (ContactRow c in contactRows.Values) c.Hits.Clear();
        stale.Clear();
        foreach (var kv in hits)
        {
            Hit h = kv.Value;
            if (h.Stamp != stamp) h.Live = false;
            double age = Time - h.FixTime;
            if (!h.Live && (age > memory || h.Unit.Stamp != stamp)) { stale.Add(kv.Key); continue; }
            if (!contactRows.TryGetValue(h.ContactId, out ContactRow cr))
            {
                cr = new ContactRow { Id = h.ContactId };
                contactRows[h.ContactId] = cr;
            }
            cr.Hits.Add(h);
            cr.Stamp = stamp;
        }
        foreach (long k in stale) hits.Remove(k);

        Contacts.Clear();
        for (int i = 0; i < vehicles.Count; i++)
        {
            Vehicle v = vehicles[i];
            if (v == null || !contactRows.TryGetValue(v.Id, out ContactRow cr) || cr.Stamp != stamp || cr.Hits.Count == 0)
                continue;
            if (cr.Listed == stamp) continue;
            cr.Listed = stamp;
            Describe(cr, v);
            Contacts.Add(cr);
        }
        Contacts.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private static void Describe(ContactRow cr, Vehicle v)
    {
        // The class is shown only once the game itself shows it for the contact (identified). The label is made again only
        // when that changes.
        string cls = null;
        try
        {
            if (v.Identified.Value && v.BaseObject != null)
                cls = v.BaseObject.ClassNameShort;
        }
        catch (Exception) { /* keep the plain label */ }
        if (cr.Label == null || !string.Equals(cls, cr.LabelClass))
        {
            cr.LabelClass = cls;
            cr.Label = string.IsNullOrEmpty(cls) ? "T" + v.Id : "T" + v.Id + " " + cls;
        }
        switch (v.CurrentRelationship())
        {
            case RelationsState.Friendly: cr.Relation = 1; break;
            case RelationsState.Neutral: cr.Relation = 2; break;
            case RelationsState.Hostile: cr.Relation = 3; break;
            default: cr.Relation = 0; break;
        }
    }

    private static void ReadUnit(UnitRow row)
    {
        ObjectBase u = row.Unit;
        row.Name = u.getName();
        row.Emcon = u.Emcon.Value;
        row.Heading = u.getHeading();
        row.SpeedKts = Mathf.Abs(u._velocityInKnots);
        float y = u.transform.position.y;
        row.DepthFt = u._type == ObjectBase.ObjectType.Submarine && y < 0f ? -y * FeetPerUnit : float.NaN;
        row.AltitudeFt = u.IsAirUnit ? y * FeetPerUnit : float.NaN;
        row.Kind ??= u._type.ToString();
        float height = y;
        var systems = u._obp?._sensorSystems;
        if (systems == null) { row.Sensors.Clear(); row.SensorHeightUnits = y; return; }
        // The rows are made once per sensor (names, arcs, details are fixed); a refresh only reads the switches. Made again when the
        // unit's sensor list changes. This used to build every row with its strings again each second - garbage for every own unit.
        int n = 0;
        foreach (SensorSystem s in systems)
            if (s != null) n++;
        bool same = row.Sensors.Count == n;
        if (same)
        {
            int k = 0;
            foreach (SensorSystem s in systems)
                if (s != null && !ReferenceEquals(row.Sensors[k++].System, s)) { same = false; break; }
        }
        if (!same)
        {
            row.Sensors.Clear();
            foreach (SensorSystem s in systems)
                if (s != null) row.Sensors.Add(MakeRow(s));
        }
        for (int i = 0; i < row.Sensors.Count; i++)
        {
            SensorRow sr = row.Sensors[i];
            SensorSystem s = sr.System;
            sr.On = s.IsOn.Value;
            sr.Inoperable = s.Inoperable.Value;
            sr.State = s.DisplayState;
            switch (s)
            {
                case SensorSystemRadar r:
                    sr.Active = sr.On;
                    height = Mathf.Max(height, r.SensorPosition.y);
                    break;
                case SensorSystemSonar so:
                    bool active = so._sonar != null && so._sonar.IsActive.Value;
                    if (active != sr.Active || sr.Detail == null)
                    {
                        sr.Active = active;
                        sr.Letter = active ? "A" : "S";
                        sr.Detail = (so.IsTowedSensor ? "towed " : so.IsVDS ? "VDS " : "") + (active ? "active" : "passive");
                    }
                    break;
                case SensorSystemECM: sr.Active = sr.On; break;
            }
        }
        row.SensorHeightUnits = height;
    }

    private static SensorRow MakeRow(SensorSystem s)
    {
        var sr = new SensorRow { System = s, Name = s.DisplayName.Value };
        Vector2 arc = s._horizontalViewArc;
        sr.ArcMin = arc.x; sr.ArcMax = arc.y;
        sr.Arc = arc.x <= -180f && arc.y >= 180f ? "all round" : arc.x.ToString("+0;-0;0") + ".." + arc.y.ToString("+0;-0;0");
        switch (s)
        {
            case SensorSystemRadar r:
                sr.Letter = r._type == SensorSystemRadar.RadarType.Targeting ? "F" : "R";
                sr.Detail = r._type + (r._radar?._rp != null ? ", max " + (r._radar._rp._maxRange * NmPerUnit).ToString("0") + " nm" : "");
                break;
            case SensorSystemESM: sr.Letter = "E"; sr.Detail = "passive"; break;
            case SensorSystemSonar: sr.Letter = "S"; break;                     // letter and detail follow the mode in ReadUnit
            case SensorSystemVisual: sr.Letter = "V"; sr.Detail = "visual / infrared"; break;
            case SensorSystemMAD: sr.Letter = "M"; sr.Detail = "MAD"; break;
            case SensorSystemECM: sr.Letter = "J"; sr.Detail = "jammer"; break;
            default: sr.Letter = "?"; break;
        }
        return sr;
    }

    /// <summary>Copies the unit's radar warning and jam bearings into short-lived strobes (the game clears them every cycle).</summary>
    public void PollStrobes()
    {
        if (!InMission) return;
        double now = GameTime.missionSessionTime;
        for (int n = 0; n < Units.Count; n++)
        {
            UnitRow row = Units[n];
            ObjectBase u = row.Unit;
            bool changed = Collect(row, u._rwrBearings, false, now) | Collect(row, u._jamBearings, true, now);
            for (int i = row.Strobes.Count - 1; i >= 0; i--)
                if (now - row.Strobes[i].Time > 8.0) { row.Strobes.RemoveAt(i); changed = true; }
            if (changed) row.StrobeVersion++;
        }
    }

    /// <summary>True when a strobe was added (a known one only gets its time refreshed).</summary>
    private static bool Collect(UnitRow row, List<float> bearings, bool jam, double now)
    {
        bool added = false;
        if (bearings == null) return false;
        for (int i = 0; i < bearings.Count; i++)
        {
            float b = Wrap360(bearings[i]);
            Strobe found = null;
            for (int k = 0; k < row.Strobes.Count; k++)
            {
                Strobe s = row.Strobes[k];
                if (s.Jam == jam && Mathf.Abs(Mathf.DeltaAngle(s.Bearing, b)) < 1.5f) { found = s; break; }
            }
            if (found == null && row.Strobes.Count < 24)
            {
                found = new Strobe { Jam = jam };
                row.Strobes.Add(found);
                added = true;
            }
            if (found != null) { found.Bearing = b; found.Time = now; }
        }
        return added;
    }

    public static float Wrap360(float a)
    {
        a %= 360f;
        return a < 0f ? a + 360f : a;
    }
}
