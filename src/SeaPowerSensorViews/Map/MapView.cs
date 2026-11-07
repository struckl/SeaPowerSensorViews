using System;
using System.Collections.Generic;
using MapControl;
using Noesis;
using GeoPosition = SeaPower.GeoPosition;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// The two layers on one map control: two <see cref="MapPanel"/>s laid into the map (sensor lines, coverage), each holding a few
/// Noesis <see cref="Path"/>s in screen pixels and a canvas of labels. The geometry is kept as longitude/latitude and
/// projected through the map (<c>LocationToView</c> on the flat map, <c>TryLocationToView</c> on the globe, where far-side points
/// are left out) whenever the view or the data changes - the same screen-space method as the Borders mod uses on the globe,
/// used for both map kinds here because the lines run between arbitrary points, not along a lon/lat tile grid.
/// </summary>
internal sealed class MapView
{
    private const float BaseWeight = 1f;      // the game's own map line weight: TacticalMap_Geometry *Thickness defaults, 1 pixel

    public readonly MapBase Map;
    private readonly MapPanel linesPanel, coveragePanel;
    private readonly Canvas labels;
    private readonly Dictionary<string, Path> linePaths = new Dictionary<string, Path>();
    private readonly Dictionary<string, Path> coveragePaths = new Dictionary<string, Path>();
    private readonly Dictionary<string, (StreamGeometry geometry, StreamGeometryContext context)> open =
        new Dictionary<string, (StreamGeometry, StreamGeometryContext)>();
    private readonly List<TextBlock> labelPool = new List<TextBlock>();
    private readonly EventHandler<ViewportChangedEventArgs> onViewport;
    private readonly Theme theme;
    private int labelsUsed;
    private bool viewDirty = true;
    private double nextViewBuild;
    private int linesVersion = -1, coverageVersion = -1, settingsVersion = -1;
    private bool linesShown, coverageShown;
    private readonly Fit linesFit, coverageFit;
    // The area paths are cut to, in view pixels: the view plus half a view size on each side. Zoomed in, a sensor line or wedge runs
    // hundreds of thousands of pixels past the screen; uncut, Noesis tessellated all of it (every dash of a dotted line), which froze
    // the game when zoomed in on a contact.
    private float clipX0, clipY0, clipX1, clipY1;
    private readonly List<Point> run = new List<Point>();
    private readonly List<Point> polyA = new List<Point>(), polyB = new List<Point>();

    /// <summary>
    /// Where a panel's paths were projected: three anchor places and their screen points at that time. When the view only moved or
    /// zoomed (the anchors' new points are the old ones scaled and shifted, as on the flat map), the panel gets a scale + shift
    /// transform instead of a rebuild - projecting every point again and Noesis tessellating every stroke again was the cost of a
    /// moving map. A zoomed panel is rebuilt once the view has been still for a moment, so strokes and labels get their size back.
    /// On the globe a rotation is not a scale + shift: those views are rebuilt as before.
    /// </summary>
    private sealed class Fit
    {
        public readonly ScaleTransform Scale = new ScaleTransform();
        public readonly TranslateTransform Shift = new TranslateTransform();
        public readonly TransformGroup Group = new TransformGroup();
        public double Lat, Lon;             // anchor 0; anchors 1 and 2 are a degree east and north of it
        public float X0, Y0, X1, Y1, X2, Y2;
        public bool Valid;
        public float ClipX0, ClipY0, ClipX1, ClipY1;   // the cut area of the last build, in its view pixels
        public double SettleAt;             // when > 0: rebuild at this time (the view was zoomed)

        public Fit()
        {
            Group.Children.Add(Scale);
            Group.Children.Add(Shift);
        }
    }

    public MapView(MapBase map)
    {
        Map = map;
        theme = new Theme(map);
        linesPanel = new MapPanel { IsHitTestVisible = false };
        MapPanel.SetParentMap(linesPanel, map);
        coveragePanel = new MapPanel { IsHitTestVisible = false };
        MapPanel.SetParentMap(coveragePanel, map);
        labels = new Canvas { IsHitTestVisible = false };
        // Under the unit icons and labels, above the tiles and graticule: right after the graticule, as the Borders panels.
        int at = Math.Min(1, map.Children.Count);
        for (int i = 0; i < map.Children.Count; i++)
        {
            if (map.Children[i] is MapGraticule) { at = i + 1; break; }
        }
        while (at < map.Children.Count && map.Children[at] is FrameworkElement e && e.Tag is string tag && tag.StartsWith("sewerlabs."))
            at++;                                                       // behind the Borders / Cities panels
        map.Children.Insert(at, coveragePanel);
        map.Children.Insert(at + 1, linesPanel);
        linesPanel.Children.Add(labels);
        coveragePanel.Visibility = Visibility.Collapsed;
        linesPanel.Visibility = Visibility.Collapsed;
        onViewport = (s, e) => viewDirty = true;
        map.ViewportChanged += onViewport;
        linesFit = new Fit();
        coverageFit = new Fit();
        linesPanel.RenderTransform = linesFit.Group;
        coveragePanel.RenderTransform = coverageFit.Group;
    }

    public bool Alive => VisualTreeHelper.GetParent(Map) != null;

    public void Detach()
    {
        try
        {
            Map.ViewportChanged -= onViewport;
            Map.Children.Remove(linesPanel);
            Map.Children.Remove(coveragePanel);
            MapPanel.SetParentMap(linesPanel, null);
            MapPanel.SetParentMap(coveragePanel, null);
        }
        catch (Exception ex)
        {
            ModLog.Debug("map detach: " + ex.Message);
        }
    }

    // ---- projection ------------------------------------------------------------------------------------------------

    private bool Project(double lat, double lon, out float x, out float y)
    {
        var g = new GeoPosition(lat, lon);
#if FLATMAP
        Point p = Map.LocationToView(g);
        x = p.X; y = p.Y;
        return Math.Abs(x) < 1e7f && Math.Abs(y) < 1e7f;
#else
        bool visible = Map.TryLocationToView(g, out Point p);
        x = p.X; y = p.Y;
        return visible && Math.Abs(x) < 1e7f && Math.Abs(y) < 1e7f;
#endif
    }

    // ---- update ------------------------------------------------------------------------------------------------------

    public void Update(bool showLines, LineSet lines, bool showCoverage, CoverageSet coverage)
    {
        if (showLines != linesShown)
        {
            linesShown = showLines;
            linesPanel.Visibility = showLines ? Visibility.Visible : Visibility.Collapsed;
            viewDirty = true;
        }
        if (showCoverage != coverageShown)
        {
            coverageShown = showCoverage;
            coveragePanel.Visibility = showCoverage ? Visibility.Visible : Visibility.Collapsed;
            viewDirty = true;
        }
        if (Map.ActualWidth < 2 || Map.ActualHeight < 2)
            return;
        if (settingsVersion != Settings.Version)
        {
            linesFit.Valid = coverageFit.Valid = false;
            // styles (weight, opacity of the dimmed lines) are fixed when a path is made: make them again
            foreach (Path p in linePaths.Values) linesPanel.Children.Remove(p);
            foreach (Path p in coveragePaths.Values) coveragePanel.Children.Remove(p);
            linePaths.Clear();
            coveragePaths.Clear();
            linesVersion = coverageVersion = -1;
        }
        double now = UnityEngine.Time.unscaledTimeAsDouble;
        bool settingsChanged = settingsVersion != Settings.Version;
        settingsVersion = Settings.Version;
        bool view = viewDirty;
        viewDirty = false;
        bool linesData = showLines && lines.Version != linesVersion;
        bool coverageData = showCoverage && coverage.Version != coverageVersion;
        // A view change first tries the transform; what it cannot follow needs a rebuild.
        bool linesView = showLines && (view && !Follow(linesFit) || Settled(linesFit, now));
        bool coverageView = showCoverage && (view && !Follow(coverageFit) || Settled(coverageFit, now));
        // While the map turns (globe) the view changes every frame: such rebuilds run at most 20 times a second.
        if ((linesView || coverageView) && !settingsChanged && !linesData && !coverageData && now < nextViewBuild)
        {
            viewDirty = true;
            return;
        }
        if (linesView || coverageView) nextViewBuild = now + 0.05;
        if ((showLines && (settingsChanged || linesData || linesView)) || (showCoverage && (settingsChanged || coverageData || coverageView)))
            SetClip();
        if (showLines && (settingsChanged || linesData || linesView))
        {
            linesVersion = lines.Version;
            long t = Perf.Start();
            BuildLines(lines);
            Anchor(linesFit);
            Perf.Stop(Perf.Part.MapBuild, t);
        }
        if (showCoverage && (settingsChanged || coverageData || coverageView))
        {
            coverageVersion = coverage.Version;
            long t = Perf.Start();
            BuildCoverage(coverage);
            Anchor(coverageFit);
            Perf.Stop(Perf.Part.MapBuild, t);
        }
    }

    private bool Settled(Fit fit, double now) => fit.SettleAt > 0 && now >= fit.SettleAt;

    /// <summary>Records where the anchors are now (the paths were just projected) and clears the panel's transform.</summary>
    private void Anchor(Fit fit)
    {
        fit.Scale.ScaleX = fit.Scale.ScaleY = 1f;
        fit.Shift.X = fit.Shift.Y = 0f;
        fit.SettleAt = 0;
        fit.ClipX0 = clipX0; fit.ClipY0 = clipY0; fit.ClipX1 = clipX1; fit.ClipY1 = clipY1;
        Location c = Map.Center;
        fit.Lat = Math.Max(-80.0, Math.Min(79.0, c.Latitude));
        fit.Lon = c.Longitude;
        fit.Valid = Project(fit.Lat, fit.Lon, out fit.X0, out fit.Y0)
                    && Project(fit.Lat, fit.Lon + 1.0, out fit.X1, out fit.Y1)
                    && Project(fit.Lat + 1.0, fit.Lon, out fit.X2, out fit.Y2);
    }

    /// <summary>True when the panel could follow the new view with its transform (scale + shift, no turn).</summary>
    private bool Follow(Fit fit)
    {
        if (!fit.Valid) return false;
        if (!Project(fit.Lat, fit.Lon, out float x0, out float y0) || !Project(fit.Lat, fit.Lon + 1.0, out float x1, out float y1)
            || !Project(fit.Lat + 1.0, fit.Lon, out float x2, out float y2))
            return false;
        double ox = fit.X1 - fit.X0, oy = fit.Y1 - fit.Y0, nx = x1 - x0, ny = y1 - y0;
        double len = Math.Sqrt(ox * ox + oy * oy);
        if (len < 1e-3) return false;
        double scale = Math.Sqrt(nx * nx + ny * ny) / len;
        if (Math.Abs(ox * ny - oy * nx) / (len * len * scale) > 1e-4) return false;           // turned
        // the third anchor must land where scale + shift put it (a quarter pixel)
        double px = x0 + scale * (fit.X2 - fit.X0), py = y0 + scale * (fit.Y2 - fit.Y0);
        if (Math.Abs(px - x2) > 0.25 || Math.Abs(py - y2) > 0.25) return false;
        // the view, taken back into the build's pixels, must still lie inside the area that was cut out; else rebuild
        double sx = x0 - scale * fit.X0, sy = y0 - scale * fit.Y0;
        double vx0 = (0 - sx) / scale, vy0 = (0 - sy) / scale;
        double vx1 = (Map.ActualWidth - sx) / scale, vy1 = (Map.ActualHeight - sy) / scale;
        if (vx0 < fit.ClipX0 || vy0 < fit.ClipY0 || vx1 > fit.ClipX1 || vy1 > fit.ClipY1) return false;
        fit.Scale.ScaleX = fit.Scale.ScaleY = (float)scale;
        fit.Shift.X = (float)(x0 - scale * fit.X0);
        fit.Shift.Y = (float)(y0 - scale * fit.Y0);
        bool zoomed = Math.Abs(scale - 1.0) > 1e-3;
        fit.SettleAt = zoomed ? UnityEngine.Time.unscaledTimeAsDouble + 0.3 : fit.SettleAt;
        return true;
    }

    // ---- cutting to the view ---------------------------------------------------------------------------------------------

    private void SetClip()
    {
        float w = Map.ActualWidth, h = Map.ActualHeight, m = 0.5f * Math.Max(w, h);
        clipX0 = -m; clipY0 = -m; clipX1 = w + m; clipY1 = h + m;
    }

    /// <summary>Adds the points collected in <see cref="run"/> to the geometry, cut to the clip area, and empties the run.</summary>
    private void Emit(StreamGeometryContext c, bool fill)
    {
        if (run.Count >= 2)
        {
            if (fill) EmitPolygon(c);
            else EmitPolyline(c);
        }
        run.Clear();
    }

    private void EmitPolyline(StreamGeometryContext c)
    {
        bool drawing = false;
        for (int i = 1; i < run.Count; i++)
        {
            Point a = run[i - 1], b = run[i];
            if (!ClipSegment(ref a, ref b, out bool cutStart, out bool cutEnd)) { drawing = false; continue; }
            if (!drawing || cutStart) c.BeginFigure(a, false, false);
            c.LineTo(b, true, false);
            drawing = !cutEnd;
        }
    }

    /// <summary>Liang-Barsky: cuts the segment to the clip area; false when nothing of it is inside.</summary>
    private bool ClipSegment(ref Point a, ref Point b, out bool cutStart, out bool cutEnd)
    {
        cutStart = cutEnd = false;
        double x0 = a.X, y0 = a.Y, dx = b.X - x0, dy = b.Y - y0, t0 = 0, t1 = 1;
        if (!Edge(-dx, x0 - clipX0, ref t0, ref t1) || !Edge(dx, clipX1 - x0, ref t0, ref t1)
            || !Edge(-dy, y0 - clipY0, ref t0, ref t1) || !Edge(dy, clipY1 - y0, ref t0, ref t1))
            return false;
        if (t0 > 0) { a = new Point((float)(x0 + t0 * dx), (float)(y0 + t0 * dy)); cutStart = true; }
        if (t1 < 1) { b = new Point((float)(x0 + t1 * dx), (float)(y0 + t1 * dy)); cutEnd = true; }
        return true;
    }

    private static bool Edge(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0) return q >= 0;
        double r = q / p;
        if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
        else { if (r < t0) return false; if (r < t1) t1 = r; }
        return true;
    }

    /// <summary>Sutherland-Hodgman: cuts the closed polygon in <see cref="run"/> to the clip area.</summary>
    private void EmitPolygon(StreamGeometryContext c)
    {
        polyA.Clear();
        polyA.AddRange(run);
        ClipPolygon(polyA, polyB, 0, clipX0);
        ClipPolygon(polyB, polyA, 1, clipX1);
        ClipPolygon(polyA, polyB, 2, clipY0);
        ClipPolygon(polyB, polyA, 3, clipY1);
        if (polyA.Count < 3) return;
        c.BeginFigure(polyA[0], true, true);
        for (int i = 1; i < polyA.Count; i++) c.LineTo(polyA[i], true, false);
    }

    private static void ClipPolygon(List<Point> src, List<Point> dst, int side, float v)
    {
        dst.Clear();
        int n = src.Count;
        if (n == 0) return;
        Point prev = src[n - 1];
        bool prevIn = Inside(prev, side, v);
        for (int i = 0; i < n; i++)
        {
            Point cur = src[i];
            bool curIn = Inside(cur, side, v);
            if (curIn != prevIn) dst.Add(Cross(prev, cur, side, v));
            if (curIn) dst.Add(cur);
            prev = cur; prevIn = curIn;
        }
    }

    private static bool Inside(Point p, int side, float v) => side switch
    {
        0 => p.X >= v, 1 => p.X <= v, 2 => p.Y >= v, _ => p.Y <= v,
    };

    private static Point Cross(Point a, Point b, int side, float v)
    {
        if (side < 2)
        {
            double t = (v - a.X) / (double)(b.X - a.X);
            return new Point(v, (float)(a.Y + t * (b.Y - a.Y)));
        }
        double u = (v - a.Y) / (double)(b.Y - a.Y);
        return new Point((float)(a.X + u * (b.X - a.X)), v);
    }

    // ---- paths -------------------------------------------------------------------------------------------------------

    private StreamGeometryContext Context(string key)
    {
        if (!open.TryGetValue(key, out var pair))
        {
            var geometry = new StreamGeometry();
            pair = (geometry, geometry.Open());
            open[key] = pair;
        }
        return pair.context;
    }

    private void Commit(Dictionary<string, Path> paths, MapPanel panel, Func<string, Path> make)
    {
        foreach (var kv in open)
        {
            kv.Value.context.Dispose();
            if (!paths.TryGetValue(kv.Key, out Path path))
            {
                path = make(kv.Key);
                paths[kv.Key] = path;
                panel.Children.Insert(0, path);
            }
            path.Data = kv.Value.geometry;
        }
        foreach (var kv in paths)
            if (!open.ContainsKey(kv.Key)) kv.Value.Data = null;
        open.Clear();
    }

    private TextBlock Label(string text, float x, float y, Brush brush, float opacity)
    {
        if (labelsUsed == labelPool.Count)
        {
            var tb = new TextBlock { IsHitTestVisible = false, FontSize = 12f };
            labelPool.Add(tb);
            labels.Children.Add(tb);
        }
        TextBlock t = labelPool[labelsUsed++];
        t.Visibility = Visibility.Visible;
        t.Text = text;
        t.Foreground = brush;
        t.Opacity = opacity;
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
        return t;
    }

    private void EndLabels()
    {
        for (int i = labelsUsed; i < labelPool.Count; i++)
            labelPool[i].Visibility = Visibility.Collapsed;
        labelsUsed = 0;
    }

    // ---- sensor lines ----------------------------------------------------------------------------------------------------

    private void BuildLines(LineSet set)
    {
        open.Clear();
        labelsUsed = 0;
        foreach (LineSet.Line line in set.Lines)
        {
            string key = LineStyles.Key(line.Style, line.Bright, line.Live, line.Fill);
            StreamGeometryContext c = Context(key);
            bool started = false;
            float lastX = 0, lastY = 0;
            run.Clear();
            for (int i = 0; i < line.Lat.Length; i++)
            {
                if (!Project(line.Lat[i], line.Lon[i], out float x, out float y))
                {
                    Emit(c, line.Fill);
                    continue;
                }
                run.Add(new Point(x, y));
                lastX = x; lastY = y;
                started = true;
            }
            Emit(c, line.Fill);
            if (!string.IsNullOrEmpty(line.Label) && started && line.Bright
                && lastX >= 0 && lastY >= 0 && lastX <= Map.ActualWidth && lastY <= Map.ActualHeight)
            {
                Brush b = theme.Line(line.Style);
                Label(line.Label, lastX + 4, lastY - 8, b, 1f);
            }
        }
        Commit(linePaths, linesPanel, key => MakeLinePath(key));
        EndLabels();
    }

    private Path MakeLinePath(string key)
    {
        LineStyles.Parse(key, out LineStyle style, out bool bright, out bool live, out bool fill);
        float alpha = (bright ? 1f : Settings.DimOpacity.Value) * (live ? 1f : 0.45f);
        // The opacity goes into the brush: an element opacity below 1 makes Noesis draw the element into an offscreen picture first.
        var path = new Path { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
        if (fill)
            path.Fill = theme.Alpha(theme.Line(style), alpha * 0.16f);
        else
        {
            path.Stroke = theme.Alpha(theme.Line(style), alpha);
            path.StrokeThickness = BaseWeight * Settings.LineWidth.Value * LineStyles.Weight(style);
            string dashes = LineStyles.Dashes(style);
            if (dashes != null) path.StrokeDashArray = dashes;
        }
        return path;
    }

    // ---- coverage -------------------------------------------------------------------------------------------------------------

    private void BuildCoverage(CoverageSet set)
    {
        open.Clear();
        foreach (CoverageSet.Ring ring in set.Rings)
        {
            if (ring.Lat.Length < 3) continue;
            string key = "c|" + (int)ring.Kind + (ring.Fill ? "f" : "s");
            StreamGeometryContext c = Context(key);
            run.Clear();
            for (int i = 0; i < ring.Lat.Length; i++)
            {
                if (!Project(ring.Lat[i], ring.Lon[i], out float x, out float y)) { Emit(c, ring.Fill); continue; }
                run.Add(new Point(x, y));
            }
            Emit(c, ring.Fill);
        }
        Commit(coveragePaths, coveragePanel, key => MakeCoveragePath(key));
    }

    private Path MakeCoveragePath(string key)
    {
        bool fill = key.EndsWith("f");
        var kind = (CoverageKind)int.Parse(key.Substring(2, key.Length - 3));
        Brush brush = theme.Coverage(kind);
        var path = new Path { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
        if (fill)
            path.Fill = theme.Alpha(brush, 0.14f);
        else
        {
            path.Stroke = theme.Alpha(brush, 0.9f);
            path.StrokeThickness = BaseWeight * Settings.LineWidth.Value * 1.2f;
            if (kind == CoverageKind.Esm) path.StrokeDashArray = "6 3";
            if (kind == CoverageKind.Visual) path.StrokeDashArray = "2 3";
        }
        return path;
    }
}
