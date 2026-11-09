using System;
using System.Collections.Generic;
using Noesis;
using SeaPower;
using SeaPowerSensorViews.Data;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>Radar scope window content: a small tool row (range, horizon, target altitude) over the scope.</summary>
public sealed class ScopeView : UserControl
{
    private readonly TextBlock rangeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0), MinWidth = 48 };
    private readonly TextBox altBox = new TextBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0), Width = 58 };
    private readonly CheckBox horizon = new CheckBox { Content = "Radar horizon", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox headingUp = new CheckBox { Content = "Heading up", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    private readonly CheckBox friends = new CheckBox { Content = "Own ships", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
    // The slider runs on a square-root scale: the useful altitudes (sea-skimmers, helicopters, strike aircraft) sit at the low end
    // and a linear 0..60000 ft slider 130 px wide moved by 460 ft per pixel. Exact values go into the box.
    private const float MaxAltitudeFt = 60000f, SliderSteps = 1000f;
    private readonly Slider altitude = new Slider { Minimum = 0, Maximum = SliderSteps, Value = 0, Width = 130, VerticalAlignment = VerticalAlignment.Center, SmallChange = 5, LargeChange = 50 };
    private bool syncing;

    private static float AltitudeOf(double slider) { double t = slider / SliderSteps; return (float)(MaxAltitudeFt * t * t); }
    private static double SliderOf(float altitudeFt) => SliderSteps * Math.Sqrt(Math.Max(0f, Math.Min(MaxAltitudeFt, altitudeFt)) / MaxAltitudeFt);

    /// <summary>Takes the number typed into the altitude box (feet).</summary>
    private void CommitAltitude()
    {
        ScopeWindow w = Win;
        if (w == null || syncing) return;
        if (float.TryParse(altBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ft) && !float.IsNaN(ft))
            w.TargetAltitudeFt = Math.Max(0f, Math.Min(100000f, ft));
        Sync();
    }

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
        bar.Children.Add(altBox);
        bar.Children.Add(new TextBlock { Text = "ft", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        bar.Children.Add(headingUp);
        bar.Children.Add(friends);
        altBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CommitAltitude(); e.Handled = true; } };
        altBox.LostFocus += (s, e) => CommitAltitude();
        friends.Click += (s, e) => { if (Win != null && !syncing) Win.ShowFriends = friends.IsChecked == true; Registry.RefreshAll(); };
        horizon.Click += (s, e) => { if (Win != null && !syncing) Win.ShowHorizon = horizon.IsChecked == true; Registry.RefreshAll(); };
        headingUp.Click += (s, e) => { if (Win != null && !syncing) Win.HeadingUp = headingUp.IsChecked == true; Registry.RefreshAll(); };
        altitude.ValueChanged += (s, e) =>
        {
            if (Win == null || syncing) return;
            float ft = AltitudeOf(altitude.Value);
            Win.TargetAltitudeFt = ft < 1000f ? (float)Math.Round(ft / 10f) * 10f : (float)Math.Round(ft / 100f) * 100f;
            altBox.Text = Win.TargetAltitudeFt.ToString("0");
            Registry.RefreshAll();
        };
        Grid.SetRow(bar, 0);
        // Three layers: the picture (redrawn with each snapshot), the sweep (drawn once, then only turned) and the measuring
        // cursor (redrawn on mouse moves). The sweep used to redraw the whole scope 25 times a second.
        var scope = new ScopeSurface();
        var blips = new ScopeBlips { IsHitTestVisible = false };
        var sweep = new ScopeSweep { IsHitTestVisible = false };
        var cursorLayer = new ScopeCursor { IsHitTestVisible = false };
        scope.CursorLayer = cursorLayer;
        var stack = new Grid();
        stack.Children.Add(scope);
        stack.Children.Add(blips);
        stack.Children.Add(sweep);
        stack.Children.Add(cursorLayer);
        Grid.SetRow(stack, 1);
        root.Children.Add(bar);
        root.Children.Add(stack);
        Content = root;
        SizeProbe.Watch(this, "scope");
        Loaded += (s, e) => Sync();
        // The wheel on the scope changes the range without touching this row: follow the window's own change event.
        DataContextChanged += (s, e) =>
        {
            if (watched != null) watched.Changed -= OnWindowChanged;
            watched = Win;
            if (watched != null) watched.Changed += OnWindowChanged;
            Sync();
        };
    }

    private ScopeWindow watched;
    private void OnWindowChanged() { if (!syncing) Sync(); }

    private ScopeWindow Win => DataContext as ScopeWindow;

    private void Sync()
    {
        ScopeWindow w = Win;
        if (w == null) return;
        syncing = true;
        try
        {
            rangeText.Text = ScopeWindow.Ranges[w.RangeIndex].ToString("0") + " nm";
            altitude.Value = (float)SliderOf(w.TargetAltitudeFt);
            altBox.Text = w.TargetAltitudeFt.ToString("0");
            horizon.IsChecked = w.ShowHorizon;
            headingUp.IsChecked = w.HeadingUp;
            friends.IsChecked = w.ShowFriends;
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
        g.RangeNm = win.RangeShown;
        g.PxPerNm = g.Radius / g.RangeNm;
        g.Up = win.HeadingUp ? unit.Heading : 0f;      // compass bearing that points up
        return true;
    }
}

/// <summary>
/// What the scope may show: only what the unit's own radar holds, and only while a search radar radiates. Contacts come from the game's
/// radar tracks. The game makes no tracks of the player's own task force, so its other ships go through the game's own search-radar
/// tests here (same range, horizon, terrain, arc, altitude, role, jamming), once a second of game time. Display only.
/// </summary>
internal static class ScopeRadar
{
    private const double OwnCheckSeconds = 1.0;
    private static readonly HashSet<int> own = new HashSet<int>();
    private static int ownFor = -1;
    private static double ownAt = -1.0;

    /// <summary>This radar system is switched on, working and able to make contacts.</summary>
    internal static bool Radiating(SensorSystemRadar r) => r.CanProduceContacts && r._radar != null && r._mountObject != null;

    private static bool IsSearch(SensorSystemRadar r) => r._type == SensorSystemRadar.RadarType.Search || r._type == SensorSystemRadar.RadarType.DirectedSearch;

    /// <summary>A search radar of the unit (turning or directed) is radiating.</summary>
    internal static bool On(ObjectBase unit)
    {
        var systems = unit?._obp?._sensorSystems;
        if (systems == null) return false;
        foreach (SensorSystem s in systems)
            if (s is SensorSystemRadar r && IsSearch(r) && Radiating(r)) return true;
        return false;
    }

    /// <summary>The unit's newest live radar track of the contact; null when its radars do not hold it or no search radar is on.</summary>
    internal static Hit Best(ContactRow cr, UnitRow unit)
    {
        if (!On(unit.Unit)) return null;
        Hit best = null;
        foreach (Hit h in cr.Hits)
            if (h.Unit == unit && h.Live && (h.Kind == Kind.Radar || h.Kind == Kind.FireControl) && !h.BearingOnly && !float.IsNaN(h.RangeM)
                && (best == null || h.FixTime > best.FixTime)) best = h;
        return best;
    }

    internal static bool Locked(ContactRow cr, UnitRow unit)
    {
        foreach (Hit h in cr.Hits)
            if (h.Unit == unit && h.Kind == Kind.FireControl && h.Live) return true;
        return false;
    }

    /// <summary>The unit's search radar holds this other ship of the player's task force.</summary>
    internal static bool SeesOwn(UnitRow unit, UnitRow other, SensorPicture pic)
    {
        if (ReferenceEquals(other, unit) || other.Unit == null || !On(unit.Unit)) return false;
        if (unit.Uid != ownFor || pic.Time < ownAt || pic.Time - ownAt >= OwnCheckSeconds)
        {
            ownFor = unit.Uid;
            ownAt = pic.Time;
            own.Clear();
            foreach (UnitRow o in pic.Units)
                if (!ReferenceEquals(o, unit) && o.Unit != null && Detects(unit.Unit, o.Unit)) own.Add(o.Uid);
        }
        return own.Contains(other.Uid);
    }

    private static bool Detects(ObjectBase home, ObjectBase target)
    {
        try
        {
            foreach (SensorSystem s in home._obp._sensorSystems)
            {
                if (!(s is SensorSystemRadar r) || !IsSearch(r) || !Radiating(r)) continue;
                var pos = r.SensorPosition;
                if (pos.y < 0f && (!(home is Submarine sub) || sub.isBelowPeriscopeDepth())) continue;
                Radar radar = r._radar;
                RadarParameters rp = radar._rp;
                if (!radar.IsDetectableTarget_Unconditional(target, true, true) || !radar.IsDetectableTarget_Conditional(target)) continue;
                // the arguments of the game's SearchRadar.createRadarEchoes
                if (RadarCalculator.createRadarEcho(pos, target, rp._waveLength, rp._gainFactor, rp._peakPower, home._ECMSystemsTryingToJamMe, rp._frequency, home._crew,
                        out SensorEcho _, false, rp._jamResistance, true, null, false, RadarCalculator.IsAirborneSensor(home) ? null : rp, pos.y, r._lookDownMultiplier < 1f))
                    return true;
            }
        }
        catch (Exception ex) { ModLog.Debug("Scope own-ship radar check: " + ex.Message); }
        return false;
    }
}

public sealed class ScopeSurface : Surface
{
    internal override bool Retro => Settings.RetroLook.Value;
    private ScopeWindow Win => DataContext as ScopeWindow;
    internal ScopeCursor CursorLayer;
    private int strobeVersion = -1;

    public ScopeSurface()
    {
        MouseMove += (s, e) => CursorLayer?.MoveTo(e.GetPosition(this));
        MouseLeave += (s, e) => CursorLayer?.MoveTo(new Point(-1, -1));
        MouseLeftButtonDown += OnPick;
        MouseWheel += (s, e) =>
        {
            Win?.StepRange(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
            Registry.RefreshAll();
        };
    }


    /// <summary>Where the scope draws a contact (the unit's own radar track, the map marker's place when the game plots it); false when the radar does not hold it or it is out of range.</summary>
    private static bool ContactPoint(ContactRow cr, UnitRow unit, in ScopeGeometry g, out Point p)
    {
        p = default;
        Hit best = ScopeRadar.Best(cr, unit);
        if (best == null) return false;
        float rNm = best.RangeM / 1852f, bNow = best.Bearing;
        if (cr.HasPlot) GeoMath.BearingRange(unit.Unit.getGeoPosition(), cr.Plot, out bNow, out rNm);
        if (rNm > g.RangeNm * 1.02f) return false;
        p = Polar(g.Cx, g.Cy, rNm * g.PxPerNm, bNow - g.Up);
        return true;
    }

    /// <summary>A click on the scope picks the contact or own ship nearest to the pointer (within 14 px) and brings the game's camera to it.</summary>
    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        ScopeWindow win = Win;
        SensorPicture pic = SensorPicture.Current;
        UnitRow unit = win?.Subject();
        if (win == null || unit == null || !pic.InMission) { ModLog.Debug("Scope click: no window, unit or mission."); return; }
        if (!ScopeGeometry.Of(win, unit, (float)ActualWidth, (float)ActualHeight, out ScopeGeometry g)) { ModLog.Debug("Scope click: no geometry."); return; }
        Point click = e.GetPosition(this);
        ContactRow pick = null;
        UnitRow pickUnit = null;
        float bestD = 14f * 14f, nearest = float.MaxValue;
        int candidates = 0;
        foreach (ContactRow cr in pic.Contacts)
        {
            if (!ContactPoint(cr, unit, g, out Point p)) continue;
            candidates++;
            float d = (p.X - (float)click.X) * (p.X - (float)click.X) + (p.Y - (float)click.Y) * (p.Y - (float)click.Y);
            nearest = Math.Min(nearest, d);
            if (d < bestD) { bestD = d; pick = cr; pickUnit = null; }
        }
        if (win.ShowFriends)       // the other own ships the radar holds, drawn as diamonds
        {
            GeoPosition here = unit.Unit.getGeoPosition();
            foreach (UnitRow other in pic.Units)
            {
                if (!ScopeRadar.SeesOwn(unit, other, pic)) continue;
                GeoMath.BearingRange(here, other.Unit.getGeoPosition(), out float brg, out float nm);
                if (nm > g.RangeNm * 1.02f) continue;
                Point fp = Polar(g.Cx, g.Cy, nm * g.PxPerNm, brg - g.Up);
                candidates++;
                float d = (fp.X - (float)click.X) * (fp.X - (float)click.X) + (fp.Y - (float)click.Y) * (fp.Y - (float)click.Y);
                nearest = Math.Min(nearest, d);
                if (d < bestD) { bestD = d; pickUnit = other; pick = null; }
            }
        }
        ModLog.Debug("Scope click at " + click.X.ToString("0") + "," + click.Y.ToString("0") + ": " + candidates + " on the scope, nearest " +
                     (nearest == float.MaxValue ? "-" : Math.Sqrt(nearest).ToString("0") + " px") + " -> " +
                     (pick != null ? pick.Label : pickUnit != null ? pickUnit.Name : "nothing"));
        if (pick != null)
        {
            win.SelContact = pick;
            Jump.ToContact(pick, unit);
        }
        else if (pickUnit != null)
        {
            win.SelContact = null;
            Jump.ToUnit(pickUnit);
        }
        else return;
        e.Handled = true;
        Registry.RefreshAll();
    }

    /// <summary>Between snapshots the picture is redrawn only when a radar warning or jam strobe came or went.</summary>
    internal override bool Animated => true;
    internal override void Animate()
    {
        if (Win != null && Win.StepZoom()) InvalidateVisual();     // the range is gliding to its new value
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
            DrawCentered(dc, (win.RangeTarget * i / 4f).ToString("0.#"), cx + 8, cy - r - 12, 9, Normal);
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
                string note = "horizon " + horizonNm.ToString("0") + " nm @ " + win.TargetAltitudeFt.ToString("0") + " ft";
                if (horizonNm <= rangeNm)
                {
                    float r = horizonNm * pxPerNm;
                    dc.DrawEllipse(null, PenOf(Fade(Emphasis, 0.8f), 1.5f), new Point(cx, cy), r, r);
                    DrawCentered(dc, note, cx, cy + r + 2, 10, Emphasis);
                }
                else   // beyond the scope: no ring (it used to run out of the window), only the number
                    DrawCentered(dc, note + " (beyond this range)", cx, h - 32, 10, Emphasis);
            }
        }

        // The scope shows what the unit's radar holds and nothing while no search radar is on. With a turning antenna the blip layer
        // paints it; without one (a directed radar, or the setting is off) this layer does, always current.
        bool radarOn = ScopeRadar.On(unit.Unit);
        bool drawnHere = radarOn && (!Settings.RealisticSweep.Value || float.IsNaN(SweepBearing(unit.Unit)));

        // ---- the other own units the radar holds -------------------------------------------------------------------------
        if (drawnHere && win.ShowFriends)
        {
            GeoPosition here = unit.Unit.getGeoPosition();
            foreach (UnitRow other in pic.Units)
            {
                if (!ScopeRadar.SeesOwn(unit, other, pic)) continue;
                GeoMath.BearingRange(here, other.Unit.getGeoPosition(), out float brg, out float nm);
                if (nm > rangeNm * 1.02f) continue;
                DrawOwnShip(dc, Polar(cx, cy, nm * pxPerNm, brg - up), other.Name, 1f);
            }
        }

        // ---- RWR and jam strobes ------------------------------------------------------------------------------------
        if (radarOn) foreach (Strobe st in unit.Strobes)
        {
            float age = (float)(pic.Time - st.Time);
            float alpha = Math.Max(0.15f, 1f - age / 8f);
            Brush b = st.Jam ? Fade(Emphasis, alpha) : Fade(Hostile, alpha);
            float a = st.Bearing - up;
            dc.DrawLine(PenOf(b, 2.5f), Polar(cx, cy, radius * 0.72f, a), Polar(cx, cy, radius, a));
            Point label = Polar(cx, cy, radius * 0.68f, a);
            DrawCentered(dc, st.Jam ? "JAM" : "RWR", label.X, label.Y - 6, 9, b);
        }

        // ---- radar contacts, when this layer draws them -------------------------------------------------------------------
        if (drawnHere) foreach (ContactRow cr in pic.Contacts)
        {
            Hit best = ScopeRadar.Best(cr, unit);
            if (best == null) continue;
            bool lock_ = ScopeRadar.Locked(cr, unit);
            float ageS = (float)(pic.Time - best.FixTime);
            float alpha = Math.Max(0.55f, 1f - ageS / Math.Max(4f, best.MaxAge));
            float rNm = best.RangeM / 1852f, bNow = best.Bearing;
            if (cr.HasPlot)   // the map marker's place, from the unit as it is now: lines and marks agree with the map
                GeoMath.BearingRange(unit.Unit.getGeoPosition(), cr.Plot, out bNow, out rNm);
            if (rNm > rangeNm * 1.02f) continue;
            Point p = Polar(cx, cy, rNm * pxPerNm, bNow - up);
            Brush color = cr.Relation == 3 ? Hostile : cr.Relation == 1 ? Friendly : cr.Relation == 2 ? Neutral : Unknown;
            Brush c = Fade(color, alpha);
            dc.DrawRectangle(c, null, new Rect(p.X - 3.5f, p.Y - 3.5f, 7, 7));
            if (lock_) dc.DrawEllipse(null, PenOf(Hostile, 1.5f), p, 9, 9);
            Draw(dc, cr.Label + (lock_ ? " LOCK" : ""), p.X + 7, p.Y - 6, 10, Fade(lock_ ? Hostile : Highlight, Math.Max(0.35f, alpha)));
        }

        // ---- the picked contact -------------------------------------------------------------------------------------
        if (win.SelContact != null && pic.Contacts.Contains(win.SelContact) && ContactPoint(win.SelContact, unit, g, out Point sp))
            dc.DrawEllipse(null, PenOf(Highlight, 1.5f), sp, 12, 12);

        // ---- own ship ------------------------------------------------------------------------------------------------
        dc.DrawEllipse(Highlight, null, new Point(cx, cy), 3, 3);
        dc.DrawLine(PenOf(Highlight, 1.5f), new Point(cx, cy), Polar(cx, cy, 22f, unit.Heading - up));
        Draw(dc, unit.Name + "   hdg " + Fmt.Compass(unit.Heading) + "   " + unit.SpeedKts.ToString("0") + " kt" +
                 (unit.Emcon ? "   EMCON" : ""), 6, 4, 11, Highlight);
        if (!radarOn)   // no search radar is on: the scope is empty, so say why
        {
            DrawCentered(dc, "SEARCH RADAR OFF", cx, cy + radius * 0.45f, 14, Emphasis);
            DrawCentered(dc, "no radar picture", cx, cy + radius * 0.45f + 18f, 10, Normal);
        }
        Draw(dc, win.HeadingUp ? "heading up" : "north up", 6, h - 16, 10, Normal);
        DrawRight(dc, "sensor height " + (unit.SensorHeightUnits * SensorPicture.FeetPerUnit).ToString("0") + " ft", w - 6, h - 16, 10, Normal);
    }

    internal static float SweepBearing(ObjectBase unit)
    {
        var systems = unit?._obp?._sensorSystems;
        if (systems == null) return float.NaN;
        foreach (SensorSystem s in systems)
        {
            if (s is SensorSystemRadar r && r._type == SensorSystemRadar.RadarType.Search && ScopeRadar.Radiating(r))
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
    internal override bool Retro => Settings.RetroLook.Value;
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
        // The trail behind the line: 1 degree slices whose brightness dies away smoothly (quadratic) over the trail, drawn once and then
        // only turned. It used to be one flat wedge, or six coarse steps with visible edges. The slices overlap by half a degree so no
        // seams show between them.
        bool retro = Settings.RetroLook.Value;
        int slices = retro ? 70 : 50;
        float start = retro ? 0.34f : 0.16f;
        for (int i = 0; i < slices; i++)
        {
            float t = 1f - (float)i / slices;
            dc.DrawGeometry(Fade(Green, start * t * t / 2.2f), null, Sector(g.Cx, g.Cy, 0f, g.Radius, -(i + 1.5f), -(float)i));
        }
        dc.DrawLine(PenOf(Fade(Green, retro ? 0.95f : 0.6f), retro ? 1.8f : 1.2f), new Point(g.Cx, g.Cy), Polar(g.Cx, g.Cy, g.Radius, 0f));
    }
}

/// <summary>
/// The scope's radar contacts as a real radar paints them: a contact appears (at its position then) when the sweep passes over it and
/// fades until the next pass. Own layer, redrawn only when the sweep has turned a few degrees or the picture changed. Off with the
/// setting "Radar scope: blips appear when the sweep passes" (the contacts are then drawn by the scope's own layer, always current).
/// Only what the radar holds is painted; a blip the radar no longer holds goes at the next pass, and all go when the radar is switched off.
/// </summary>
public sealed class ScopeBlips : Surface
{
    internal override bool Retro => Settings.RetroLook.Value;
    private struct Blip { public float Brg, Rng; public int Pass; }

    private readonly Dictionary<int, Blip> blips = new Dictionary<int, Blip>();
    private readonly Dictionary<int, Blip> ownBlips = new Dictionary<int, Blip>();     // the other own units the radar holds, by unit id
    private readonly List<int> lost = new List<int>();
    private int pass;
    private float prevSweep = float.NaN, drawnSweep = float.NaN, drawnRange = float.NaN;
    private int uid = -1, version = -1;
    private ScopeWindow Win => DataContext as ScopeWindow;

    public ScopeBlips() => Background = null;

    internal override bool Animated => true;

    /// <summary>Where the contact is now (bearing from the unit, nm): the plotted position when the game has one, else the radar's own fix.</summary>
    private static bool Now(ContactRow cr, UnitRow unit, out float brg, out float rng)
    {
        brg = rng = 0f;
        Hit best = ScopeRadar.Best(cr, unit);
        if (best == null) return false;
        brg = best.Bearing; rng = best.RangeM / 1852f;
        if (cr.HasPlot) GeoMath.BearingRange(unit.Unit.getGeoPosition(), cr.Plot, out brg, out rng);
        return true;
    }

    private bool ClearBlips()
    {
        if (blips.Count == 0 && ownBlips.Count == 0) return false;
        blips.Clear();
        ownBlips.Clear();
        return true;
    }

    /// <summary>The sweep passed over these blips without painting them again: the radar no longer holds them.</summary>
    private bool DropUnpainted(Dictionary<int, Blip> from, float turned)
    {
        lost.Clear();
        foreach (var kv in from)
            if (kv.Value.Pass != pass && SensorPicture.Wrap360(kv.Value.Brg - prevSweep) <= turned) lost.Add(kv.Key);
        foreach (int id in lost) from.Remove(id);
        return lost.Count > 0;
    }

    internal override void Animate()
    {
        ScopeWindow win = Win;
        SensorPicture pic = SensorPicture.Current;
        UnitRow unit = win?.Subject();
        if (win == null || unit == null || !pic.InMission || !Settings.RealisticSweep.Value)
        {
            if (ClearBlips()) InvalidateVisual();
            prevSweep = float.NaN;
            return;
        }
        if (unit.Uid != uid) { ClearBlips(); uid = unit.Uid; prevSweep = float.NaN; }
        float sweep = ScopeSurface.SweepBearing(unit.Unit);
        bool changed = version != pic.Version && float.IsNaN(sweep);
        if (!float.IsNaN(sweep))
        {
            if (!float.IsNaN(prevSweep))
            {
                float turned = SensorPicture.Wrap360(sweep - prevSweep);       // clockwise since the last step
                if (turned > 0f && turned < 180f)
                {
                    pass++;
                    foreach (ContactRow cr in pic.Contacts)
                    {
                        if (!Now(cr, unit, out float brg, out float rng)) continue;
                        if (SensorPicture.Wrap360(brg - prevSweep) <= turned)
                        {
                            blips[cr.Id] = new Blip { Brg = brg, Rng = rng, Pass = pass };      // the sweep is on it: painted at its position now
                            changed = true;
                        }
                    }
                    // the other own ships go by the same rule as any contact: painted only when this radar holds them
                    GeoPosition here = unit.Unit.getGeoPosition();
                    foreach (UnitRow other in pic.Units)
                    {
                        if (!ScopeRadar.SeesOwn(unit, other, pic)) continue;
                        GeoMath.BearingRange(here, other.Unit.getGeoPosition(), out float ownBrg, out float ownRng);
                        if (SensorPicture.Wrap360(ownBrg - prevSweep) > turned) continue;
                        ownBlips[other.Uid] = new Blip { Brg = ownBrg, Rng = ownRng, Pass = pass };
                        changed = true;
                    }
                    if (DropUnpainted(blips, turned)) changed = true;
                    if (DropUnpainted(ownBlips, turned)) changed = true;
                }
            }
            prevSweep = sweep;
            if (float.IsNaN(drawnSweep) || Math.Abs(UnityEngine.Mathf.DeltaAngle(sweep, drawnSweep)) > 4f) changed = true;
        }
        else
        {
            if (ClearBlips()) changed = true;      // radar off: nothing stays on the scope
            if (!float.IsNaN(drawnSweep)) { changed = true; prevSweep = float.NaN; }
        }
        if (!float.IsNaN(drawnRange) && Math.Abs(win.RangeShown - drawnRange) > 0.0001f) changed = true;   // the range glides
        if (changed)
        {
            drawnRange = win.RangeShown;
            version = pic.Version;
            drawnSweep = sweep;
            InvalidateVisual();
        }
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        ScopeWindow win = Win;
        SensorPicture pic = SensorPicture.Current;
        UnitRow unit = win?.Subject();
        if (!Settings.RealisticSweep.Value || unit == null || !pic.InMission || !ScopeGeometry.Of(win, unit, w, h, out ScopeGeometry g)) return;
        float sweep = ScopeSurface.SweepBearing(unit.Unit);
        foreach (ContactRow cr in pic.Contacts)
        {
            if (!blips.TryGetValue(cr.Id, out Blip b)) continue;
            if (b.Rng > g.RangeNm * 1.02f) continue;
            float alpha = Brightness(sweep, b);
            Point p = Polar(g.Cx, g.Cy, b.Rng * g.PxPerNm, b.Brg - g.Up);
            Brush color = cr.Relation == 3 ? Hostile : cr.Relation == 1 ? Friendly : cr.Relation == 2 ? Neutral : Unknown;
            dc.DrawRectangle(Fade(color, alpha), null, new Rect(p.X - 3.5f, p.Y - 3.5f, 7, 7));
            bool lock_ = ScopeRadar.Locked(cr, unit);
            if (lock_) dc.DrawEllipse(null, PenOf(Hostile, 1.5f), p, 9, 9);
            Draw(dc, cr.Label + (lock_ ? " LOCK" : ""), p.X + 7, p.Y - 6, 10, Fade(lock_ ? Hostile : Highlight, Math.Max(0.3f, alpha)));
        }
        if (!win.ShowFriends) return;
        foreach (UnitRow other in pic.Units)
        {
            if (ReferenceEquals(other, unit) || !ownBlips.TryGetValue(other.Uid, out Blip own) || own.Rng > g.RangeNm * 1.02f) continue;
            DrawOwnShip(dc, Polar(g.Cx, g.Cy, own.Rng * g.PxPerNm, own.Brg - g.Up), other.Name, Brightness(sweep, own));
        }
    }

    /// <summary>Full where the sweep just passed, fading to a faint memory a whole turn later.</summary>
    private static float Brightness(float sweep, in Blip b) =>
        float.IsNaN(sweep) ? 0.3f : Math.Max(0.12f, 1f - 0.88f * SensorPicture.Wrap360(sweep - b.Brg) / 360f);
}

/// <summary>The scope's measuring cursor: bearing and range from own ship, on its own layer.</summary>
public sealed class ScopeCursor : Surface
{
    internal override bool Retro => Settings.RetroLook.Value;
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
