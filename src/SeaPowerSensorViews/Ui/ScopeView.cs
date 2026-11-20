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
    private const float SliderSteps = 1000f;
    private readonly Slider altitude = new Slider { Minimum = 0, Maximum = SliderSteps, Value = 0, Width = 130, VerticalAlignment = VerticalAlignment.Center, SmallChange = 5, LargeChange = 50 };
    private bool syncing;
    private ScopeWindow watched;

    private static float AltitudeOf(double slider) { double t = slider / SliderSteps; return (float)(Settings.MaxTargetAltitudeFt * t * t); }
    private static double SliderOf(float altitudeFt) => SliderSteps * Math.Sqrt(Math.Max(0f, Math.Min(Settings.MaxTargetAltitudeFt, altitudeFt)) / Settings.MaxTargetAltitudeFt);

    private ScopeWindow Win => DataContext as ScopeWindow;

    /// <summary>Takes the number typed into the altitude box (feet).</summary>
    private void CommitAltitude()
    {
        ScopeWindow w = Win;
        if (w == null || syncing) return;
        if (float.TryParse(altBox.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ft) && !float.IsNaN(ft))
            w.TargetAltitudeFt = Math.Max(0f, Math.Min(Settings.MaxTargetAltitudeFt, ft));
        Sync();
    }

    public ScopeView()
    {
        var root = new Grid { Margin = new Thickness(6) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        WrapPanel bar = ToolRow();
        Grid.SetRow(bar, 0);
        // Four layers: the picture (redrawn with each snapshot), the blips, the sweep (drawn once, then only turned) and the
        // measuring cursor (redrawn on mouse moves).
        var scope = new ScopeSurface();
        var cursorLayer = new ScopeCursor { IsHitTestVisible = false };
        scope.CursorLayer = cursorLayer;
        var stack = new Grid();
        stack.Children.Add(scope);
        stack.Children.Add(new ScopeBlips { IsHitTestVisible = false });
        stack.Children.Add(new ScopeSweep { IsHitTestVisible = false });
        stack.Children.Add(cursorLayer);
        Grid.SetRow(stack, 1);
        root.Children.Add(bar);
        root.Children.Add(stack);
        Content = root;
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

    private WrapPanel ToolRow()
    {
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
        // Preview, not KeyDown: the TextBox uses up Enter itself, so KeyDown never saw it (VM test 2026-11-20). A click on
        // the scope does not take the logical focus, so the keyboard focus leaving is the second commit point.
        altBox.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Return) { CommitAltitude(); e.Handled = true; } };
        altBox.LostKeyboardFocus += (s, e) => CommitAltitude();
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
        return bar;
    }

    private void OnWindowChanged() { if (!syncing) Sync(); }

    private void Sync()
    {
        ScopeWindow w = Win;
        if (w == null) return;
        syncing = true;
        try
        {
            rangeText.Text = w.RangeTarget.ToString("0") + " nm";
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

/// <summary>Where the scope's circle sits in a surface of this size, shared by its layers.</summary>
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

    /// <summary>On the scope: inside the range ring, with a small margin so a contact on the ring is not lost.</summary>
    public bool Holds(float nm) => nm <= RangeNm * 1.02f;

    public Point At(float bearing, float nm) => Surface.Polar(Cx, Cy, nm * PxPerNm, bearing - Up);
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

    /// <summary>This radar system is switched on and working - the same switch the game's own sensor panel shows. Not
    /// CanProduceContacts: that also needs the unit's _hasControl and no pending switch request, and on aircraft (F-4, F-15C
    /// directed-search radars) it read "off" while the game showed the radar on.</summary>
    internal static bool Radiating(SensorSystemRadar r) => r.IsOn.Value && !r.Inoperable.Value && r._radar != null;

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
            if (h.Unit == unit && h.Live && (h.Kind == Kind.Radar || h.Kind == Kind.FireControl) && h.Ranged
                && (best == null || h.FixTime > best.FixTime)) best = h;
        return best;
    }

    /// <summary>Where the scope puts the contact (bearing, nm from the unit): the radar's fix, on the map marker when the game plots it.</summary>
    internal static bool Place(ContactRow cr, UnitRow unit, out Hit best, out float bearing, out float nm)
    {
        best = Best(cr, unit);
        bearing = nm = 0f;
        if (best == null) return false;
        bearing = best.Bearing;
        nm = Conv.Nm(best.RangeM);
        GeoMath.ToPlot(cr, unit.Unit.getGeoPosition(), ref bearing, ref nm);
        return true;
    }

    internal static bool Locked(ContactRow cr, UnitRow unit)
    {
        foreach (Hit h in cr.Hits)
            if (h.Unit == unit && h.Kind == Kind.FireControl && h.Live) return true;
        return false;
    }

    /// <summary>Where the unit's search radar sees this other ship of the player's task force; false when it does not hold it.</summary>
    internal static bool OwnPlace(UnitRow unit, UnitRow other, SensorPicture pic, out float bearing, out float nm)
    {
        bearing = nm = 0f;
        if (!SeesOwn(unit, other, pic)) return false;
        GeoMath.BearingRange(unit.Unit.getGeoPosition(), other.Unit.getGeoPosition(), out bearing, out nm);
        return true;
    }

    private static bool SeesOwn(UnitRow unit, UnitRow other, SensorPicture pic)
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
                if (radar._rp == null) continue;
                if (!radar.IsDetectableTarget_Unconditional(target, true, true) || !radar.IsDetectableTarget_Conditional(target)) continue;
                // The game's own per-radar overload: it fills in wavelength, power, jammers, crew, low-altitude and look-down from the radar
                // itself, and its signature stays the same across game builds (the long overload gained parameters in build 261107).
                var opts = new ObjectBase.SensorRangeOptions { IgnoreSelfScreeningJammers = true };
                if (RadarCalculator.createRadarEcho(pos, target, r, out SensorEcho _, false, opts))
                    return true;
            }
        }
        catch (Exception ex) { ModLog.Debug("Scope own-ship radar check: " + ex.Message); }
        return false;
    }

    /// <summary>The bearing of the antenna of the first turning search radar that radiates; NaN when there is none.</summary>
    internal static float SweepBearing(ObjectBase unit)
    {
        var systems = unit?._obp?._sensorSystems;
        if (systems == null) return float.NaN;
        foreach (SensorSystem s in systems)
        {
            if (s is SensorSystemRadar r && r._type == SensorSystemRadar.RadarType.Search && Radiating(r) && r._mountObject != null)
                return SensorPicture.Wrap360(r._mountObject.transform.eulerAngles.y);
        }
        return float.NaN;
    }
}

public sealed class ScopeSurface : TubeSurface
{
    private ScopeWindow Win => DataContext as ScopeWindow;
    internal ScopeCursor CursorLayer;
    private int strobeVersion = -1;

    /// <summary>A click picks what is within this many pixels of the pointer.</summary>
    private const float PickRadius = 14f;

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

    /// <summary>Where the scope draws a contact; false when the radar does not hold it or it is out of range.</summary>
    private static bool ContactPoint(ContactRow cr, UnitRow unit, in ScopeGeometry g, out Point p)
    {
        p = default;
        if (!ScopeRadar.Place(cr, unit, out _, out float bearing, out float nm) || !g.Holds(nm)) return false;
        p = g.At(bearing, nm);
        return true;
    }

    /// <summary>The nearest mark to a click so far (squared pixel distances).</summary>
    private sealed class Picker
    {
        public Point Click;
        public float Best, Nearest;
        public int Candidates;
        public ContactRow Contact;
        public UnitRow Unit;

        /// <summary>Counts a mark at <paramref name="p"/>; true when it is the closest yet within the pick radius.</summary>
        public bool Closer(Point p)
        {
            Candidates++;
            float d = (p.X - (float)Click.X) * (p.X - (float)Click.X) + (p.Y - (float)Click.Y) * (p.Y - (float)Click.Y);
            Nearest = Math.Min(Nearest, d);
            if (d >= Best) return false;
            Best = d;
            return true;
        }

        public void Log() =>
            ModLog.Debug("Scope click at " + Click.X.ToString("0") + "," + Click.Y.ToString("0") + ": " + Candidates + " on the scope, nearest " +
                         (Nearest == float.MaxValue ? "-" : Math.Sqrt(Nearest).ToString("0") + " px") + " -> " +
                         (Contact != null ? Contact.Label : Unit != null ? Unit.Name : "nothing"));
    }

    /// <summary>A click on the scope picks the contact or own ship nearest to the pointer and brings the game's camera to it.</summary>
    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        ScopeWindow win = Win;
        SensorPicture pic = SensorPicture.Current;
        UnitRow unit = win?.Subject();
        if (win == null || unit == null || !pic.InMission) { ModLog.Debug("Scope click: no window, unit or mission."); return; }
        if (!ScopeGeometry.Of(win, unit, (float)ActualWidth, (float)ActualHeight, out ScopeGeometry g)) { ModLog.Debug("Scope click: no geometry."); return; }
        Point click = e.GetPosition(this);
        var picker = new Picker { Click = click, Best = PickRadius * PickRadius, Nearest = float.MaxValue };
        foreach (ContactRow cr in pic.Contacts)
            if (ContactPoint(cr, unit, g, out Point p) && picker.Closer(p)) { picker.Contact = cr; picker.Unit = null; }
        if (win.ShowFriends)       // the other own ships the radar holds, drawn as diamonds
            foreach (UnitRow other in pic.Units)
                if (ScopeRadar.OwnPlace(unit, other, pic, out float brg, out float nm) && g.Holds(nm) && picker.Closer(g.At(brg, nm)))
                {
                    picker.Unit = other;
                    picker.Contact = null;
                }
        picker.Log();
        ContactRow pick = picker.Contact;
        UnitRow pickUnit = picker.Unit;
        if (pick == null && pickUnit == null) return;
        win.SelContact = pick;
        if (pick != null) Jump.ToContact(pick, unit);
        else Jump.ToUnit(pickUnit);
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
        strobeVersion = unit.StrobeVersion;
        DrawRings(dc, win, g);
        DrawRadarArcs(dc, unit, g);
        if (win.ShowHorizon) DrawHorizon(dc, win, unit, g, h);
        // The scope shows what the unit's radar holds and nothing while no search radar is on. With a turning antenna the blip layer
        // paints it; without one (a directed radar, or the setting is off) this layer does, always current.
        bool radarOn = ScopeRadar.On(unit.Unit);
        bool drawnHere = radarOn && (!Settings.RealisticSweep.Value || float.IsNaN(ScopeRadar.SweepBearing(unit.Unit)));
        if (drawnHere && win.ShowFriends) DrawOwnShips(dc, pic, unit, g);
        if (radarOn) DrawStrobes(dc, pic, unit, g);
        if (drawnHere) DrawContacts(dc, pic, unit, g);
        if (win.SelContact != null && pic.Contacts.Contains(win.SelContact) && ContactPoint(win.SelContact, unit, g, out Point sp))
            dc.DrawEllipse(null, PenOf(Highlight, 1.5f), sp, 12, 12);
        DrawUnit(dc, win, unit, g, w, h, radarOn);
    }

    private void DrawRings(DrawingContext dc, ScopeWindow win, in ScopeGeometry g)
    {
        Pen ring = PenOf(Lines, 1f);
        for (int i = 1; i <= 4; i++)
        {
            float r = g.Radius * i / 4f;
            dc.DrawEllipse(null, ring, new Point(g.Cx, g.Cy), r, r);
            DrawCentered(dc, (win.RangeTarget * i / 4f).ToString("0.#"), g.Cx + 8, g.Cy - r - 12, 9, Normal);
        }
        for (int b = 0; b < 360; b += 30)
        {
            float a = b - g.Up;
            dc.DrawLine(ring, Polar(g.Cx, g.Cy, g.Radius, a), Polar(g.Cx, g.Cy, g.Radius + (b % 90 == 0 ? 7f : 4f), a));
            Point p = Polar(g.Cx, g.Cy, g.Radius + 16f, a);
            string label = b == 0 ? "N" : b == 90 ? "E" : b == 180 ? "S" : b == 270 ? "W" : b.ToString();
            DrawCentered(dc, label, p.X, p.Y - 6, 10, b % 90 == 0 ? Highlight : Normal);
        }
    }

    /// <summary>
    /// Covered arcs of the unit's limited search radars (arcs are from the mount forward; the hull heading is used). The covered sector
    /// is shaded, not the blind one: a fighter's nose radar (-60..+60) used to paint the 240 degrees behind the aircraft, which read as
    /// coverage. All-round radars get no shading.
    /// </summary>
    private void DrawRadarArcs(DrawingContext dc, UnitRow unit, in ScopeGeometry g)
    {
        foreach (SensorRow s in unit.Sensors)
        {
            if (!s.SearchRadar || !s.On || s.ArcMin <= -180f && s.ArcMax >= 180f) continue;
            float from = unit.Heading + s.ArcMin - g.Up, to = unit.Heading + s.ArcMax - g.Up;
            dc.DrawGeometry(Fade(Green, 0.14f), null, Sector(g.Cx, g.Cy, 0f, g.Radius, from, to));
        }
    }

    /// <summary>The radar horizon against the chosen target altitude: a ring, or only the number when it lies beyond the range.</summary>
    private void DrawHorizon(DrawingContext dc, ScopeWindow win, UnitRow unit, in ScopeGeometry g, float h)
    {
        float targetY = win.TargetAltitudeFt / Conv.FeetPerUnit;
        float horizonNm = RadarCalculator.getRadarHorizon(unit.SensorHeightUnits, targetY, false, 0f) * Conv.NmPerUnit;
        if (horizonNm <= 0.1f) return;
        string note = "horizon " + horizonNm.ToString("0") + " nm @ " + win.TargetAltitudeFt.ToString("0") + " ft";
        if (horizonNm <= g.RangeNm)
        {
            float r = horizonNm * g.PxPerNm;
            dc.DrawEllipse(null, PenOf(Fade(Emphasis, 0.8f), 1.5f), new Point(g.Cx, g.Cy), r, r);
            DrawCentered(dc, note, g.Cx, g.Cy + r + 2, 10, Emphasis);
        }
        else
            DrawCentered(dc, note + " (beyond this range)", g.Cx, h - 32, 10, Emphasis);
    }

    private void DrawOwnShips(DrawingContext dc, SensorPicture pic, UnitRow unit, in ScopeGeometry g)
    {
        foreach (UnitRow other in pic.Units)
            if (ScopeRadar.OwnPlace(unit, other, pic, out float brg, out float nm) && g.Holds(nm))
                DrawOwnShip(dc, g.At(brg, nm), other.Name, 1f);
    }

    /// <summary>Radar warning and jam strobes, fading over their lifetime.</summary>
    private void DrawStrobes(DrawingContext dc, SensorPicture pic, UnitRow unit, in ScopeGeometry g)
    {
        foreach (Strobe st in unit.Strobes)
        {
            float age = (float)(pic.Time - st.Time);
            float alpha = Math.Max(0.15f, 1f - age / Strobe.Lifetime);
            Brush b = st.Jam ? Fade(Emphasis, alpha) : Fade(Hostile, alpha);
            float a = st.Bearing - g.Up;
            dc.DrawLine(PenOf(b, 2.5f), Polar(g.Cx, g.Cy, g.Radius * 0.72f, a), Polar(g.Cx, g.Cy, g.Radius, a));
            Point label = Polar(g.Cx, g.Cy, g.Radius * 0.68f, a);
            DrawCentered(dc, st.Jam ? "JAM" : "RWR", label.X, label.Y - 6, 9, b);
        }
    }

    /// <summary>The radar contacts at their latest position, fading with the age of the fix.</summary>
    private void DrawContacts(DrawingContext dc, SensorPicture pic, UnitRow unit, in ScopeGeometry g)
    {
        foreach (ContactRow cr in pic.Contacts)
        {
            if (!ScopeRadar.Place(cr, unit, out Hit best, out float bearing, out float nm) || !g.Holds(nm)) continue;
            float ageS = (float)(pic.Time - best.FixTime);
            float alpha = Math.Max(0.55f, 1f - ageS / Math.Max(4f, best.MaxAge));
            DrawContact(dc, g.At(bearing, nm), cr, ScopeRadar.Locked(cr, unit), alpha, Math.Max(0.35f, alpha));
        }
    }

    /// <summary>Own ship in the centre with its heading line, the header line and the footer notes.</summary>
    private void DrawUnit(DrawingContext dc, ScopeWindow win, UnitRow unit, in ScopeGeometry g, float w, float h, bool radarOn)
    {
        dc.DrawEllipse(Highlight, null, new Point(g.Cx, g.Cy), 3, 3);
        dc.DrawLine(PenOf(Highlight, 1.5f), new Point(g.Cx, g.Cy), Polar(g.Cx, g.Cy, 22f, unit.Heading - g.Up));
        Draw(dc, unit.Name + "   hdg " + Fmt.Compass(unit.Heading) + "   " + unit.SpeedKts.ToString("0") + " kt" +
                 (unit.Emcon ? "   EMCON" : ""), 6, 4, 11, Highlight);
        if (!radarOn)   // no search radar is on: the scope is empty, so say why
        {
            DrawCentered(dc, "SEARCH RADAR OFF", g.Cx, g.Cy - g.Radius * 0.45f, 14, Emphasis);
            DrawCentered(dc, "no radar picture", g.Cx, g.Cy - g.Radius * 0.45f + 18f, 10, Normal);
        }
        Draw(dc, win.HeadingUp ? "heading up" : "north up", 6, h - 16, 10, Normal);
        DrawRight(dc, "sensor height " + (unit.SensorHeightUnits * Conv.FeetPerUnit).ToString("0") + " ft", w - 6, h - 16, 10, Normal);
    }
}

/// <summary>
/// The scope's decorative sweep (it follows the antenna mount of the first search radar that is on). Drawn pointing up once per
/// snapshot or size change; in between the host only turns it (a rotate transform), which costs no redraw.
/// </summary>
public sealed class ScopeSweep : TubeSurface
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
        float sweep = unit == null ? float.NaN : ScopeRadar.SweepBearing(unit.Unit);
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
        // only turned. The slices overlap by half a degree so no seams show between them.
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
public sealed class ScopeBlips : TubeSurface
{
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

    /// <summary>The sweep turned <paramref name="turned"/> degrees clockwise from prevSweep: paints what lies in that slice. True when a blip changed.</summary>
    private bool Sweep(SensorPicture pic, UnitRow unit, float turned)
    {
        bool changed = false;
        pass++;
        foreach (ContactRow cr in pic.Contacts)
        {
            if (!ScopeRadar.Place(cr, unit, out _, out float brg, out float rng) || SensorPicture.Wrap360(brg - prevSweep) > turned) continue;
            blips[cr.Id] = new Blip { Brg = brg, Rng = rng, Pass = pass };      // the sweep is on it: painted at its position now
            changed = true;
        }
        // the other own ships go by the same rule as any contact: painted only when this radar holds them
        foreach (UnitRow other in pic.Units)
        {
            if (!ScopeRadar.OwnPlace(unit, other, pic, out float brg, out float rng) || SensorPicture.Wrap360(brg - prevSweep) > turned) continue;
            ownBlips[other.Uid] = new Blip { Brg = brg, Rng = rng, Pass = pass };
            changed = true;
        }
        changed |= DropUnpainted(blips, turned);
        changed |= DropUnpainted(ownBlips, turned);
        return changed;
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
        float sweep = ScopeRadar.SweepBearing(unit.Unit);
        bool changed = float.IsNaN(sweep) ? RadarOff() : Turn(pic, unit, sweep);
        changed |= version != pic.Version && float.IsNaN(sweep);
        if (!float.IsNaN(drawnRange) && Math.Abs(win.RangeShown - drawnRange) > 0.0001f) changed = true;   // the range glides
        if (!changed) return;
        drawnRange = win.RangeShown;
        version = pic.Version;
        drawnSweep = sweep;
        InvalidateVisual();
    }

    private bool Turn(SensorPicture pic, UnitRow unit, float sweep)
    {
        bool changed = false;
        if (!float.IsNaN(prevSweep))
        {
            float turned = SensorPicture.Wrap360(sweep - prevSweep);       // clockwise since the last step
            if (turned > 0f && turned < 180f) changed = Sweep(pic, unit, turned);
        }
        prevSweep = sweep;
        return changed || float.IsNaN(drawnSweep) || Math.Abs(UnityEngine.Mathf.DeltaAngle(sweep, drawnSweep)) > 4f;
    }

    /// <summary>Radar off: nothing stays on the scope.</summary>
    private bool RadarOff()
    {
        bool changed = ClearBlips();
        if (!float.IsNaN(drawnSweep)) { changed = true; prevSweep = float.NaN; }
        return changed;
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        ScopeWindow win = Win;
        SensorPicture pic = SensorPicture.Current;
        UnitRow unit = win?.Subject();
        if (!Settings.RealisticSweep.Value || unit == null || !pic.InMission || !ScopeGeometry.Of(win, unit, w, h, out ScopeGeometry g)) return;
        float sweep = ScopeRadar.SweepBearing(unit.Unit);
        foreach (ContactRow cr in pic.Contacts)
        {
            if (!blips.TryGetValue(cr.Id, out Blip b) || !g.Holds(b.Rng)) continue;
            float alpha = Brightness(sweep, b);
            DrawContact(dc, g.At(b.Brg, b.Rng), cr, ScopeRadar.Locked(cr, unit), alpha, Math.Max(0.3f, alpha));
        }
        if (!win.ShowFriends) return;
        foreach (UnitRow other in pic.Units)
        {
            if (ReferenceEquals(other, unit) || !ownBlips.TryGetValue(other.Uid, out Blip own) || !g.Holds(own.Rng)) continue;
            DrawOwnShip(dc, g.At(own.Brg, own.Rng), other.Name, Brightness(sweep, own));
        }
    }

    /// <summary>Full where the sweep just passed, fading to a faint memory a whole turn later.</summary>
    private static float Brightness(float sweep, in Blip b) =>
        float.IsNaN(sweep) ? 0.3f : Math.Max(0.12f, 1f - 0.88f * SensorPicture.Wrap360(sweep - b.Brg) / 360f);
}

/// <summary>The scope's measuring cursor: bearing and range from own ship, on its own layer.</summary>
public sealed class ScopeCursor : CursorSurface
{
    private ScopeWindow Win => DataContext as ScopeWindow;

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        if (Pointer.X < 0) return;
        ScopeWindow win = Win;
        UnitRow unit = win?.Subject();
        if (!SensorPicture.Current.InMission || !ScopeGeometry.Of(win, unit, w, h, out ScopeGeometry g)) return;
        double dx = Pointer.X - g.Cx, dy = g.Cy - Pointer.Y;
        double distPx = Math.Sqrt(dx * dx + dy * dy);
        if (distPx > g.Radius + 10) return;
        float brg = SensorPicture.Wrap360(g.Up + (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI));
        float nm = (float)distPx / g.PxPerNm;
        dc.DrawLine(PenOf(Highlight, 1f), new Point(g.Cx, g.Cy), Pointer);
        float tx = Pointer.X > w - 140 ? (float)Pointer.X - 136 : (float)Pointer.X + 10;
        float ty = Pointer.Y < 20 ? (float)Pointer.Y + 8 : (float)Pointer.Y - 18;
        DrawTag(dc, "brg " + Fmt.Compass(brg) + "  rng " + nm.ToString("0.0") + " nm", tx, ty, 134);
    }
}
