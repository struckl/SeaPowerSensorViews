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
    private const float KmPerMeter = 0.001f;

    private static readonly List<LineSet.Line> Fresh = new List<LineSet.Line>();
    private static readonly Comparison<LineSet.Line> ByKey = (a, b) => a.Key.CompareTo(b.Key);

    /// <summary>The tolerance the current lines were last compared with (degrees); see <see cref="Build"/>.</summary>
    public static double LastTolerance = double.MaxValue;

    /// <summary>
    /// Builds the lines; the set's version (which makes every map rebuild its paths) only moves when a line came, went, changed its
    /// style or label, or moved by more than <paramref name="tolerance"/> degrees (about one screen pixel at the map's zoom, from
    /// <see cref="MapSet"/>). The track estimates wander by a few dozen metres even in a paused game; a fixed grid rebuilt every
    /// map path once a second for that. Compared against the lines last built, so slow drift is drawn once it adds up to a pixel.
    /// </summary>
    public static void Build(SensorPicture pic, UnitRow selected, LineSet set, double tolerance)
    {
        Fresh.Clear();
        Fill(pic, selected, Fresh);
        Fresh.Sort(ByKey);
        LastTolerance = tolerance;
        int changed = Changed(set.Lines, Fresh, tolerance, out string first);
        if (changed == 0 && set.Version > 0) return;
        ModLog.Debug($"lines rebuilt: {Fresh.Count} lines (were {set.Lines.Count}), {changed} new, changed or moved; first: {first ?? "-"}");
        set.Lines.Clear();
        set.Lines.AddRange(Fresh);
        set.Version++;
    }

    /// <summary>How many lines differ (both lists sorted by key); 0 = nothing worth drawing again.</summary>
    private static int Changed(List<LineSet.Line> old, List<LineSet.Line> now, double tol, out string first)
    {
        first = null;
        int changed = 0, i = 0, j = 0;
        while (i < old.Count || j < now.Count)
        {
            LineSet.Line a = i < old.Count ? old[i] : null, b = j < now.Count ? now[j] : null;
            if (a != null && b != null && a.Key == b.Key)
            {
                if (!Same(a, b, tol)) { changed++; first ??= Describe(b); }
                i++; j++;
            }
            else if (b == null || (a != null && a.Key < b.Key)) { changed++; first ??= "gone " + Describe(a); i++; }
            else { changed++; first ??= "new " + Describe(b); j++; }
        }
        return changed;
    }

    private static bool Same(LineSet.Line a, LineSet.Line b, double tol)
    {
        if (a.Style != b.Style || a.Bright != b.Bright || a.Live != b.Live || a.Fill != b.Fill || a.Label != b.Label
            || a.Lat.Length != b.Lat.Length)
            return false;
        for (int k = 0; k < a.Lat.Length; k++)
            if (Math.Abs(a.Lat[k] - b.Lat[k]) > tol || Math.Abs(a.Lon[k] - b.Lon[k]) > tol)
                return false;
        return true;
    }

    private static string Describe(LineSet.Line l) =>
        $"{l.Style} live={l.Live} fill={l.Fill} '{l.Label}' end {l.Lat[l.Lat.Length - 1]:F4},{l.Lon[l.Lon.Length - 1]:F4}";

    private static ulong KeyOf(UnitRow unit, ContactRow cr, Hit h, int part)
    {
        ulong k = 14695981039346656037UL;
        void Mix(long v) { k = (k ^ (ulong)v) * 1099511628211UL; }
        Mix(unit.Uid);
        Mix(cr.Id);
        Mix((int)h.Kind);
        Mix(h.SensorName?.GetHashCode() ?? 0);
        Mix(part);
        return k;
    }

    /// <summary>A line along a bearing from <paramref name="fromKm"/> to <paramref name="toKm"/> out of the origin, a chord every ~25 nm.</summary>
    private static LineSet.Line MakeLine(GeoPosition origin, float bearing, float fromKm, float toKm, ulong key, LineStyle style, bool bright, bool live, string label)
    {
        int steps = Math.Max(1, Math.Min(10, (int)Math.Ceiling((toKm - fromKm) / 46f)));
        var lat = new double[steps + 1];
        var lon = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float d = fromKm + (toKm - fromKm) * i / steps;
            GeoPosition p = d <= 0f ? origin : origin.OffsetByDistanceOnHeadingUnity(d, bearing);
            lat[i] = p._latitude; lon[i] = p._longitude;
        }
        return new LineSet.Line { Key = key, Style = style, Bright = bright, Live = live, Lat = lat, Lon = lon, Label = label };
    }
    private static bool KindShown(Kind k)
    {
        switch (k)
        {
            case Kind.Radar:
            case Kind.FireControl: return Settings.LinesRadar.Value;
            case Kind.Esm: return Settings.LinesEsm.Value;
            case Kind.Visual: return Settings.LinesVisual.Value;
            case Kind.Passive:
            case Kind.Active: return Settings.LinesSonar.Value;
            default: return true;
        }
    }

    private static void Fill(SensorPicture pic, UnitRow selected, List<LineSet.Line> lines)
    {
        bool onlySelected = Settings.LinesWho.Value == Settings.SelectedOnly && !Settings.LinesAllUnits.Value;
        if (onlySelected && selected == null) return;              // nothing selected: no lines
        bool lost = Settings.LostLines.Value;
        float wedgeKm = Settings.WedgeNm.Value * 1.852f;
        foreach (ContactRow cr in pic.Contacts)
        {
            // an ESM bearing of a contact that a ranging sensor holds live adds nothing: the game already linked them
            bool ranged = false;
            if (Settings.HideTrackedEsm.Value)
                foreach (Hit r in cr.Hits)
                    if (r.Live && !r.BearingOnly && !float.IsNaN(r.RangeM)) { ranged = true; break; }
            foreach (Hit h in cr.Hits)
            {
                if (h.Unit?.Unit == null || h.Unit.Unit.IsDestroyed) continue;
                if (!KindShown(h.Kind)) continue;
                if (ranged && h.Kind == Kind.Esm) continue;
                bool bright = selected == null || ReferenceEquals(h.Unit, selected);
                if (onlySelected && !bright) continue;
                if (!h.Live && !lost) continue;
                GeoPosition origin = h.Unit.Unit.getGeoPosition();
                LineStyle style = LineStyles.Of(h.Kind);
                if (!h.BearingOnly && !float.IsNaN(h.RangeM) && h.RangeM > 1f)
                {
                    float km = h.RangeM * KmPerMeter;
                    float bearing = h.Bearing;
                    if (cr.HasPlot)   // end on the map marker, not on this sensor's older estimate
                    {
                        GeoMath.BearingRange(origin, cr.Plot, out float pb, out float pnm);
                        if (pnm > 0.01f) { km = pnm * 1.852f; bearing = pb; }
                    }
                    // The range is an estimate with an error. The line is solid up to the nearest range it could be, and a faint stretch
                    // runs on to the farthest: a line ending at one exact point would claim a precision the sensor does not have (sonar).
                    float errKm = h.RangeErrorM * KmPerMeter;
                    bool uncertain = errKm > 0.1f && errKm > 0.03f * km;
                    float solidKm = uncertain ? Math.Max(0.05f * km, km - errKm) : km;
                    string label = h.Kind == Kind.FireControl ? cr.Label + " LOCK" : cr.Label;
                    lines.Add(MakeLine(origin, bearing, 0f, solidKm, KeyOf(h.Unit, cr, h, 0), style, bright, h.Live, label));
                    if (uncertain)
                        lines.Add(MakeLine(origin, bearing, solidKm, km + errKm, KeyOf(h.Unit, cr, h, 3), style, false, h.Live, null));                }
                else
                {
                    float half = Math.Max(1f, Math.Min(15f, h.BearingError));
                    // With a plotted position (the map marker) the line and cone run to the marker's distance and a little past it, so
                    // the marker sits inside the cone when this bearing agrees with it; without one they have the configured length.
                    float lenKm = wedgeKm;
                    if (cr.HasPlot)
                    {
                        GeoMath.BearingRange(origin, cr.Plot, out float _, out float pnm);
                        lenKm = Math.Max(1.852f, Math.Min(300f * 1.852f, pnm * 1.852f * 1.1f));
                    }
                    float bearingNow = GeoMath.BearingNow(cr, h, origin);
                    GeoPosition a = origin.OffsetByDistanceOnHeadingUnity(lenKm, bearingNow - half);
                    GeoPosition m = origin.OffsetByDistanceOnHeadingUnity(lenKm, bearingNow);
                    GeoPosition b = origin.OffsetByDistanceOnHeadingUnity(lenKm, bearingNow + half);
                    // the wedge, filled
                    lines.Add(new LineSet.Line
                    {
                        Key = KeyOf(h.Unit, cr, h, 1), Style = style, Bright = bright, Live = h.Live, Fill = true,
                        Lat = new[] { origin._latitude, a._latitude, m._latitude, b._latitude },
                        Lon = new[] { origin._longitude, a._longitude, m._longitude, b._longitude },
                    });
                    // the bearing line down its middle, labelled at the far end
                    lines.Add(new LineSet.Line
                    {
                        Key = KeyOf(h.Unit, cr, h, 2), Style = style, Bright = bright, Live = h.Live,
                        Lat = new[] { origin._latitude, m._latitude },
                        Lon = new[] { origin._longitude, m._longitude },
                        Label = (h.Kind == Kind.Esm ? "ESM " : "S ") + cr.Label,
                    });
                }
            }
        }
    }
}
