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
    private static HashSet<ulong> lastHashes = new HashSet<ulong>(), freshHashes = new HashSet<ulong>();

    /// <summary>
    /// Builds the lines; the set's version (which makes every map rebuild its paths) only moves when they differ from the last ones
    /// by more than about 0.1 nm or in style or label - a paused game, or contacts that hold still, cost nothing on the map.
    /// </summary>
    public static void Build(SensorPicture pic, UnitRow selected, LineSet set)
    {
        Fresh.Clear();
        Fill(pic, selected, Fresh);
        ulong sig = Signature(Fresh);
        if (sig == set.Signature && set.Version > 0) return;
        if (Settings.VerboseLog.Value) Explain(set);
        set.Signature = sig;
        set.Lines.Clear();
        set.Lines.AddRange(Fresh);
        set.Version++;
    }

    /// <summary>
    /// The lines' fingerprint, independent of their order (the snapshot does not list the contacts in a fixed order, and an
    /// order-bound fingerprint rebuilt every map path once a second even in a paused game).
    /// </summary>
    private static ulong Signature(List<LineSet.Line> lines)
    {
        freshHashes.Clear();
        ulong sum = (ulong)lines.Count * 0x9E3779B97F4A7C15UL;
        foreach (LineSet.Line l in lines)
        {
            ulong h = LineHash(l);
            freshHashes.Add(h);
            sum += h;
        }
        return sum;
    }

    private static ulong LineHash(LineSet.Line l)
    {
        ulong h = 14695981039346656037UL;
        void Mix(long v) { h = (h ^ (ulong)v) * 1099511628211UL; }
        Mix(((int)l.Style << 3) | (l.Bright ? 4 : 0) | (l.Live ? 2 : 0) | (l.Fill ? 1 : 0));
        Mix(l.Label?.GetHashCode() ?? 0);
        Mix(l.Lat.Length);
        for (int i = 0; i < l.Lat.Length; i++)
        {
            Mix((long)Math.Round(l.Lat[i] * 500.0));        // 0.002 degree, about 0.1 nm
            Mix((long)Math.Round(l.Lon[i] * 500.0));
        }
        h ^= h >> 31;
        return h * 0xBF58476D1CE4E5B9UL;
    }

    /// <summary>Verbose log: what made the map paths rebuild (new or changed lines, gone lines).</summary>
    private static void Explain(LineSet set)
    {
        int added = 0, kept = 0;
        string first = null;
        foreach (LineSet.Line l in Fresh)
        {
            if (lastHashes.Contains(LineHash(l))) { kept++; continue; }
            added++;
            first ??= $"{l.Style} live={l.Live} fill={l.Fill} '{l.Label}' end {l.Lat[l.Lat.Length - 1]:F4},{l.Lon[l.Lon.Length - 1]:F4}";
        }
        ModLog.Debug($"lines rebuilt: {Fresh.Count} lines, {added} new or changed, {lastHashes.Count - kept} gone; first new: {first ?? "-"}");
        (lastHashes, freshHashes) = (freshHashes, lastHashes);
    }

    private static void Fill(SensorPicture pic, UnitRow selected, List<LineSet.Line> lines)
    {
        bool onlySelected = Settings.LinesWho.Value == "Selected only";
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
                    lines.Add(new LineSet.Line { Style = style, Bright = bright, Live = h.Live, Lat = lat, Lon = lon, Label = label });
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
                        Style = style, Bright = bright, Live = h.Live, Fill = true,
                        Lat = new[] { origin._latitude, a._latitude, m._latitude, b._latitude },
                        Lon = new[] { origin._longitude, a._longitude, m._longitude, b._longitude },
                    });
                    // the bearing line down its middle, labelled at the far end
                    lines.Add(new LineSet.Line
                    {
                        Style = style, Bright = bright, Live = h.Live,
                        Lat = new[] { origin._latitude, m._latitude },
                        Lon = new[] { origin._longitude, m._longitude },
                        Label = (h.Kind == Kind.Esm ? "ESM " : "S ") + cr.Label,
                    });
                }
            }
        }
    }
}
