using System;
using System.Collections.Generic;
using Noesis;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Map;

internal enum CoverageKind { Radar, Esm, Visual, Sonar }

/// <summary>How the map draws a line of each sensor kind and each coverage ring.</summary>
internal static class LineStyles
{
    public const string Dashed = "6 3", Dotted = "2 3";

    /// <summary>Weight relative to the game's base line: a lock is drawn three times as thick, like the game's weapon vectors stand out.</summary>
    public static float Weight(Kind k) => k == Kind.FireControl ? 3f : k == Kind.Radar ? 1.2f : 1f;

    public static string Dashes(Kind k) => k switch
    {
        Kind.Esm or Kind.Passive => Dashed,
        Kind.Visual => Dotted,
        _ => null,
    };

    public static string Dashes(CoverageKind c) => c switch
    {
        CoverageKind.Esm => Dashed,
        CoverageKind.Visual => Dotted,
        _ => null,
    };

    public static string ColourKey(Kind k) => k switch
    {
        Kind.Radar => GameColours.Unknown,             // amber
        Kind.FireControl => GameColours.MapRwr,        // the red of the game's radar warning vectors
        Kind.Esm => GameColours.Friend,
        Kind.Active => GameColours.MapUnitRange,
        Kind.Visual => GameColours.Normal,
        _ => GameColours.Neutral,
    };

    public static string ColourKey(CoverageKind c) => c switch
    {
        CoverageKind.Radar => GameColours.Unknown,
        CoverageKind.Esm => GameColours.Friend,
        CoverageKind.Visual => GameColours.Normal,
        _ => GameColours.Neutral,
    };
}

/// <summary>Which map path a line or ring goes into: all figures of one look share a path.</summary>
internal readonly struct PathKey : IEquatable<PathKey>
{
    public readonly bool Coverage, Bright, Live, Fill;
    public readonly int Style;          // a Kind for a line, a CoverageKind for a ring
    public readonly int Group;          // the line's group (see LineBuilder.GroupOf); -1 for coverage

    private PathKey(bool coverage, int style, bool bright, bool live, bool fill, int group)
    {
        Coverage = coverage; Style = style; Bright = bright; Live = live; Fill = fill; Group = group;
    }

    public static PathKey Of(LineSet.Line l) => new PathKey(false, (int)l.Kind, l.Bright, l.Live, l.Fill, l.Group);

    public static PathKey Of(CoverageSet.Ring r) => new PathKey(true, (int)r.Kind, true, true, r.Fill, -1);

    public bool Equals(PathKey o) =>
        Coverage == o.Coverage && Style == o.Style && Bright == o.Bright && Live == o.Live && Fill == o.Fill && Group == o.Group;

    public override bool Equals(object o) => o is PathKey k && Equals(k);

    public override int GetHashCode() =>
        (Style << 8) ^ (Group << 4) ^ (Coverage ? 1 : 0) ^ (Bright ? 2 : 0) ^ (Live ? 4 : 0) ^ (Fill ? 8 : 0);
}

/// <summary>The game's colours as one map control sees them, cached per key.</summary>
internal sealed class Theme
{
    private readonly Dictionary<string, Brush> cache = new Dictionary<string, Brush>();
    private readonly FrameworkElement map;

    public Theme(FrameworkElement map) => this.map = map;

    public Brush Of(string key)
    {
        if (!cache.TryGetValue(key, out Brush b))
            cache[key] = b = GameColours.Find(map, key);
        return b;
    }
}

/// <summary>Cached lines (in longitude/latitude) of the sensor lines layer, rebuilt from the snapshot (see LineBuilder).</summary>
internal sealed class LineSet
{
    public sealed class Line
    {
        public ulong Key;               // unit, contact, sensor and part: the same line across snapshots
        public Kind Kind;
        public bool Bright, Live, Fill;
        public double[] Lat, Lon;
        public string Label;
        /// <summary>Which map paths the line goes into (see <see cref="LineBuilder.GroupOf"/>): only groups that changed are drawn again.</summary>
        public int Group;
    }

    public readonly List<Line> Lines = new List<Line>();
    public int Version;
    /// <summary>The groups whose lines changed from Version - 1 to Version (a map one version behind redraws only these).</summary>
    public readonly HashSet<int> ChangedGroups = new HashSet<int>();
    /// <summary>The tolerance (degrees) the lines were last compared with; MaxValue before the first build.</summary>
    public double Tolerance = double.MaxValue;

    public void Clear()
    {
        Lines.Clear();
        Tolerance = double.MaxValue;
        Version++;
    }

    public void Replace(List<Line> lines, HashSet<int> changedGroups)
    {
        Lines.Clear();
        Lines.AddRange(lines);
        ChangedGroups.Clear();
        ChangedGroups.UnionWith(changedGroups);
        Version++;
    }
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
    // The rings are built around Origin; the maps shift them each frame by how far Unit has moved since, so they stay on the unit
    // between rebuilds (a new shape comes only about every nm).
    public SeaPower.ObjectBase Unit;
    public SeaPower.GeoPosition Origin;
}
