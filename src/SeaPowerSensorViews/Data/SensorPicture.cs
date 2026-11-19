using System;
using System.Collections.Generic;
using SeaPower;
using Unity.Entities;
using UnityEngine;

namespace SeaPowerSensorViews.Data;

/// <summary>
/// Read-only snapshot of what each own unit's sensors currently hold, refreshed while a window or a map layer needs it; only the
/// host refreshes it (Host.RefreshPicture), everything else reads <see cref="Current"/>. Source: the player side's plotting table;
/// every vehicle (contact) keeps its tracks, and a track names the unit that made it (Source), the sensor and its estimates. Only
/// estimates are read: TrueTargetPosition and the contact's real object position are never touched.
/// </summary>
internal sealed class SensorPicture
{
    public static readonly SensorPicture Current = new SensorPicture();

    /// <summary>How long tracks are kept: <see cref="MemorySeconds"/> after they are lost, a visual track at most
    /// <see cref="VisualSeconds"/> after its last sighting (0 = as long as the game keeps it).</summary>
    public struct Limits
    {
        public float MemorySeconds, VisualSeconds;
    }

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
    private double nextRefresh, lastRefreshReal;

    public UnitRow RowFor(ObjectBase unit) => unit != null && rows.TryGetValue(unit, out UnitRow r) ? r : null;

    public void Reset()
    {
        Units.Clear(); Contacts.Clear(); rows.Clear(); byEntity.Clear(); hits.Clear(); contactRows.Clear();
        InMission = false; nextRefresh = 0;
    }

    /// <summary>Refreshes when due (every <paramref name="interval"/> s of real time). True when a new snapshot was made.</summary>
    public bool Tick(double realNow, Limits limits, double interval, bool followGameTime)
    {
        if (realNow < nextRefresh)
            return false;
        // Following the game: a new snapshot only when the game's clock moved (a paused game shows nothing new, so it falls back to
        // the slow rate that still catches selection changes). The snapshot is then at most one short step behind the world.
        if (followGameTime && InMission && GameTime.missionSessionTime == Time && realNow < lastRefreshReal + 1.0)
            return false;
        lastRefreshReal = realNow;
        nextRefresh = realNow + interval;
        try { Refresh(limits); }
        catch (Exception ex) { ModLog.Error("sensor picture: " + ex); return false; }
        return true;
    }

    private void Refresh(Limits limits)
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
        ReadOwnUnits(player);
        EntityManager em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        var vehicles = table.Vehicles;
        for (int i = 0; i < vehicles.Count; i++)
        {
            Vehicle v = vehicles[i];
            if (v != null && v.BaseObject != null && v.BaseObject._taskforce != player)
                ReadTracks(em, v, limits.VisualSeconds);
        }
        GroupByContact(limits.MemorySeconds);
        ListContacts(vehicles);
    }

    private void ReadOwnUnits(Taskforce player)
    {
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
    }

    private void ReadTracks(EntityManager em, Vehicle v, float visualCap)
    {
        foreach (Entity e in v.CurrentTracks())
        {
            if (!em.HasComponent<TrackComponent>(e)) continue;
            TrackComponent tc = em.GetComponentData<TrackComponent>(e);
            if (!KindInfo.TryMap(tc.SensorType, out Kind kind)) continue;
            if (!byEntity.TryGetValue(tc.Source, out UnitRow unitRow)) continue;
            if (!em.HasComponent<TrackBearing>(e)) continue;
            double fix = tc.Epoch.ToLive();
            // A visual track lives as long as the game keeps it (its MaxAge); a contact out of sight is not held that long.
            if (kind == Kind.Visual && visualCap > 0f && Time - fix > visualCap) continue;
            Hit hit = HitFor(unitRow, v.Id, kind, fix);
            if (hit != null)
                ReadTrack(em, e, tc, hit, fix);
        }
    }

    /// <summary>The hit this track goes into; null when the unit's sensor of this kind already gave a newer track this refresh.</summary>
    private Hit HitFor(UnitRow unitRow, int contactId, Kind kind, double fix)
    {
        long key = ((long)unitRow.Uid << 32) ^ ((long)contactId << 4) ^ (long)kind;
        if (!hits.TryGetValue(key, out Hit hit))
            hits[key] = hit = new Hit { ContactId = contactId, Kind = kind };
        else if (hit.Stamp == stamp && hit.FixTime >= fix)
            return null;
        hit.Unit = unitRow;
        return hit;
    }

    private void ReadTrack(EntityManager em, Entity e, TrackComponent tc, Hit hit, double fix)
    {
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
        hit.BearingError = hit.Kind == Kind.Esm ? b.Error : Mathf.Sqrt(Mathf.Max(0f, b.Error));
        ReadRangeAndHeight(em, e, hit);
        hit.Signal = em.HasComponent<IncomingSignalData>(e) ? (float)em.GetComponentData<IncomingSignalData>(e)._incomingSignalValue : float.NaN;
    }

    private static void ReadRangeAndHeight(EntityManager em, Entity e, Hit hit)
    {
        // Passive sonar is bearing-only here. Its track does carry a range component, but that holds the true distance
        // (with a large error, for the game's own analysis) or nothing at all: it is not something the sonar measured.
        hit.BearingOnly = hit.Kind == Kind.Passive || !em.HasComponent<TrackRangeInMeters>(e);
        PointEstimate range = hit.BearingOnly ? default : em.GetComponentData<TrackRangeInMeters>(e).Estimate;
        hit.RangeM = hit.BearingOnly ? float.NaN : range.Estimate;
        hit.RangeErrorM = hit.BearingOnly ? 0f : Mathf.Sqrt(Mathf.Max(0f, range.Error));
        bool hasAltitude = em.HasComponent<TrackAltitude>(e);
        PointEstimate altitude = hasAltitude ? em.GetComponentData<TrackAltitude>(e).Estimate : default;
        hit.AltitudeUnits = hasAltitude ? altitude.Estimate : float.NaN;
        hit.AltitudeError = hasAltitude ? altitude.Error : 0f;
    }

    /// <summary>Lost tracks stay as remembered hits for the memory time (and while their unit is there); the rest go.</summary>
    private void GroupByContact(float memory)
    {
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
    }

    private void ListContacts(IList<Vehicle> vehicles)
    {
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
        catch (Exception) { /* a contact whose object is being removed has no class to read: the plain label stays */ }
        if (cr.Label == null || !string.Equals(cls, cr.LabelClass))
        {
            cr.LabelClass = cls;
            cr.Label = string.IsNullOrEmpty(cls) ? "T" + v.Id : "T" + v.Id + " " + cls;
        }
        ReadPlot(cr, v);
        cr.LiveRanged = false;
        foreach (Hit h in cr.Hits)
            if (h.Live && h.Ranged) { cr.LiveRanged = true; break; }
        cr.Relation = KindInfo.Of(v.CurrentRelationship());
    }

    /// <summary>
    /// The plotted position: what the map's marker shows (the track's estimate, extrapolated between fixes). Lines and scope marks
    /// that end here sit on the marker; one built from a sensor's own bearing and range lagged behind it.
    /// </summary>
    private static void ReadPlot(ContactRow cr, Vehicle v)
    {
        try
        {
            var est = v.PositionEstimate;
            cr.HasPlot = est.HasValue;
            if (!est.HasValue) return;
            // Position is the property the map's marker follows (set when the vehicle is refreshed); the estimate above is the
            // live extrapolation, which runs ahead of the marker between refreshes. A Position not set yet reads (0, 0): then the
            // estimate is all there is.
            GeoPosition shown = v.Position.Value;
            cr.Plot = shown._latitude == 0.0 && shown._longitude == 0.0 ? est.Value.Item1 : shown;
        }
        catch (Exception) { cr.HasPlot = false; }   // the estimate throws for a vehicle without a track yet: nothing plotted
    }

    private static void ReadUnit(UnitRow row)
    {
        ObjectBase u = row.Unit;
        row.Name = u.getName();
        row.Emcon = u.Emcon.Value;
        row.Heading = u.getHeading();
        row.SpeedKts = Mathf.Abs(u._velocityInKnots);
        float y = u.transform.position.y;
        row.DepthFt = u._type == ObjectBase.ObjectType.Submarine && y < 0f ? -y * Conv.FeetPerUnit : float.NaN;
        row.AltitudeFt = u.IsAirUnit ? y * Conv.FeetPerUnit : float.NaN;
        row.Kind ??= u._type.ToString();
        var systems = u._obp?._sensorSystems;
        if (systems == null) { row.Sensors.Clear(); row.SensorHeightUnits = y; return; }
        // The rows are made once per sensor (names, arcs, details are fixed); a refresh only reads the switches. Made again when the
        // unit's sensor list changes.
        if (!SameSensors(row, systems))
        {
            row.Sensors.Clear();
            foreach (SensorSystem s in systems)
                if (s != null) row.Sensors.Add(MakeRow(s));
        }
        float height = y;
        foreach (SensorRow sr in row.Sensors)
            height = Mathf.Max(height, ReadSwitches(sr));
        row.SensorHeightUnits = height;
    }

    private static bool SameSensors(UnitRow row, IEnumerable<SensorSystem> systems)
    {
        int k = 0;
        foreach (SensorSystem s in systems)
        {
            if (s == null) continue;
            if (k >= row.Sensors.Count || !ReferenceEquals(row.Sensors[k].System, s)) return false;
            k++;
        }
        return k == row.Sensors.Count;
    }

    /// <summary>Reads the sensor's switches; returns the height of a radar's antenna (else minus infinity).</summary>
    private static float ReadSwitches(SensorRow sr)
    {
        SensorSystem s = sr.System;
        sr.On = s.IsOn.Value;
        sr.Inoperable = s.Inoperable.Value;
        switch (s)
        {
            case SensorSystemRadar r:
                sr.Active = sr.On;
                return r.SensorPosition.y;
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
        return float.NegativeInfinity;
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
                sr.SearchRadar = r._type != SensorSystemRadar.RadarType.Targeting;
                sr.Letter = sr.SearchRadar ? "R" : "F";
                sr.Detail = r._type + (r._radar?._rp != null ? ", max " + (r._radar._rp._maxRange * Conv.NmPerUnit).ToString("0") + " nm" : "");
                break;
            case SensorSystemESM: sr.Letter = "E"; sr.Detail = "passive"; break;
            case SensorSystemSonar: sr.Letter = "S"; break;                     // letter and detail follow the mode in ReadSwitches
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
                if (now - row.Strobes[i].Time > Strobe.Lifetime) { row.Strobes.RemoveAt(i); changed = true; }
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
