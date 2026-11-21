using System;
using System.Collections.Generic;
using MapControl;
using Noesis;
using GeoPosition = SeaPower.GeoPosition;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// The two layers on one map control: two <see cref="MapPanel"/>s laid into the map (sensor lines, coverage), each holding a few
/// Noesis <see cref="Path"/>s in screen pixels and a canvas of labels. The geometry is kept as longitude/latitude and
/// projected through the map (<c>TryLocationToView</c>, which leaves out the far side of the globe) whenever the view or the data
/// changes - the same screen-space method as the Borders mod uses, here because the lines run between arbitrary points, not
/// along a lon/lat tile grid.
/// </summary>
internal sealed class MapView
{
    /// <summary>
    /// Tag prefix of the Borders mod's map panels (its Map/MapSlots.cs): ours go behind them, so cities and borders stay on top of
    /// the cones. The two mods do not reference each other; the prefix is the agreement.
    /// </summary>
    private const string BordersPanelTag = "sewerlabs.";
    /// <summary>A rebuild for a moving view at most once per frame (s).</summary>
    private const double ViewBuildInterval = 0.016;
    /// <summary>A zoomed panel is rebuilt this long (s) after the zoom stopped, so strokes and labels get their size back.</summary>
    private const double SettleDelay = 0.3;
    /// <summary>The projection probes stay below this latitude (a map projection runs away towards the pole).</summary>
    private const double MaxProbeLatitude = 80.0;

    public readonly MapBase Map;
    private readonly MapPanel linesPanel, coveragePanel;
    private readonly Canvas labels;
    private readonly Dictionary<PathKey, Path> linePaths = new Dictionary<PathKey, Path>();
    private readonly Dictionary<PathKey, Path> coveragePaths = new Dictionary<PathKey, Path>();
    private readonly Dictionary<PathKey, (StreamGeometry geometry, StreamGeometryContext context)> open =
        new Dictionary<PathKey, (StreamGeometry, StreamGeometryContext)>();
    private readonly List<TextBlock> labelPool = new List<TextBlock>();
    private readonly EventHandler<ViewportChangedEventArgs> onViewport;
    private readonly Theme theme;
    private int labelsUsed;
    private bool viewDirty = true;
    private double nextViewBuild;
    private LineSet lastLines;
    private CoverageSet lastCoverage;
    private int linesVersion = -1, coverageVersion = -1, settingsVersion = -1;
    private bool linesShown, coverageShown;
    private readonly Fit linesFit, coverageFit;
    // The area paths are cut to, in view pixels: the view plus a view size on each side. Zoomed in, a sensor line or wedge runs
    // hundreds of thousands of pixels past the screen; uncut, Noesis tessellated all of it (every dash of a dotted line), which froze
    // the game when zoomed in on a contact.
    private float clipX0, clipY0, clipX1, clipY1;
    private readonly List<Point> run = new List<Point>();
    private readonly List<Point> polyA = new List<Point>(), polyB = new List<Point>();
    private readonly List<PathKey> emptied = new List<PathKey>();

    /// <summary>
    /// Where a panel's paths were projected: three anchor places and their screen points at that time. When the view only moved or
    /// zoomed (the anchors' new points are the old ones scaled and shifted), the panel gets a scale + shift
    /// transform instead of a rebuild - projecting every point again and Noesis tessellating every stroke again was the cost of a
    /// moving map. A zoomed panel is rebuilt once the view has been still for a moment, so strokes and labels get their size back.
    /// A rotation of the globe is not a scale + shift: those views are rebuilt.
    /// </summary>
    private sealed class Fit
    {
        public readonly ScaleTransform Scale = new ScaleTransform();
        public readonly TranslateTransform Shift = new TranslateTransform();
        public readonly TransformGroup Group = new TransformGroup();
        public double Lat, Lon;             // anchor 0; anchors 1 and 2 are a degree east and north of it
        public Triple At;
        public bool Valid;
        public float ClipX0, ClipY0, ClipX1, ClipY1;   // the cut area of the last build, in its view pixels
        public double SettleAt;             // when > 0: rebuild at this time (the view was zoomed)
        public readonly TranslateTransform Drift = new TranslateTransform();   // screen pixels the unit moved since the build (coverage)

        public Fit()
        {
            Group.Children.Add(Scale);
            Group.Children.Add(Shift);
            Group.Children.Add(Drift);
        }
    }

    /// <summary>A place and the places <c>step</c> degrees east and north of it, projected.</summary>
    private struct Triple { public float X0, Y0, X1, Y1, X2, Y2; }

    public MapView(MapBase map)
    {
        Map = map;
        theme = new Theme(map);
        linesPanel = new MapPanel { IsHitTestVisible = false };
        MapPanel.SetParentMap(linesPanel, map);
        coveragePanel = new MapPanel { IsHitTestVisible = false };
        MapPanel.SetParentMap(coveragePanel, map);
        labels = new Canvas { IsHitTestVisible = false };
        int at = SlotIn(map);
        map.Children.Insert(at, coveragePanel);
        map.Children.Insert(at + 1, linesPanel);
        linesPanel.Children.Add(labels);
        coveragePanel.Visibility = Visibility.Collapsed;
        linesPanel.Visibility = Visibility.Collapsed;
        onViewport = (s, e) => OnViewport();
        map.ViewportChanged += onViewport;
        linesFit = new Fit();
        coverageFit = new Fit();
        linesPanel.RenderTransform = linesFit.Group;
        coveragePanel.RenderTransform = coverageFit.Group;
    }

    /// <summary>Under the unit icons and labels, above the tiles and graticule: right after the graticule and the Borders panels.</summary>
    private static int SlotIn(MapBase map)
    {
        int at = Math.Min(1, map.Children.Count);
        for (int i = 0; i < map.Children.Count; i++)
        {
            if (map.Children[i] is MapGraticule) { at = i + 1; break; }
        }
        while (at < map.Children.Count && map.Children[at] is FrameworkElement e && e.Tag is string tag && tag.StartsWith(BordersPanelTag))
            at++;
        return at;
    }

    /// <summary>
    /// Moves the panels with the map at once, in the same frame the map moved (the next Update comes a frame later, so the lines and
    /// cones trailed the map by one frame while it was dragged or zoomed). What the transform cannot follow (a turning globe, a pan
    /// beyond the cut area) is rebuilt right here, at most about once per frame.
    /// </summary>
    private void OnViewport()
    {
        viewDirty = true;
        try
        {
            bool linesOk = !linesShown || Follow(linesFit), coverOk = !coverageShown || Follow(coverageFit);
            double now = UnityEngine.Time.unscaledTimeAsDouble;
            if ((!linesOk || !coverOk) && lastLines != null && lastCoverage != null && now >= nextViewBuild)
            {
                nextViewBuild = now + ViewBuildInterval;
                SetClip();
                Rebuild(!linesOk, !coverOk);
                viewDirty = false;
            }
        }
        catch (Exception ex) { Rig.Log.Debug("map view follow: " + ex.Message); }   // the next Update rebuilds what this missed
        FollowUnit();
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
            Rig.Log.Debug("map detach: " + ex.Message);
        }
    }

    private bool Project(double lat, double lon, out float x, out float y)
    {
        var g = new GeoPosition(lat, lon);
        bool visible = Map.TryLocationToView(g, out Point p);
        x = p.X; y = p.Y;
        return visible && Math.Abs(x) < 1e7f && Math.Abs(y) < 1e7f;
    }

    private bool ProjectTriple(double lat, double lon, double step, out Triple t)
    {
        t = default;
        return Project(lat, lon, out t.X0, out t.Y0) && Project(lat, lon + step, out t.X1, out t.Y1)
            && Project(lat + step, lon, out t.X2, out t.Y2);
    }

    /// <summary>The map's centre as a probe place: latitude kept low enough that the north probe <paramref name="step"/> away stays valid.</summary>
    private void ProbeCentre(double step, out double lat, out double lon)
    {
        Location c = Map.Center;
        lat = Math.Max(-MaxProbeLatitude, Math.Min(MaxProbeLatitude - step, c.Latitude));
        lon = c.Longitude;
    }

    /// <summary>Degrees of latitude or longitude per screen pixel at the map's centre (the finer of the two); MaxValue if unknown.</summary>
    public double DegreesPerPixel()
    {
        if (Map.ActualWidth < 2 || Map.ActualHeight < 2) return double.MaxValue;
        const double step = 0.01;
        try
        {
            ProbeCentre(step, out double lat, out double lon);
            if (!ProjectTriple(lat, lon, step, out Triple t)) return double.MaxValue;
            double px = Math.Max(Math.Sqrt((t.X1 - t.X0) * (t.X1 - t.X0) + (t.Y1 - t.Y0) * (t.Y1 - t.Y0)),
                                 Math.Sqrt((t.X2 - t.X0) * (t.X2 - t.X0) + (t.Y2 - t.Y0) * (t.Y2 - t.Y0)));
            return px < 1e-6 ? double.MaxValue : step / px;
        }
        catch (Exception) { return double.MaxValue; }   // the map is between layouts (no centre yet): the caller's fallback
    }

    /// <summary>Projects and builds the panels again for the current view; the data is the last one Update was given.</summary>
    private void Rebuild(bool lines, bool coverage, HashSet<int> onlyGroups = null)
    {
        if (lines && linesShown)
        {
            long t = Perf.Start();
            BuildLines(lastLines, onlyGroups);
            Anchor(linesFit);
            Perf.Stop(Perf.Part.MapBuild, t);
        }
        if (coverage && coverageShown)
        {
            long t = Perf.Start();
            BuildCoverage(lastCoverage);
            Anchor(coverageFit);
            Perf.Stop(Perf.Part.MapBuild, t);
        }
    }

    public void Update(bool showLines, LineSet lines, bool showCoverage, CoverageSet coverage)
    {
        lastLines = lines;
        lastCoverage = coverage;
        FollowUnit();
        Show(showLines, showCoverage);
        if (Map.ActualWidth < 2 || Map.ActualHeight < 2)
            return;
        bool restyled = settingsVersion != Settings.MapLook.Version;
        if (restyled) DropPaths();
        settingsVersion = Settings.MapLook.Version;
        double now = UnityEngine.Time.unscaledTimeAsDouble;
        bool view = viewDirty;
        viewDirty = false;
        bool linesView = linesShown && ViewNeedsBuild(linesFit, view, now);
        bool coverageView = coverageShown && ViewNeedsBuild(coverageFit, view, now);
        NewData(lines, coverage, out bool linesData, out bool coverageData);
        if (Throttled(linesView || coverageView, restyled || linesData || coverageData, now)) return;
        Build(lines, coverage, restyled || linesData || linesView, restyled || coverageData || coverageView, restyled || linesView);
    }

    private void NewData(LineSet lines, CoverageSet coverage, out bool linesData, out bool coverageData)
    {
        linesData = linesShown && lines.Version != linesVersion;
        coverageData = coverageShown && coverage.Version != coverageVersion;
    }

    /// <summary>A view change first tries the transform; what it cannot follow, or a zoom that has settled, needs a rebuild.</summary>
    private bool ViewNeedsBuild(Fit fit, bool view, double now) => (view && !Follow(fit)) || Settled(fit, now);

    /// <summary>
    /// While the map turns (globe) the view changes every frame: rebuilds for the view alone run at most once per frame; the view is
    /// looked at again next frame. A data or style change is drawn at once.
    /// </summary>
    private bool Throttled(bool forView, bool forData, double now)
    {
        if (!forView) return false;
        if (!forData && now < nextViewBuild)
        {
            viewDirty = true;
            return true;
        }
        nextViewBuild = now + ViewBuildInterval;
        return false;
    }

    /// <summary>Builds what is shown and asked for; <paramref name="whole"/> = every line group (style or view change).</summary>
    private void Build(LineSet lines, CoverageSet coverage, bool buildLines, bool buildCoverage, bool whole)
    {
        buildLines &= linesShown;
        buildCoverage &= coverageShown;
        if (!buildLines && !buildCoverage) return;
        SetClip();
        // Only the data moved on by one version and the panel sits untransformed where it was built: draw only the changed groups.
        bool partial = !whole && lines.Version == linesVersion + 1 && linesFit.Valid && Untransformed(linesFit);
        if (buildLines) linesVersion = lines.Version;
        if (buildCoverage) coverageVersion = coverage.Version;
        Rebuild(buildLines, buildCoverage, partial ? lines.ChangedGroups : null);
    }

    private void Show(bool showLines, bool showCoverage)
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
    }

    /// <summary>Styles (weight, opacity of the dimmed lines) are fixed when a path is made: a look setting makes them again.</summary>
    private void DropPaths()
    {
        linesFit.Valid = coverageFit.Valid = false;
        foreach (Path p in linePaths.Values) linesPanel.Children.Remove(p);
        foreach (Path p in coveragePaths.Values) coveragePanel.Children.Remove(p);
        linePaths.Clear();
        coveragePaths.Clear();
        linesVersion = coverageVersion = -1;
    }

    /// <summary>
    /// Keeps the coverage on its unit: the rings are built around where the unit was when the job started (a new job only about every
    /// nm, so a fast unit ran out of its own circle). Every frame they are shifted by the screen distance from that place to where the
    /// unit is now - the place it was built at and the unit both projected through the current view, so zoom and pan need nothing more.
    /// </summary>
    private void FollowUnit()
    {
        float dx = 0f, dy = 0f;
        try
        {
            CoverageSet c = lastCoverage;
            if (coverageShown && c != null && c.Unit != null && !c.Unit.IsDestroyed && c.Rings.Count > 0)
            {
                GeoPosition now = c.Unit.getGeoPosition();
                if (Project(c.Origin._latitude, c.Origin._longitude, out float x0, out float y0) && Project(now._latitude, now._longitude, out float x1, out float y1))
                {
                    dx = x1 - x0;
                    dy = y1 - y0;
                }
            }
        }
        catch (Exception) { dx = dy = 0f; }   // the unit is being removed: the rings stay where they were built
        if (coverageFit.Drift.X != dx) coverageFit.Drift.X = dx;
        if (coverageFit.Drift.Y != dy) coverageFit.Drift.Y = dy;
    }

    private bool Settled(Fit fit, double now) => fit.SettleAt > 0 && now >= fit.SettleAt;

    private static bool Untransformed(Fit fit) => fit.Scale.ScaleX == 1f && fit.Shift.X == 0f && fit.Shift.Y == 0f;

    /// <summary>Records where the anchors are now (the paths were just projected) and clears the panel's transform.</summary>
    private void Anchor(Fit fit)
    {
        fit.Scale.ScaleX = fit.Scale.ScaleY = 1f;
        fit.Shift.X = fit.Shift.Y = 0f;
        fit.SettleAt = 0;
        fit.ClipX0 = clipX0; fit.ClipY0 = clipY0; fit.ClipX1 = clipX1; fit.ClipY1 = clipY1;
        ProbeCentre(1.0, out fit.Lat, out fit.Lon);
        fit.Valid = ProjectTriple(fit.Lat, fit.Lon, 1.0, out fit.At);
    }

    /// <summary>True when the panel could follow the new view with its transform (scale + shift, no turn).</summary>
    private bool Follow(Fit fit)
    {
        if (!fit.Valid || !ProjectTriple(fit.Lat, fit.Lon, 1.0, out Triple t)) return false;
        Triple o = fit.At;
        double ox = o.X1 - o.X0, oy = o.Y1 - o.Y0, nx = t.X1 - t.X0, ny = t.Y1 - t.Y0;
        double len = Math.Sqrt(ox * ox + oy * oy);
        if (len < 1e-3) return false;
        double scale = Math.Sqrt(nx * nx + ny * ny) / len;
        if (Math.Abs(ox * ny - oy * nx) / (len * len * scale) > 1e-4) return false;           // turned
        // the third anchor must land where scale + shift put it (a quarter pixel)
        double px = t.X0 + scale * (o.X2 - o.X0), py = t.Y0 + scale * (o.Y2 - o.Y0);
        if (Math.Abs(px - t.X2) > 0.25 || Math.Abs(py - t.Y2) > 0.25) return false;
        // the view, taken back into the build's pixels, must still lie inside the area that was cut out; else rebuild
        double sx = t.X0 - scale * o.X0, sy = t.Y0 - scale * o.Y0;
        double vx0 = (0 - sx) / scale, vy0 = (0 - sy) / scale;
        double vx1 = (Map.ActualWidth - sx) / scale, vy1 = (Map.ActualHeight - sy) / scale;
        // Move with the map in any case (a panel that stops while the map still moves is the lag), then say whether it still covers the view.
        fit.Scale.ScaleX = fit.Scale.ScaleY = (float)scale;
        fit.Shift.X = (float)sx;
        fit.Shift.Y = (float)sy;
        bool zoomed = Math.Abs(scale - 1.0) > 1e-3;
        fit.SettleAt = zoomed ? UnityEngine.Time.unscaledTimeAsDouble + SettleDelay : fit.SettleAt;
        return !(vx0 < fit.ClipX0 || vy0 < fit.ClipY0 || vx1 > fit.ClipX1 || vy1 > fit.ClipY1);
    }

    private void SetClip()
    {
        float w = Map.ActualWidth, h = Map.ActualHeight, m = Math.Max(w, h);     // a whole view size each side: fast pans stay inside it longer
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

    private StreamGeometryContext Context(PathKey key)
    {
        if (!open.TryGetValue(key, out var pair))
        {
            // Every figure of one style goes into one geometry. With the default even-odd rule two wedges that overlap cancel each other
            // out where they cross (a cone blue at its edges and empty inside); non-zero fills the union. All wedges wind the same way.
            var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
            pair = (geometry, geometry.Open());
            open[key] = pair;
        }
        return pair.context;
    }

    /// <summary>
    /// Puts the geometries just built into their paths. With <paramref name="only"/> the build covered only those groups: paths of
    /// other groups keep what they show. A path left without geometry is taken out of the panel.
    /// </summary>
    private void Commit(Dictionary<PathKey, Path> paths, MapPanel panel, Func<PathKey, Path> make, HashSet<int> only = null)
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
        emptied.Clear();
        foreach (var kv in paths)
            if (!open.ContainsKey(kv.Key) && (only == null || only.Contains(kv.Key.Group))) emptied.Add(kv.Key);
        foreach (PathKey key in emptied)
        {
            panel.Children.Remove(paths[key]);
            paths.Remove(key);
        }
        open.Clear();
    }

    private void Label(string text, float x, float y, Brush brush)
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
        t.Opacity = 1f;
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y);
    }

    private void EndLabels()
    {
        for (int i = labelsUsed; i < labelPool.Count; i++)
            labelPool[i].Visibility = Visibility.Collapsed;
        labelsUsed = 0;
    }

    private void BuildLines(LineSet set, HashSet<int> only = null)
    {
        open.Clear();
        labelsUsed = 0;
        foreach (LineSet.Line line in set.Lines)
        {
            bool drawn = only == null || only.Contains(line.Group);
            // A line of a group that is not drawn again keeps its paths; only its label is placed again (labels are laid out for
            // all lines in one go).
            if (!drawn && (string.IsNullOrEmpty(line.Label) || !line.Bright)) continue;
            bool started = drawn ? ProjectLine(line, out float firstX, out float firstY, out float lastX, out float lastY)
                                 : ProjectEnds(line, out firstX, out firstY, out lastX, out lastY);
            if (!string.IsNullOrEmpty(line.Label) && started && line.Bright
                && lastX >= 0 && lastY >= 0 && lastX <= Map.ActualWidth && lastY <= Map.ActualHeight)
            {
                // Not at the end: the end is now the contact's marker, whose own text (track number, class, range) would sit under
                // this one. A quarter of the way back along the line it stands clear of it.
                float fx = lastX + (firstX - lastX) * 0.25f, fy = lastY + (firstY - lastY) * 0.25f;
                Label(line.Label, fx + 4, fy - 8, theme.Of(LineStyles.ColourKey(line.Kind)));
            }
        }
        Commit(linePaths, linesPanel, MakeLinePath, only);
        EndLabels();
    }

    private bool ProjectEnds(LineSet.Line line, out float firstX, out float firstY, out float lastX, out float lastY)
    {
        int n = line.Lat.Length - 1;
        lastX = lastY = 0f;
        return Project(line.Lat[0], line.Lon[0], out firstX, out firstY) && Project(line.Lat[n], line.Lon[n], out lastX, out lastY);
    }

    /// <summary>Projects the line into its path; true when any point of it was on the map (first and last give the label's place).</summary>
    private bool ProjectLine(LineSet.Line line, out float firstX, out float firstY, out float lastX, out float lastY)
    {
        firstX = firstY = lastX = lastY = 0f;
        bool started = false;
        StreamGeometryContext c = Context(PathKey.Of(line));
        run.Clear();
        for (int i = 0; i < line.Lat.Length; i++)
        {
            if (!Project(line.Lat[i], line.Lon[i], out float x, out float y))
            {
                Emit(c, line.Fill);
                continue;
            }
            run.Add(new Point(x, y));
            if (!started) { firstX = x; firstY = y; }
            lastX = x; lastY = y;
            started = true;
        }
        // zoomed in so far that a wedge (origin, edge, middle, edge) is longer than the screen: its bearing line alone
        if (line.Fill && !Settings.ZoomedWedges.Value && run.Count >= 3)
        {
            float dx = run[2].X - run[0].X, dy = run[2].Y - run[0].Y, limit = Math.Max(Map.ActualWidth, Map.ActualHeight);
            if (dx * dx + dy * dy > limit * limit) run.Clear();
        }
        Emit(c, line.Fill);
        return started;
    }

    private Path MakeLinePath(PathKey key)
    {
        var kind = (Data.Kind)key.Style;
        Brush brush = theme.Of(LineStyles.ColourKey(kind));
        float alpha = (key.Bright ? 1f : Settings.DimOpacity.Value) * (key.Live ? 1f : 0.45f);
        // The opacity goes into the brush: an element opacity below 1 makes Noesis draw the element into an offscreen picture first.
        var path = new Path { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
        if (key.Fill)
        {
            // A faint fill with a firm outline: the cones stay readable on bright terrain and where they overlap, and their fill no
            // longer drowns the map under it.
            path.Fill = GameColours.Faded(brush, alpha * 0.07f);
            path.Stroke = GameColours.Faded(brush, alpha * 0.85f);
            path.StrokeThickness = Settings.LineWidth.Value * 0.9f;
        }
        else
        {
            path.Stroke = GameColours.Faded(brush, alpha);
            path.StrokeThickness = Settings.LineWidth.Value * LineStyles.Weight(kind);
            string dashes = LineStyles.Dashes(kind);
            if (dashes != null) path.StrokeDashArray = dashes;
        }
        return path;
    }

    private void BuildCoverage(CoverageSet set)
    {
        open.Clear();
        foreach (CoverageSet.Ring ring in set.Rings)
        {
            if (ring.Lat.Length < 3) continue;
            StreamGeometryContext c = Context(PathKey.Of(ring));
            run.Clear();
            for (int i = 0; i < ring.Lat.Length; i++)
            {
                if (!Project(ring.Lat[i], ring.Lon[i], out float x, out float y)) { Emit(c, ring.Fill); continue; }
                run.Add(new Point(x, y));
            }
            Emit(c, ring.Fill);
        }
        Commit(coveragePaths, coveragePanel, MakeCoveragePath);
    }

    private Path MakeCoveragePath(PathKey key)
    {
        var kind = (CoverageKind)key.Style;
        Brush brush = theme.Of(LineStyles.ColourKey(kind));
        var path = new Path { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
        if (key.Fill)
            path.Fill = GameColours.Faded(brush, 0.14f);
        else
        {
            path.Stroke = GameColours.Faded(brush, 0.9f);
            path.StrokeThickness = Settings.LineWidth.Value * 1.2f;
            string dashes = LineStyles.Dashes(kind);
            if (dashes != null) path.StrokeDashArray = dashes;
        }
        return path;
    }
}
