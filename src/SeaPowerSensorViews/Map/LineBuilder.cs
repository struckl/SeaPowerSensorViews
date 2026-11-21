using System;
using System.Collections.Generic;
using SeaPower;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// Turns the sensor snapshot into map lines: from each own unit to the place each of its sensors puts a contact (bearing and range
/// of that sensor's track estimate - never the contact's real position), and bearing lines with a wedge for sensors that only know
/// a bearing (ESM, passive sonar). A fire-control lock is drawn thick and labelled. The selected unit's lines are bright, the others
/// dimmed (or left out, by the setting).
/// </summary>
internal static class LineBuilder
{
    /// <summary>What goes into the lines (from the settings, made by MapSet).</summary>
    public struct Options
    {
        public bool OnlySelected, Lost, HideTrackedEsm;
        public bool Radar, Esm, Visual, Sonar;
        public float WedgeKm;           // bearing line length while the contact has no plotted position
    }

    private static readonly List<LineSet.Line> Fresh = new List<LineSet.Line>();
    private static readonly List<LineSet.Line> Merged = new List<LineSet.Line>();
    private static readonly HashSet<int> Groups = new HashSet<int>();
    private static readonly Comparison<LineSet.Line> ByKey = (a, b) => a.Key.CompareTo(b.Key);

    /// <summary>Groups of other units' lines; the selected unit's lines have a group of their own.</summary>
    public const int SharedGroups = 6, SelectedGroup = SharedGroups;

    /// <summary>
    /// The map paths a line goes into. Noesis draws a changed path again whole (every dash of it), so with one path per style every
    /// moving unit redrew every line on the map up to 10 times a second (FPS 10-40 with ~20 units, board post #2). The selected
    /// unit's lines get a group of their own, the other units are spread over a few groups: a move redraws only its group.
    /// </summary>
    public static int GroupOf(UnitRow unit, bool selectedUnit) => selectedUnit ? SelectedGroup : (int)((uint)unit.Uid % SharedGroups);

    /// <summary>
    /// Builds the lines; the set's version (which makes every map draw the changed groups again) only moves when a line came, went,
    /// changed its style or label, or moved by more than <paramref name="tolerance"/> degrees (about one screen pixel at the map's
    /// zoom, from <see cref="MapSet"/>). The track estimates wander by a few dozen metres even in a paused game; a fixed grid rebuilt
    /// every map path once a second for that. Compared against the lines last built, so slow drift is drawn once it adds up to a pixel.
    /// Lines that only moved and are not the selected unit's keep their old place until their group's turn: one shared group per
    /// build (<paramref name="dueGroup"/>, round robin), so each follows about twice a second and no build redraws them all at
    /// once (a VM test 2026-11-19 saw 4 of 4 groups in 40 % of the builds when they all came due together). The selected unit's
    /// lines follow every snapshot. <paramref name="all"/> (settings, selection, zoom) takes every change at once.
    /// </summary>
    public static void Build(SensorPicture pic, UnitRow selected, in Options options, LineSet set, double tolerance, bool all, int dueGroup)
    {
        Fresh.Clear();
        Fill(pic, selected, options, Fresh);
        Fresh.Sort(ByKey);
        set.Tolerance = tolerance;
        Merged.Clear();
        Groups.Clear();
        int changed = Merge(set.Lines, Fresh, tolerance, all, dueGroup, out string first);
        if (changed == 0 && set.Version > 0) return;
        Rig.Log.Debug($"lines rebuilt: {Merged.Count} lines (were {set.Lines.Count}), {changed} new, changed or moved in {Groups.Count} group(s); first: {first ?? "-"}");
        set.Replace(Merged, Groups);
    }

    /// <summary>Merges the new lines into <see cref="Merged"/> (both lists sorted by key); returns how many differ, 0 = nothing to draw.</summary>
    private static int Merge(List<LineSet.Line> old, List<LineSet.Line> now, double tol, bool all, int dueGroup, out string first)
    {
        first = null;
        int changed = 0, i = 0, j = 0;
        while (i < old.Count || j < now.Count)
        {
            LineSet.Line a = i < old.Count ? old[i] : null, b = j < now.Count ? now[j] : null;
            if (a != null && b != null && a.Key == b.Key)
            {
                if (!KeepOld(a, b, tol, all, dueGroup)) { changed++; first ??= Describe(b); }
                i++; j++;
            }
            else if (b == null || (a != null && a.Key < b.Key)) { changed++; first ??= "gone " + Describe(a); Groups.Add(a.Group); i++; }
            else { Merged.Add(b); changed++; first ??= "new " + Describe(b); Groups.Add(b.Group); j++; }
        }
        return changed;
    }

    /// <summary>The same line in both builds: keeps the old one when it did not move, or only moved and waits for its group's turn.</summary>
    private static bool KeepOld(LineSet.Line a, LineSet.Line b, double tol, bool all, int dueGroup)
    {
        bool waits = !all && a.Group != SelectedGroup && a.Group != dueGroup && b.Group == a.Group && SameShape(a, b);
        if (waits || Same(a, b, tol))
        {
            Merged.Add(a);
            return true;
        }
        Merged.Add(b);
        Groups.Add(a.Group);
        Groups.Add(b.Group);
        return false;
    }

    private static bool SameShape(LineSet.Line a, LineSet.Line b) =>
        a.Kind == b.Kind && a.Bright == b.Bright && a.Live == b.Live && a.Fill == b.Fill && a.Label == b.Label && a.Lat.Length == b.Lat.Length;

    private static bool Same(LineSet.Line a, LineSet.Line b, double tol)
    {
        if (a.Group != b.Group || !SameShape(a, b))
            return false;
        for (int k = 0; k < a.Lat.Length; k++)
            if (Math.Abs(a.Lat[k] - b.Lat[k]) > tol || Math.Abs(a.Lon[k] - b.Lon[k]) > tol)
                return false;
        return true;
    }

    private static string Describe(LineSet.Line l) =>
        $"{l.Kind} live={l.Live} fill={l.Fill} '{l.Label}' end {l.Lat[l.Lat.Length - 1]:F4},{l.Lon[l.Lon.Length - 1]:F4}";

    private static ulong KeyOf(Hit h, ContactRow cr, int part)
    {
        ulong k = 14695981039346656037UL;
        void Mix(long v) { k = (k ^ (ulong)v) * 1099511628211UL; }
        Mix(h.Unit.Uid);
        Mix(cr.Id);
        Mix((int)h.Kind);
        Mix(h.SensorName?.GetHashCode() ?? 0);
        Mix(part);
        return k;
    }

    private static bool KindShown(Kind k, in Options o) => k switch
    {
        Kind.Radar or Kind.FireControl => o.Radar,
        Kind.Esm => o.Esm,
        Kind.Visual => o.Visual,
        Kind.Passive or Kind.Active => o.Sonar,
        _ => true,
    };

    /// <summary>The look all lines of one hit share.</summary>
    private struct Look
    {
        public GeoPosition Origin;
        public bool Bright, Live;
        public int Group;
    }

    /// <summary>Whether the hit gets lines; <paramref name="bright"/> for the selected unit's (all of them while none is selected).</summary>
    private static bool Drawn(Hit h, in Options o, bool hideEsm, UnitRow selected, out bool bright)
    {
        bright = selected == null || ReferenceEquals(h.Unit, selected);
        if (h.Unit?.Unit == null || h.Unit.Unit.IsDestroyed || !KindShown(h.Kind, o) || (hideEsm && h.Kind == Kind.Esm)) return false;
        return (bright || !o.OnlySelected) && (h.Live || o.Lost);
    }

    private static void Fill(SensorPicture pic, UnitRow selected, in Options o, List<LineSet.Line> lines)
    {
        if (o.OnlySelected && selected == null) return;              // nothing selected: no lines
        foreach (ContactRow cr in pic.Contacts)
        {
            // an ESM bearing of a contact that a ranging sensor holds live adds nothing: the game already linked them
            bool hideEsm = o.HideTrackedEsm && cr.LiveRanged;
            foreach (Hit h in cr.Hits)
            {
                if (!Drawn(h, o, hideEsm, selected, out bool bright)) continue;
                var look = new Look
                {
                    Origin = h.Unit.Unit.getGeoPosition(), Bright = bright, Live = h.Live,
                    Group = GroupOf(h.Unit, selected != null && ReferenceEquals(h.Unit, selected)),
                };
                if (h.Ranged) AddRanged(cr, h, look, lines);
                else AddBearing(cr, h, look, o.WedgeKm, lines);
            }
        }
    }

    /// <summary>A line to where the sensor (or the map marker) puts the contact.</summary>
    private static void AddRanged(ContactRow cr, Hit h, in Look look, List<LineSet.Line> lines)
    {
        float bearing = h.Bearing, nm = Conv.Nm(h.RangeM);
        GeoMath.ToPlot(cr, look.Origin, ref bearing, ref nm);
        float km = nm * Conv.KmPerNm;
        // The range is an estimate with an error. The line is solid up to the nearest range it could be, and a faint stretch
        // runs on to the farthest: a line ending at one exact point would claim a precision the sensor does not have (sonar).
        float errKm = h.RangeErrorM * Conv.KmPerMeter;
        bool uncertain = errKm > 0.1f && errKm > 0.03f * km;
        float solidKm = uncertain ? Math.Max(0.05f * km, km - errKm) : km;
        string label = h.Kind == Kind.FireControl ? cr.Label + " LOCK" : cr.Label;
        lines.Add(MakeLine(look, bearing, 0f, solidKm, KeyOf(h, cr, 0), h.Kind, look.Bright, label));
        if (uncertain)
            lines.Add(MakeLine(look, bearing, solidKm, km + errKm, KeyOf(h, cr, 3), h.Kind, false, null));
    }

    /// <summary>A bearing line with its wedge (the bearing error) for a sensor that has no range.</summary>
    private static void AddBearing(ContactRow cr, Hit h, in Look look, float wedgeKm, List<LineSet.Line> lines)
    {
        GeoPosition origin = look.Origin;
        float half = Math.Max(1f, Math.Min(15f, h.BearingError));
        // With a plotted position (the map marker) the line and cone run to the marker's distance and a little past it, so
        // the marker sits inside the cone when this bearing agrees with it; without one they have the configured length.
        float lenKm = wedgeKm;
        if (cr.HasPlot)
        {
            GeoMath.BearingRange(origin, cr.Plot, out float _, out float pnm);
            lenKm = Math.Max(Conv.KmPerNm, Math.Min(300f * Conv.KmPerNm, pnm * Conv.KmPerNm * 1.1f));
        }
        float bearingNow = GeoMath.BearingNow(cr, h, origin);
        GeoPosition a = origin.OffsetByDistanceOnHeadingUnity(lenKm, bearingNow - half);
        GeoPosition m = origin.OffsetByDistanceOnHeadingUnity(lenKm, bearingNow);
        GeoPosition b = origin.OffsetByDistanceOnHeadingUnity(lenKm, bearingNow + half);
        lines.Add(new LineSet.Line
        {
            Key = KeyOf(h, cr, 1), Kind = h.Kind, Bright = look.Bright, Live = look.Live, Fill = true, Group = look.Group,
            Lat = new[] { origin._latitude, a._latitude, m._latitude, b._latitude },
            Lon = new[] { origin._longitude, a._longitude, m._longitude, b._longitude },
        });
        // the bearing line down the wedge's middle, labelled at the far end
        lines.Add(new LineSet.Line
        {
            Key = KeyOf(h, cr, 2), Kind = h.Kind, Bright = look.Bright, Live = look.Live, Group = look.Group,
            Lat = new[] { origin._latitude, m._latitude },
            Lon = new[] { origin._longitude, m._longitude },
            Label = (h.Kind == Kind.Esm ? "ESM " : "S ") + cr.Label,
        });
    }

    /// <summary>A line along a bearing from <paramref name="fromKm"/> to <paramref name="toKm"/> out of the origin, a chord every ~25 nm.</summary>
    private static LineSet.Line MakeLine(in Look look, float bearing, float fromKm, float toKm, ulong key, Kind kind, bool bright, string label)
    {
        int steps = Math.Max(1, Math.Min(10, (int)Math.Ceiling((toKm - fromKm) / 46f)));
        var lat = new double[steps + 1];
        var lon = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float d = fromKm + (toKm - fromKm) * i / steps;
            GeoPosition p = d <= 0f ? look.Origin : look.Origin.OffsetByDistanceOnHeadingUnity(d, bearing);
            lat[i] = p._latitude; lon[i] = p._longitude;
        }
        return new LineSet.Line { Key = key, Kind = kind, Bright = bright, Live = look.Live, Lat = lat, Lon = lon, Label = label, Group = look.Group };
    }
}
