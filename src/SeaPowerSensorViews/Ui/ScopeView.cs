using System;
using Noesis;
using SeaPower;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Ui;

/// <summary>Radar scope window content: a small tool row (range, horizon, target altitude) over the scope.</summary>
public sealed class ScopeView : UserControl
{
    private readonly TextBlock rangeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0), MinWidth = 48 };
    private readonly TextBlock altText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), MinWidth = 90 };
    private readonly CheckBox horizon = new CheckBox { Content = "Radar horizon", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox headingUp = new CheckBox { Content = "Heading up", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly Slider altitude = new Slider { Minimum = 0, Maximum = 60000, Value = 100, Width = 130, VerticalAlignment = VerticalAlignment.Center, SmallChange = 100, LargeChange = 1000 };
    private bool syncing;

    public ScopeView()
    {
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };   // wraps in a narrow window
        bar.Children.Add(new TextBlock { Text = "Range", VerticalAlignment = VerticalAlignment.Center });
        var minus = new Button { Content = "-", Width = 24, Margin = new Thickness(6, 0, 0, 0), Focusable = false };
        var plus = new Button { Content = "+", Width = 24, Margin = new Thickness(2, 0, 0, 0), Focusable = false };
        minus.Click += (s, e) => { Win?.StepRange(1); Sync(); };
        plus.Click += (s, e) => { Win?.StepRange(-1); Sync(); };
        bar.Children.Add(minus);
        bar.Children.Add(plus);
        bar.Children.Add(rangeText);
        bar.Children.Add(horizon);
        bar.Children.Add(new TextBlock { Text = "Target altitude", VerticalAlignment = VerticalAlignment.Center });
        bar.Children.Add(altitude);
        bar.Children.Add(altText);
        bar.Children.Add(headingUp);
        horizon.Click += (s, e) => { if (Win != null && !syncing) Win.ShowHorizon = horizon.IsChecked == true; Registry.RefreshAll(); };
        headingUp.Click += (s, e) => { if (Win != null && !syncing) Win.HeadingUp = headingUp.IsChecked == true; Registry.RefreshAll(); };
        altitude.ValueChanged += (s, e) =>
        {
            if (Win == null || syncing) return;
            Win.TargetAltitudeFt = altitude.Value;
            altText.Text = Win.TargetAltitudeFt.ToString("0") + " ft";
            Registry.RefreshAll();
        };
        Grid.SetRow(bar, 0);
        // Three layers: the picture (redrawn with each snapshot), the sweep (drawn once, then only turned) and the measuring
        // cursor (redrawn on mouse moves). The sweep used to redraw the whole scope 25 times a second.
        var scope = new ScopeSurface();
        var sweep = new ScopeSweep { IsHitTestVisible = false };
        var cursorLayer = new ScopeCursor { IsHitTestVisible = false };
        scope.CursorLayer = cursorLayer;
        var stack = new Grid();
        stack.Children.Add(scope);
        stack.Children.Add(sweep);
        stack.Children.Add(cursorLayer);
        Grid.SetRow(stack, 1);
        root.Children.Add(bar);
        root.Children.Add(stack);
        Content = root;
        Loaded += (s, e) => Sync();
        DataContextChanged += (s, e) => Sync();
    }

    private ScopeWindow Win => DataContext as ScopeWindow;

    private void Sync()
    {
        ScopeWindow w = Win;
        if (w == null) return;
        syncing = true;
        try
        {
            rangeText.Text = ScopeWindow.Ranges[w.RangeIndex].ToString("0") + " nm";
            altitude.Value = w.TargetAltitudeFt;
            altText.Text = w.TargetAltitudeFt.ToString("0") + " ft";
            horizon.IsChecked = w.ShowHorizon;
            headingUp.IsChecked = w.HeadingUp;
        }
        finally { syncing = false; }
        Registry.RefreshAll();
    }
}

/// <summary>Where the scope's circle sits in a surface of this size, shared by its three layers.</summary>
internal struct ScopeGeometry
{
    public float Cx, Cy, Radius, RangeNm, PxPerNm, Up;

    public static bool Of(ScopeWindow win, UnitRow unit, float w, float h, out ScopeGeometry g)
    {
        g = default;
        if (win == null || unit == null) return false;
        g.Cx = w / 2f; g.Cy = h / 2f;
        g.Radius = Math.Max(20f, Math.Min(w, h) / 2f - 20f);
        g.RangeNm = ScopeWindow.Ranges[win.RangeIndex];
        g.PxPerNm = g.Radius / g.RangeNm;
        g.Up = win.HeadingUp ? unit.Heading : 0f;      // compass bearing that points up
        return true;
    }
}

public sealed class ScopeSurface : Surface
{
    private ScopeWindow Win => DataContext as ScopeWindow;
    internal ScopeCursor CursorLayer;
    private int strobeVersion = -1;

    public ScopeSurface()
    {
        MouseMove += (s, e) => CursorLayer?.MoveTo(e.GetPosition(this));
        MouseLeave += (s, e) => CursorLayer?.MoveTo(new Point(-1, -1));
        MouseWheel += (s, e) =>
        {
            Win?.StepRange(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
            Registry.RefreshAll();
        };
    }

    /// <summary>Between snapshots the picture is redrawn only when a radar warning or jam strobe came or went.</summary>
    internal override bool Animated => true;
    internal override void Animate()
    {
        UnitRow unit = Win?.Subject();
        int v = unit?.StrobeVersion ?? -1;
        if (v == strobeVersion) return;
        strobeVersion = v;
        InvalidateVisual();
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));
        ScopeWindow win = Win;
        SensorPicture pic = SensorPicture.Current;
        UnitRow unit = win?.Subject();
        if (win == null || !pic.InMission || unit == null)
        {
            Draw(dc, "No unit.", 8, 8, 12, Normal);
            return;
        }
        ScopeGeometry.Of(win, unit, w, h, out ScopeGeometry g);
        float cx = g.Cx, cy = g.Cy, radius = g.Radius, rangeNm = g.RangeNm, pxPerNm = g.PxPerNm, up = g.Up;
        strobeVersion = unit.StrobeVersion;

        // ---- rings and bearing marks -----------------------------------------------------------------------------
        Pen ring = PenOf(Fade(Lines, 1f), 1f);
        for (int i = 1; i <= 4; i++)
        {
            float r = radius * i / 4f;
            dc.DrawEllipse(null, ring, new Point(cx, cy), r, r);
            DrawCentered(dc, (rangeNm * i / 4f).ToString("0.#"), cx + 8, cy - r - 12, 9, Normal);
        }
        for (int b = 0; b < 360; b += 30)
        {
            float a = b - up;
            dc.DrawLine(ring, Polar(cx, cy, radius, a), Polar(cx, cy, radius + (b % 90 == 0 ? 7f : 4f), a));
            Point p = Polar(cx, cy, radius + 16f, a);
            string label = b == 0 ? "N" : b == 90 ? "E" : b == 180 ? "S" : b == 270 ? "W" : b.ToString();
            DrawCentered(dc, label, p.X, p.Y - 6, 10, b % 90 == 0 ? Highlight : Normal);
        }

        // ---- blind arcs of the unit's search radars (arcs are from the mount forward; the hull heading is used) -----
        foreach (SensorRow s in unit.Sensors)
        {
            if (s.Letter != "R" || !s.On || s.ArcMin <= -180f && s.ArcMax >= 180f) continue;
            float from = unit.Heading + s.ArcMax - up, to = unit.Heading + s.ArcMin + 360f - up;
            dc.DrawGeometry(Fade(Hostile, 0.12f), null, Sector(cx, cy, 0f, radius, from, to));
        }

        // ---- radar horizon against the chosen target altitude ----------------------------------------------------------
        float horizonNm = float.NaN;
        if (win.ShowHorizon)
        {
            float targetY = win.TargetAltitudeFt / SensorPicture.FeetPerUnit;
            horizonNm = RadarCalculator.getRadarHorizon(unit.SensorHeightUnits, targetY, false, 0f) * SensorPicture.NmPerUnit;
            if (horizonNm > 0.1f)
            {
                float r = Math.Min(horizonNm, rangeNm * 1.4f) * pxPerNm;
                dc.DrawEllipse(null, PenOf(Fade(Emphasis, 0.8f), 1.5f), new Point(cx, cy), r, r);
                DrawCentered(dc, "horizon " + horizonNm.ToString("0") + " nm @ " + win.TargetAltitudeFt.ToString("0") + " ft", cx, cy + r + 2, 10, Emphasis);
            }
        }

        // ---- RWR and jam strobes ------------------------------------------------------------------------------------
        foreach (Strobe st in unit.Strobes)
        {
            float age = (float)(pic.Time - st.Time);
            float alpha = Math.Max(0.15f, 1f - age / 8f);
            Brush b = st.Jam ? Fade(Emphasis, alpha) : Fade(Hostile, alpha);
            float a = st.Bearing - up;
            dc.DrawLine(PenOf(b, 2.5f), Polar(cx, cy, radius * 0.72f, a), Polar(cx, cy, radius, a));
            Point label = Polar(cx, cy, radius * 0.68f, a);
            DrawCentered(dc, st.Jam ? "JAM" : "RWR", label.X, label.Y - 6, 9, b);
        }

        // ---- contacts: track estimates, fading with the age of the last fix ----------------------------------------------
        float memory = Math.Max(5f, Settings.MemorySeconds.Value);
        foreach (ContactRow cr in pic.Contacts)
        {
            Hit best = null;
            bool lock_ = false;
            foreach (Hit hit in cr.Hits)
            {
                if (hit.Unit != unit) continue;
                if (hit.Kind == Kind.FireControl && hit.Live) lock_ = true;
                bool ranged = !hit.BearingOnly && !float.IsNaN(hit.RangeM);
                if (!ranged)
                {
                    if (hit.Kind == Kind.Esm || hit.Kind == Kind.Passive)
                        DrawBearingLine(dc, cx, cy, radius, hit, pic.Time, up);
                    continue;
                }
                if (best == null || hit.FixTime > best.FixTime) best = hit;
            }
            if (best == null) continue;
            float ageS = (float)(pic.Time - best.FixTime);
            float alpha = best.Live ? Math.Max(0.55f, 1f - ageS / Math.Max(4f, best.MaxAge)) : Math.Max(0.12f, 0.5f * (1f - ageS / memory));
            float rNm = best.RangeM / 1852f;
            if (rNm > rangeNm * 1.02f) continue;
            Point p = Polar(cx, cy, rNm * pxPerNm, best.Bearing - up);
            Brush color = cr.Relation == 3 ? Hostile : cr.Relation == 1 ? Friendly : cr.Relation == 2 ? Neutral : Unknown;
            Brush c = Fade(color, alpha);
            dc.DrawRectangle(c, null, new Rect(p.X - 3.5f, p.Y - 3.5f, 7, 7));
            if (lock_) dc.DrawEllipse(null, PenOf(Hostile, 1.5f), p, 9, 9);
            Draw(dc, cr.Label + (lock_ ? " LOCK" : ""), p.X + 7, p.Y - 6, 10, Fade(lock_ ? Hostile : Highlight, Math.Max(0.35f, alpha)));
        }

        // ---- own ship ------------------------------------------------------------------------------------------------
        dc.DrawEllipse(Highlight, null, new Point(cx, cy), 3, 3);
        dc.DrawLine(PenOf(Highlight, 1.5f), new Point(cx, cy), Polar(cx, cy, 22f, unit.Heading - up));
        Draw(dc, unit.Name + "   hdg " + Fmt.Compass(unit.Heading) + "   " + unit.SpeedKts.ToString("0") + " kt" +
                 (unit.Emcon ? "   EMCON" : ""), 6, 4, 11, Highlight);
        Draw(dc, win.HeadingUp ? "heading up" : "north up", 6, h - 16, 10, Normal);
        DrawRight(dc, "sensor height " + (unit.SensorHeightUnits * SensorPicture.FeetPerUnit).ToString("0") + " ft", w - 6, h - 16, 10, Normal);
    }

    private void DrawBearingLine(DrawingContext dc, float cx, float cy, float radius, Hit hit, double now, float up)
    {
        float age = (float)(now - hit.FixTime);
        float alpha = hit.Live ? 0.9f : Math.Max(0.1f, 0.4f * (1f - age / Math.Max(5f, Settings.MemorySeconds.Value)));
        Brush b = Fade(hit.Kind == Kind.Esm ? Blue : Green, alpha);
        float a = hit.Bearing - up;
        dc.DrawLine(PenOf(b, 1f), Polar(cx, cy, 0f, a), Polar(cx, cy, radius, a));
        Point p = Polar(cx, cy, radius * 0.93f, a);
        DrawCentered(dc, (hit.Kind == Kind.Esm ? "E" : "S") + hit.ContactId, p.X, p.Y - 5, 9, b);
    }

    internal static float SweepBearing(ObjectBase unit)
    {
        var systems = unit?._obp?._sensorSystems;
        if (systems == null) return float.NaN;
        foreach (SensorSystem s in systems)
        {
            if (s is SensorSystemRadar r && r._type == SensorSystemRadar.RadarType.Search && r.IsOn.Value && r._mountObject != null)
                return SensorPicture.Wrap360(r._mountObject.transform.eulerAngles.y);
        }
        return float.NaN;
    }
}

/// <summary>
/// The scope's decorative sweep (it follows the antenna mount of the first search radar that is on). Drawn pointing up once per
/// snapshot or size change; in between the host only turns it (a rotate transform), which costs no redraw.
/// </summary>
public sealed class ScopeSweep : Surface
{
    private readonly RotateTransform turn = new RotateTransform();
    private ScopeWindow Win => DataContext as ScopeWindow;

    public ScopeSweep()
    {
        Background = null;
        RenderTransform = turn;
    }

    internal override bool Animated => true;

    internal override void Animate()
    {
        ScopeWindow win = Win;
        UnitRow unit = win?.Subject();
        float sweep = unit == null ? float.NaN : ScopeSurface.SweepBearing(unit.Unit);
        if (float.IsNaN(sweep))
        {
            if (Visibility != Visibility.Hidden) Visibility = Visibility.Hidden;
            return;
        }
        if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
        float angle = sweep - (win.HeadingUp ? unit.Heading : 0f);
        if (Math.Abs(UnityEngine.Mathf.DeltaAngle(turn.Angle, angle)) > 0.05f) turn.Angle = angle;
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        ScopeWindow win = Win;
        UnitRow unit = win?.Subject();
        if (!SensorPicture.Current.InMission || !ScopeGeometry.Of(win, unit, w, h, out ScopeGeometry g)) return;
        turn.CenterX = g.Cx;
        turn.CenterY = g.Cy;
        dc.DrawGeometry(Fade(Green, 0.10f), null, Sector(g.Cx, g.Cy, 0f, g.Radius, -18f, 0f));
        dc.DrawLine(PenOf(Fade(Green, 0.55f), 1.2f), new Point(g.Cx, g.Cy), Polar(g.Cx, g.Cy, g.Radius, 0f));
    }
}

/// <summary>The scope's measuring cursor: bearing and range from own ship, on its own layer.</summary>
public sealed class ScopeCursor : Surface
{
    private Point cursor = new Point(-1, -1);
    private ScopeWindow Win => DataContext as ScopeWindow;

    public ScopeCursor() => Background = null;

    internal void MoveTo(Point p)
    {
        if (p.X < 0 && cursor.X < 0) return;
        cursor = p;
        InvalidateVisual();
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        if (cursor.X < 0) return;
        ScopeWindow win = Win;
        UnitRow unit = win?.Subject();
        if (!SensorPicture.Current.InMission || !ScopeGeometry.Of(win, unit, w, h, out ScopeGeometry g)) return;
        double dx = cursor.X - g.Cx, dy = g.Cy - cursor.Y;
        double distPx = Math.Sqrt(dx * dx + dy * dy);
        if (distPx > g.Radius + 10) return;
        float brg = SensorPicture.Wrap360(g.Up + (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI));
        float nm = (float)distPx / g.PxPerNm;
        dc.DrawLine(PenOf(Highlight, 1f), new Point(g.Cx, g.Cy), cursor);
        string t = "brg " + Fmt.Compass(brg) + "  rng " + nm.ToString("0.0") + " nm";
        float tx = cursor.X > w - 140 ? (float)cursor.X - 136 : (float)cursor.X + 10;
        float ty = cursor.Y < 20 ? (float)cursor.Y + 8 : (float)cursor.Y - 18;
        dc.DrawRectangle(Fade(Back, 0.85f), null, new Rect(tx - 2, ty - 1, 134, 15));
        Draw(dc, t, tx, ty, 10, Highlight);
    }
}
