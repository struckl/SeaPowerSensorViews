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

    private static void Fill(SensorPicture pic, UnitRow selected, List<LineSet.Line> lines)
    {
        bool onlySelected = Settings.LinesWho.Value == Settings.SelectedOnly;
        if (onlySelected && selected == null) return;              // nothing selected: no lines
        bool lost = Settings.LostLines.Value;
        float wedgeKm = Settings.WedgeNm.Value * 1.852f;
        foreach (ContactRow cr in pic.Contacts)
        {
            foreach (Hit h in cr.Hits)
            {
                if (h.Unit?.Unit == null || h.Unit.Unit.IsDestroyed) continue;
                bool bright = selected == null || ReferenceEquals(h.Unit, selected);
                if (onlySelected && !bright) continue;
                if (!h.Live && !lost) continue;
                GeoPosition origin = h.Unit.Unit.getGeoPosition();
                LineStyle style = LineStyles.Of(h.Kind);
                if (!h.BearingOnly && !float.IsNaN(h.RangeM) && h.RangeM > 1f)
                {
                    float km = h.RangeM * KmPerMeter;
                    int steps = Math.Max(1, Math.Min(10, (int)Math.Ceiling(km / 46f)));   // a chord every ~25 nm follows the curve
                    var lat = new double[steps + 1];
                    var lon = new double[steps + 1];
                    lat[0] = origin._latitude; lon[0] = origin._longitude;
                    for (int i = 1; i <= steps; i++)
                    {
                        GeoPosition p = origin.OffsetByDistanceOnHeadingUnity(km * i / steps, h.Bearing);
                        lat[i] = p._latitude; lon[i] = p._longitude;
                    }
                    string label = h.Kind == Kind.FireControl ? cr.Label + " LOCK" : cr.Label;
                    lines.Add(new LineSet.Line { Key = KeyOf(h.Unit, cr, h, 0), Style = style, Bright = bright, Live = h.Live, Lat = lat, Lon = lon, Label = label });
                }
                else
                {
                    float half = Math.Max(1f, Math.Min(15f, h.BearingError));
                    GeoPosition a = origin.OffsetByDistanceOnHeadingUnity(wedgeKm, h.Bearing - half);
                    GeoPosition m = origin.OffsetByDistanceOnHeadingUnity(wedgeKm, h.Bearing);
                    GeoPosition b = origin.OffsetByDistanceOnHeadingUnity(wedgeKm, h.Bearing + half);
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
