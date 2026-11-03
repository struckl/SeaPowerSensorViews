using System;
using Noesis;
using SeaPower;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Ui;

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
        bar.Children.Add(nameText);
        Grid.SetRow(bar, 0);
        var surface = new SonarSurface();
        Grid.SetRow(surface, 1);
        root.Children.Add(bar);
        root.Children.Add(surface);
        Content = root;
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
        float bed = TerrainUtils.GetHeightAt(geo);
        st.SeabedFt = bed < 0f ? -bed * SensorPicture.MetersPerUnit * 3.28084f : float.NaN;

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
    internal static event Action Changed;
    private SonarWindow Win => DataContext as SonarWindow;
    private Point cursor = new Point(-1, -1);

    public SonarSurface()
    {
        MouseMove += (s, e) => { cursor = e.GetPosition(this); InvalidateVisual(); };
        MouseLeave += (s, e) => { cursor = new Point(-1, -1); InvalidateVisual(); };
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
        float stripW = Math.Min(190f, w * 0.28f);
        float x0 = 8, y0 = 34, ww = w - stripW - 24, hh = h - y0 - 26;
        if (ww < 60 || hh < 60) return;
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
        if (wf != null && wf.Count > 0)
        {
            float rowH = hh / SonarWindow.Rows;
            for (int age = 0; age < wf.Count; age++)
            {
                var dots = wf.Rows[wf.Back(age)];
                float y = y0 + age * rowH;
                for (int i = 0; i < dots.Count; i++)
                {
                    Waterfall.Dot d = dots[i];
                    float a = d.Level / 255f * (1f - age * 0.004f);
                    Brush c = Fade(d.Active ? Blue : Green, a);
                    float dw = Math.Max(2f, d.Width * binW);
                    dc.DrawRectangle(c, null, new Rect(x0 + d.Bin * binW - dw / 2f + binW / 2f, y, dw, Math.Max(1.5f, rowH + 0.5f)));
                }
            }
        }
        if (!st.On)
            DrawCentered(dc, "sonar is off - nothing is listening", x0 + ww / 2, y0 + hh / 2f - 24, 12, Emphasis);
        // measuring cursor: bearing (true and relative to the bow) and age under the mouse
        if (cursor.X >= x0 && cursor.X <= x0 + ww && cursor.Y >= y0 && cursor.Y <= y0 + hh)
        {
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
        DrawRight(dc, "now", x0 + ww - 4, y0 + 2, 9, Normal);
        DrawRight(dc, SonarWindow.Rows + " s ago", x0 + ww - 4, y0 + hh - 12, 9, Normal);
        Draw(dc, "Bearing relative to the bow (the stern is at both edges), newest at the top.  Brighter = stronger signal excess.", x0, y0 + hh + 6, 10, Normal);

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
    }

    internal static string KeyOf(SonarWindow win)
    {
        SonarState st = win.State;
        UnitRow unit = win.Subject();
        return unit == null ? "" : unit.Uid + ":" + st.SonarName;
    }

    internal static void RaiseChanged() => Changed?.Invoke();
}
