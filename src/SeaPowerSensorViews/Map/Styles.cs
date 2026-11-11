using System;
using System.Collections.Generic;
using Noesis;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Map;

internal enum LineStyle { Radar, FireControl, Esm, Passive, Active, Visual, Mad }

internal enum CoverageKind { Radar, Esm, Visual, Sonar }

internal static class LineStyles
{
    public static LineStyle Of(Kind k)
    {
        switch (k)
        {
            case Kind.Radar: return LineStyle.Radar;
            case Kind.FireControl: return LineStyle.FireControl;
            case Kind.Esm: return LineStyle.Esm;
            case Kind.Passive: return LineStyle.Passive;
            case Kind.Active: return LineStyle.Active;
            case Kind.Visual: return LineStyle.Visual;
            default: return LineStyle.Mad;
        }
    }

    public static string Key(LineStyle s, bool bright, bool live, bool fill) =>
        "l|" + (int)s + (bright ? "b" : "d") + (live ? "l" : "x") + (fill ? "f" : "s");

    public static void Parse(string key, out LineStyle style, out bool bright, out bool live, out bool fill)
    {
        style = (LineStyle)(key[2] - '0');
        bright = key[3] == 'b';
        live = key[4] == 'l';
        fill = key[5] == 'f';
    }

    /// <summary>Weight relative to the game's base line: a lock is drawn three times as thick, like the game's weapon vectors stand out.</summary>
    public static float Weight(LineStyle s) => s == LineStyle.FireControl ? 3f : s == LineStyle.Radar ? 1.2f : 1f;

    public static string Dashes(LineStyle s)
    {
        switch (s)
        {
            case LineStyle.Esm: return "6 3";
            case LineStyle.Passive: return "6 3";
            case LineStyle.Visual: return "2 3";
            default: return null;
        }
    }
}

/// <summary>Colours from the game: the NTDS brushes of the application resources and the map's own resource keys, looked up from the map control.</summary>
internal sealed class Theme
{
    private readonly Dictionary<int, Brush> cache = new Dictionary<int, Brush>();
    private readonly FrameworkElement map;

    public Theme(FrameworkElement map) => this.map = map;

    private Brush Find(string key, uint fallback)
    {
        try { if (map.TryFindResource(key) is Brush b) return b; }
        catch (Exception) { /* fallback */ }
        return new SolidColorBrush(Color.FromArgb((byte)(fallback >> 24), (byte)(fallback >> 16), (byte)(fallback >> 8), (byte)fallback));
    }

    public Brush Line(LineStyle s)
    {
        int k = (int)s;
        if (cache.TryGetValue(k, out Brush b)) return b;
        switch (s)
        {
            case LineStyle.Radar: b = Find("NTDS.UnknownColour", 0xFFFFC000); break;           // amber
            case LineStyle.FireControl: b = Find("Map_RWR", 0xFFFF0000); break;               // the red of the game's radar warning vectors
            case LineStyle.Esm: b = Find("NTDS.FriendColour", 0xFF2060E0); break;
            case LineStyle.Passive: b = Find("NTDS.NeutralColour", 0xFF2FA84F); break;
            case LineStyle.Active: b = Find("Map_UnitOnMapRange", 0xFF3B7FE0); break;
            case LineStyle.Visual: b = Find("Brush.Foreground.Normal", 0xFF949AA1); break;
            default: b = Find("NTDS.NeutralColour", 0xFF2FA84F); break;
        }
        return cache[k] = b;
    }

    private readonly Dictionary<(Brush, int), Brush> alphas = new Dictionary<(Brush, int), Brush>();

    /// <summary>The brush with an opacity of its own (cached per brush and 1/64 step).</summary>
    public Brush Alpha(Brush brush, float alpha)
    {
        int step = (int)Math.Round(Math.Max(0f, Math.Min(1f, alpha)) * 64f);
        if (alphas.TryGetValue((brush, step), out Brush b)) return b;
        Color c = (brush as SolidColorBrush)?.Color ?? Color.FromArgb(255, 200, 200, 200);
        return alphas[(brush, step)] = new SolidColorBrush(Color.FromArgb((byte)(c.A * step / 64), c.R, c.G, c.B));
    }

    public Brush Coverage(CoverageKind c)
    {
        int k = 100 + (int)c;
        if (cache.TryGetValue(k, out Brush b)) return b;
        switch (c)
        {
            case CoverageKind.Radar: b = Find("NTDS.UnknownColour", 0xFFFFC000); break;
            case CoverageKind.Esm: b = Find("NTDS.FriendColour", 0xFF2060E0); break;
            case CoverageKind.Visual: b = Find("Brush.Foreground.Normal", 0xFF949AA1); break;
            default: b = Find("NTDS.NeutralColour", 0xFF2FA84F); break;
        }
        return cache[k] = b;
    }
}

/// <summary>Cached lines (in longitude/latitude) of the sensor lines layer, rebuilt from the snapshot about once a second.</summary>
internal sealed class LineSet
{
    public sealed class Line
    {
        public ulong Key;               // unit, contact, sensor and part: the same line across snapshots
        public LineStyle Style;
        public bool Bright, Live, Fill;
        public double[] Lat, Lon;
        public string Label;
    }

    public readonly List<Line> Lines = new List<Line>();
    public int Version;
}

internal sealed class CoverageSet
{
    public sealed class Ring
    {
        public CoverageKind Kind;
        public bool Fill;
        public double[] Lat, Lon;
    }

    public readonly List<Ring> Rings = new List<Ring>();
    public int Version;
    public string Status = "";
    // The rings are built around Origin; the maps shift them each frame by how far Unit has moved since, so they stay on the unit
    // between rebuilds (a new shape comes only about every nm).
    public SeaPower.ObjectBase Unit;
    public SeaPower.GeoPosition Origin;
}
