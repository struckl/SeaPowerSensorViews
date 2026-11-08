using System;
using Noesis;
using SeaPower;
using SeaPowerSensorViews.Data;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>Sonar display window content: sonar chooser over the waterfall and depth strip.</summary>
public sealed class SonarView : UserControl
{
    private readonly TextBlock nameText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };

    public SonarView()
    {
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        bar.Children.Add(new TextBlock { Text = "Sonar", VerticalAlignment = VerticalAlignment.Center });
        var prev = new Button { Content = "<", Width = 24, Margin = new Thickness(6, 0, 0, 0), Focusable = false };
        var next = new Button { Content = ">", Width = 24, Margin = new Thickness(2, 0, 0, 0), Focusable = false };
        prev.Click += (s, e) => Step(-1);
        next.Click += (s, e) => Step(1);
        bar.Children.Add(prev);
        bar.Children.Add(next);
        var echoes = new ToggleButton { Content = "Echoes", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0), Focusable = false };
        echoes.Click += (s, e) => { if (DataContext is SonarWindow w) w.ShowEchoes = echoes.IsChecked == true; Registry.RefreshAll(); };
        bar.Children.Add(echoes);
        bar.Children.Add(nameText);
        Grid.SetRow(bar, 0);
        // The measuring cursor is drawn on its own layer over the waterfall, so moving the mouse redraws a few lines, not the
        // whole waterfall (up to 150 rows of dots).
        var surface = new SonarSurface();
        var cursorLayer = new SonarCursor { IsHitTestVisible = false };
        surface.CursorLayer = cursorLayer;
        var stack = new Grid();
        stack.Children.Add(surface);
        stack.Children.Add(cursorLayer);
        Grid.SetRow(stack, 1);
        root.Children.Add(bar);
        root.Children.Add(stack);
        Content = root;
        SizeProbe.Watch(this, "sonar");
        Loaded += (s, e) => UpdateName();
        DataContextChanged += (s, e) => UpdateName();
        SonarSurface.Changed += UpdateName;
        Unloaded += (s, e) => SonarSurface.Changed -= UpdateName;
    }

    private void Step(int d)
    {
        if (DataContext is SonarWindow w)
        {
            w.SonarIndex += d;
            SonarSampler.Update(w);
            Registry.RefreshAll();
        }
        UpdateName();
    }

    private void UpdateName()
    {
        if (DataContext is SonarWindow w && w.State.Valid)
            nameText.Text = w.State.UnitName + " - " + w.State.SonarName + " (" + w.State.Mode + ")";
        else nameText.Text = "no sonar";
    }
}

/// <summary>Takes the sonar display's numbers from the snapshot and the game's environment, once a second.</summary>
internal static class SonarSampler
{
    public static void Update(SonarWindow win)
    {
        SensorPicture pic = SensorPicture.Current;
        SonarState st = win.State;
        st.Valid = false;
        UnitRow unit = win.Subject();
        if (unit == null || !pic.InMission) return;
        var sonars = unit.Unit._obp?._sonarSystems;
        if (sonars == null || sonars.Count == 0) { st.UnitName = unit.Name; return; }
        int count = sonars.Count;
        win.SonarIndex = ((win.SonarIndex % count) + count) % count;
        SensorSystemSonar sonar = sonars[win.SonarIndex];
        string name = sonar.DisplayName.Value;
        string key = unit.Uid + ":" + name;
        if (!win.Falls.TryGetValue(key, out Waterfall wf))
            win.Falls[key] = wf = new Waterfall();

        SonarParameters sp = sonar._sonar?._sp;
        ObjectBase u = unit.Unit;
        st.Valid = true;
        st.UnitName = unit.Name;
        st.SonarName = name;
        st.Heading = unit.Heading;
        st.SpeedKts = unit.SpeedKts;
        st.Towed = sonar.IsTowedSensor || sonar.IsVDS;
        st.On = sonar.IsOn.Value;
        bool active = sonar._sonar != null && sonar._sonar.IsActive.Value;
        st.Mode = (st.Towed ? "towed " : "") + (active ? "active" : "passive") + (sonar.IsOn.Value ? "" : ", off");
        if (sp != null && sp._baffles != UnityEngine.Vector2.zero && !st.Towed) { st.BaffleFrom = sp._baffles.x; st.BaffleTo = sp._baffles.y; }
        else { st.BaffleFrom = float.NaN; st.BaffleTo = float.NaN; }

        // own noise: the same call the game's sonar makes for its own-ship noise term
        st.OwnNoise = float.NaN;
        SensorData sd = u._sensorData;
        if (sd != null && sp != null && sd._baseNoise >= 0f)
            st.OwnNoise = Compat.OwnNoiseDb(sd._baseNoise, sd._machineryRaftingGeneration, unit.SpeedKts, sd._flowNoise, sp._type,
                u._powerSystemInUse?.AdditionalNoise ?? 0f, u._cavitationIntensity, sd._cavitationNoise);

        // depth strip
        st.DepthFt = unit.DepthFt;
        GeoPosition geo = u.getGeoPosition();
        try
        {
            float layer = Singleton<SeaPower.Environment>.Instance.getLayerHeight(geo);
            st.LayerFt = layer < 0f ? -layer * SensorPicture.FeetPerUnit : float.NaN;
        }
        catch (Exception) { st.LayerFt = float.NaN; }
        float ay = sonar.SensorPosition.y;
        st.ArrayFt = ay < 0f ? -ay * SensorPicture.FeetPerUnit : float.NaN;
        float bed = Terrain.HeightAt(geo);
        st.SeabedFt = bed < 0f ? -bed * SensorPicture.MetersPerUnit * 3.28084f : float.NaN;

        st.Marks.Clear();
        foreach (ContactRow cr in pic.Contacts)
            foreach (Hit h in cr.Hits)
            {
                if (h.Unit != unit || !h.Live || (h.Kind != Kind.Passive && h.Kind != Kind.Active) || h.SensorName != name) continue;
                float dep = float.IsNaN(h.AltitudeUnits) || h.AltitudeUnits > 0f ? float.NaN : -h.AltitudeUnits * SensorPicture.FeetPerUnit;
                st.Marks.Add(new SonarState.Mark
                {
                    Label = cr.Label, Rel = Ang.DeltaAngle(unit.Heading, h.Bearing), RangeM = h.RangeM, Age = (float)(pic.Time - h.FixTime),
                    DepthFt = dep, DepthErrFt = h.AltitudeError * SensorPicture.FeetPerUnit, Active = h.Kind == Kind.Active,
                });
            }
        if (wf.LastVersion == pic.Version) return;
        wf.LastVersion = pic.Version;
        var dots = wf.Push(float.IsNaN(st.OwnNoise) ? 0f : st.OwnNoise, unit.Heading);
        foreach (ContactRow cr in pic.Contacts)
        {
            foreach (Hit h in cr.Hits)
            {
                if (h.Unit != unit || !h.Live || (h.Kind != Kind.Passive && h.Kind != Kind.Active) || h.SensorName != name) continue;
                float rel = Ang.DeltaAngle(unit.Heading, h.Bearing);          // -180..180, starboard positive
                int bin = (int)((rel + 180f) / 360f * SonarWindow.Bins);
                bin = Math.Max(0, Math.Min(SonarWindow.Bins - 1, bin));
                float lvl = float.IsNaN(h.Signal) ? 0.6f : Math.Max(0f, Math.Min(1f, h.Signal / 30f));
                dots.Add(new Waterfall.Dot
                {
                    Bin = (short)bin,
                    Width = (byte)Math.Max(1, Math.Min(8, (int)(h.BearingError / 2f) + 1)),
                    Level = (byte)(70 + 185 * lvl),
                    Active = h.Kind == Kind.Active,
                });
            }
        }
    }
}

internal static class Ang
{
    public static float DeltaAngle(float a, float b)
    {
        float d = (b - a) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return d;
    }
}

public sealed class SonarSurface : Surface
{
    internal override bool Retro => Settings.RetroLook.Value;
    internal static event Action Changed;
    private SonarWindow Win => DataContext as SonarWindow;
    internal SonarCursor CursorLayer;

    public SonarSurface()
    {
        MouseMove += (s, e) => CursorLayer?.MoveTo(e.GetPosition(this));
        MouseLeave += (s, e) => CursorLayer?.MoveTo(new Point(-1, -1));
    }

    /// <summary>The waterfall's rectangle in a surface of this size (shared with the cursor layer).</summary>
    internal static bool Layout(float w, float h, out float x0, out float y0, out float ww, out float hh, out float stripW)
    {
        stripW = Math.Min(190f, w * 0.28f);
        x0 = 8; y0 = 34; ww = w - stripW - 24; hh = h - y0 - 26;
        return ww >= 60 && hh >= 60;
    }

    private readonly System.Collections.Generic.Dictionary<int, StreamGeometryContext> batches = new System.Collections.Generic.Dictionary<int, StreamGeometryContext>();
    private readonly System.Collections.Generic.List<(int key, StreamGeometry geometry)> batchList = new System.Collections.Generic.List<(int, StreamGeometry)>();

    private static void AddRect(StreamGeometryContext c, float x, float y, float w, float h)
    {
        c.BeginFigure(new Point(x, y), true, true);
        c.LineTo(new Point(x + w, y), false, false);
        c.LineTo(new Point(x + w, y + h), false, false);
        c.LineTo(new Point(x, y + h), false, false);
    }

    private static bool InBaffles(float from, float to, float rel)
    {
        if (float.IsNaN(from)) return false;
        return from < to ? (from < rel && rel < to) : (to >= rel || rel >= from);
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));
        SonarWindow win = Win;
        if (win == null) return;
        SonarState st = win.State;
        if (!st.Valid)
        {
            Draw(dc, st.UnitName != null ? st.UnitName + " has no sonar." : "No unit.", 8, 8, 12, Normal);
            return;
        }
        if (!Layout(w, h, out float x0, out float y0, out float ww, out float hh, out float stripW)) return;
        Waterfall wf = null;
        win.Falls.TryGetValue(KeyOf(win), out wf);

        // ---- waterfall ---------------------------------------------------------------------------------------------
        dc.DrawRectangle(Header, PenOf(Lines, 1f), new Rect(x0, y0, ww, hh));
        float binW = ww / SonarWindow.Bins;
        if (!float.IsNaN(st.BaffleFrom))
        {
            float run0 = -1f;
            for (int b = 0; b <= SonarWindow.Bins; b++)
            {
                bool inside = b < SonarWindow.Bins && InBaffles(st.BaffleFrom, st.BaffleTo, b * 2f - 180f + 1f);
                if (inside && run0 < 0f) run0 = b;
                if (!inside && run0 >= 0f)
                {
                    dc.DrawRectangle(Fade(Lines, 0.55f), null, new Rect(x0 + run0 * binW, y0, (b - run0) * binW, hh));
                    run0 = -1f;
                }
            }
            DrawCentered(dc, "baffles", x0 + ww * 0.08f, y0 + hh / 2f, 10, Normal);
            DrawCentered(dc, "baffles", x0 + ww * 0.92f, y0 + hh / 2f, 10, Normal);
        }
        for (int deg = -150; deg <= 150; deg += 30)
        {
            float x = x0 + (deg + 180f) / 360f * ww;
            dc.DrawLine(PenOf(Fade(Lines, 0.8f), 1f), new Point(x, y0), new Point(x, y0 + hh));
            DrawCentered(dc, deg == 0 ? "bow" : (deg > 0 ? "+" : "") + deg, x, y0 - 14, 10, Normal);
        }
        if (!win.ShowEchoes)
        {
        if (wf != null && wf.Count > 0)
        {
            // One geometry per colour and brightness step instead of one rectangle per dot.
            float rowH = hh / SonarWindow.Rows;
            batches.Clear();
            batchList.Clear();
            for (int age = 0; age < wf.Count; age++)
            {
                var dots = wf.Rows[wf.Back(age)];
                float y = y0 + age * rowH;
                for (int i = 0; i < dots.Count; i++)
                {
                    Waterfall.Dot d = dots[i];
                    float a = d.Level / 255f * (1f - age * 0.004f);
                    int key = (d.Active ? 64 : 0) + Math.Max(0, Math.Min(32, (int)Math.Round(a * 32f)));
                    if (!batches.TryGetValue(key, out StreamGeometryContext c))
                    {
                        var g = new StreamGeometry();
                        batches[key] = c = g.Open();
                        batchList.Add((key, g));
                    }
                    float dw = Math.Max(2f, d.Width * binW);
                    AddRect(c, x0 + d.Bin * binW - dw / 2f + binW / 2f, y, dw, Math.Max(1.5f, rowH + 0.5f));
                }
            }
            foreach (StreamGeometryContext c in batches.Values) c.Dispose();
            foreach ((int key, StreamGeometry g) in batchList)
                dc.DrawGeometry(Fade(key >= 64 ? Blue : Green, (key & 63) / 32f), null, g);
            batches.Clear();
            batchList.Clear();
        }
        if (!st.On)
            DrawCentered(dc, "sonar is off - nothing is listening", x0 + ww / 2, y0 + hh / 2f - 24, 12, Emphasis);
        }
        else DrawEchoes(dc, st, x0, y0, ww, hh);
        if (!win.ShowEchoes)
        {
        DrawRight(dc, "now", x0 + ww - 4, y0 + 2, 9, Normal);
        DrawRight(dc, SonarWindow.Rows + " s ago", x0 + ww - 4, y0 + hh - 12, 9, Normal);
        }
        Draw(dc, win.ShowEchoes ? "Echoes by bearing relative to the bow and range; filled = active, ring = passive with a range estimate; fades with the age of the fix." : "Bearing relative to the bow (the stern is at both edges), newest at the top.  Brighter = stronger signal excess.", x0, y0 + hh + 6, 10, Normal);

        // ---- own noise line -------------------------------------------------------------------------------------------
        string noise = float.IsNaN(st.OwnNoise) ? "own noise n/a" : "own noise " + st.OwnNoise.ToString("0") + " dB";
        Draw(dc, noise + "   speed " + st.SpeedKts.ToString("0") + " kt", x0, 8, 11, Highlight);
        if (wf != null && wf.Count > 1)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < wf.Count; i++) { float n = wf.Noise[wf.Back(i)]; if (n < lo) lo = n; if (n > hi) hi = n; }
            if (hi - lo > 0.5f)
            {
                float gx = x0 + 240, gw = Math.Min(180f, ww - 250);
                if (gw > 30)
                    for (int i = 1; i < wf.Count; i++)
                    {
                        float xa = gx + gw * (1f - (i - 1f) / SonarWindow.Rows), xb = gx + gw * (1f - (float)i / SonarWindow.Rows);
                        float ya = 24 - 14 * (wf.Noise[wf.Back(i - 1)] - lo) / (hi - lo), yb = 24 - 14 * (wf.Noise[wf.Back(i)] - lo) / (hi - lo);
                        dc.DrawLine(PenOf(Emphasis, 1f), new Point(xa, ya), new Point(xb, yb));
                    }
            }
        }

        // ---- depth strip ---------------------------------------------------------------------------------------------------
        float sx = x0 + ww + 16, sw = stripW - 8;
        float maxDepth = 300f;
        foreach (float d in new[] { st.SeabedFt, st.LayerFt * 1.4f, st.DepthFt * 1.3f, st.ArrayFt * 1.3f })
            if (!float.IsNaN(d) && d > maxDepth) maxDepth = d;
        maxDepth = (float)Math.Ceiling(maxDepth / 100f) * 100f;
        float top = y0, bottom = y0 + hh;
        dc.DrawRectangle(Header, PenOf(Lines, 1f), new Rect(sx, top, sw, bottom - top));
        DrawCentered(dc, "Depth (ft)", sx + sw / 2, y0 - 28, 10, Highlight);
        Func<float, float> yOf = d => top + (bottom - top) * Math.Min(1f, d / maxDepth);
        dc.DrawRectangle(Fade(Blue, 0.18f), null, new Rect(sx + 1, top + 1, sw - 2, (!float.IsNaN(st.LayerFt) ? yOf(st.LayerFt) : bottom) - top - 1));
        if (!float.IsNaN(st.SeabedFt))
        {
            float yb = yOf(st.SeabedFt);
            dc.DrawRectangle(Fade(Normal, 0.45f), null, new Rect(sx + 1, yb, sw - 2, Math.Max(1f, bottom - yb)));
            DrawRight(dc, "seabed " + st.SeabedFt.ToString("0"), sx + sw - 3, Math.Min(bottom - 13, yb - 13), 10, Normal);
        }
        if (!float.IsNaN(st.LayerFt))
        {
            float yl = yOf(st.LayerFt);
            dc.DrawLine(PenOf(Emphasis, 1.5f), new Point(sx, yl), new Point(sx + sw, yl));
            Draw(dc, "layer " + st.LayerFt.ToString("0"), sx + 4, yl - 13, 10, Emphasis);
        }
        Draw(dc, "surface", sx + 4, top + 2, 10, Normal);
        if (!float.IsNaN(st.DepthFt))
        {
            float yd = yOf(st.DepthFt);
            dc.DrawEllipse(Highlight, null, new Point(sx + sw * 0.3f, yd), 4, 4);
            Draw(dc, "own " + st.DepthFt.ToString("0"), sx + sw * 0.3f + 8, yd - 6, 10, Highlight);
        }
        if (!float.IsNaN(st.ArrayFt))
        {
            float ya = yOf(st.ArrayFt);
            dc.DrawLine(PenOf(Green, 1.5f), new Point(sx + sw * 0.3f, yOf(float.IsNaN(st.DepthFt) ? 0f : st.DepthFt)), new Point(sx + sw * 0.7f, ya));
            dc.DrawRectangle(Green, null, new Rect(sx + sw * 0.7f - 3, ya - 3, 6, 6));
            Draw(dc, "array " + st.ArrayFt.ToString("0"), sx + 4, ya + 4, 10, Green);
        }
        foreach (SonarState.Mark m in st.Marks)
        {
            if (float.IsNaN(m.DepthFt)) continue;
            float ym = yOf(m.DepthFt), mx = sx + sw * 0.88f;
            Brush mc = Fade(m.Active ? Blue : Green, Math.Max(0.4f, 1f - m.Age / 60f));
            if (m.DepthErrFt > 1f)
                dc.DrawLine(PenOf(mc, 1f), new Point(mx, yOf(Math.Max(0f, m.DepthFt - m.DepthErrFt))), new Point(mx, yOf(m.DepthFt + m.DepthErrFt)));
            dc.DrawEllipse(mc, null, new Point(mx, ym), 3, 3);
            DrawRight(dc, m.Label + " " + m.DepthFt.ToString("0"), mx - 6, ym - 6, 9, mc);
            if (!float.IsNaN(st.LayerFt))
                DrawRight(dc, m.DepthFt < st.LayerFt ? "above layer" : "below layer", mx - 6, ym + 4, 8, Fade(Normal, 0.8f));
        }
    }

    private void DrawEchoes(DrawingContext dc, SonarState st, float x0, float y0, float ww, float hh)
    {
        float maxM = 4000f;
        foreach (SonarState.Mark m in st.Marks)
            if (!float.IsNaN(m.RangeM) && m.RangeM > maxM) maxM = m.RangeM;
        float step = maxM > 40000f ? 20000f : maxM > 10000f ? 5000f : 1000f;
        maxM = (float)Math.Ceiling(maxM * 1.05f / step) * step;
        for (float r = step; r <= maxM + 1f; r += step)
        {
            float y = y0 + hh - hh * r / maxM;
            dc.DrawLine(PenOf(Fade(Lines, 0.8f), 1f), new Point(x0, y), new Point(x0 + ww, y));
            DrawRight(dc, (r / 1852f).ToString("0.#") + " nm", x0 + ww - 3, y + 1, 9, Normal);
        }
        int n = 0;
        foreach (SonarState.Mark m in st.Marks)
        {
            if (float.IsNaN(m.RangeM)) continue;
            n++;
            float x = x0 + (m.Rel + 180f) / 360f * ww, y = y0 + hh - hh * m.RangeM / maxM;
            Brush c = Fade(m.Active ? Blue : Green, Math.Max(0.35f, 1f - m.Age / 90f));
            if (m.Active) dc.DrawEllipse(c, null, new Point(x, y), 4, 4);
            else dc.DrawEllipse(null, PenOf(c, 1.5f), new Point(x, y), 4, 4);
            Draw(dc, m.Label + " " + (m.RangeM / 1852f).ToString("0.0") + " nm  " + m.Age.ToString("0") + " s", x + 7, y - 6, 10, c);
        }
        if (n == 0) DrawCentered(dc, "no echo with a range yet", x0 + ww / 2, y0 + hh / 2f, 12, Normal);
    }

    internal static string KeyOf(SonarWindow win)
    {
        SonarState st = win.State;
        UnitRow unit = win.Subject();
        return unit == null ? "" : unit.Uid + ":" + st.SonarName;
    }

    internal static void RaiseChanged() => Changed?.Invoke();
}

/// <summary>The sonar display's measuring cursor: bearing (true and relative to the bow) and age under the mouse, on its own layer.</summary>
public sealed class SonarCursor : Surface
{
    internal override bool Retro => Settings.RetroLook.Value;
    private Point cursor = new Point(-1, -1);
    private SonarWindow Win => DataContext as SonarWindow;

    public SonarCursor() => Background = null;

    internal void MoveTo(Point p)
    {
        if (p.X < 0 && cursor.X < 0) return;
        cursor = p;
        InvalidateVisual();
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        SonarWindow win = Win;
        if (win == null || win.ShowEchoes || cursor.X < 0) return;
        SonarState st = win.State;
        if (!st.Valid || !SonarSurface.Layout(w, h, out float x0, out float y0, out float ww, out float hh, out _)) return;
        if (cursor.X < x0 || cursor.X > x0 + ww || cursor.Y < y0 || cursor.Y > y0 + hh) return;
        float rel = (float)((cursor.X - x0) / ww * 360f - 180f);
        float secs = (float)((cursor.Y - y0) / hh * SonarWindow.Rows);
        float brg = SensorPicture.Wrap360(st.Heading + rel);
        Pen cp = PenOf(Highlight, 1f);
        dc.DrawLine(cp, new Point(cursor.X, y0), new Point(cursor.X, y0 + hh));
        dc.DrawLine(cp, new Point(x0, cursor.Y), new Point(x0 + ww, cursor.Y));
        string t = "brg " + Fmt.Compass(brg) + "  rel " + (rel > 0 ? "+" : "") + rel.ToString("0") + "  " + secs.ToString("0") + " s ago";
        float tx = cursor.X > x0 + ww - 190 ? (float)cursor.X - 190 : (float)cursor.X + 8;
        float ty = cursor.Y > y0 + hh - 20 ? (float)cursor.Y - 18 : (float)cursor.Y + 6;
        dc.DrawRectangle(Fade(Back, 0.85f), null, new Rect(tx - 2, ty - 1, 188, 15));
        Draw(dc, t, tx, ty, 10, Highlight);
    }
}
