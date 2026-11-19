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
    private struct Heard { public string Name; public float Rel, RangeM, DepthFt, Signal; public bool Active; }
    private static readonly List<Heard> ownHeard = new List<Heard>();

    /// <summary>The listening sonar and the sea around it, the same for every target of one sample.</summary>
    private struct Ears
    {
        public UnitRow Unit;
        public ObjectBase Home;
        public UnityEngine.Vector3 Pos;
        public GeoPosition Here;
        public SonarParameters Sp;
        public SonarState St;
        public bool Active;
        public float Ambient, XLayer, Duct;
    }

    /// <summary>
    /// The game makes no sonar tracks of the player's own task force, so its other ships go through the game's sonar sums here, with the
    /// same filters, baffles, line of sight and detection threshold as a contact (the public per-contact sums; the game's batch model is private). Display only.
    /// </summary>
    private static void CollectOwnShips(UnitRow unit, SensorPicture pic, SensorSystemSonar sonar, SonarParameters sp, bool active, SonarState st)
    {
        if (sp == null) return;
        try
        {
            if (sonar.SensorPosition.y > 0f) return;      // not in the water
            Ears ears = Listen(unit, sonar, sp, active, st);
            foreach (UnitRow other in pic.Units)
            {
                ObjectBase t = other.Unit;
                if (ReferenceEquals(other, unit) || !Audible(t, sp) || !InEarshot(ears, t, out float rel, out float nm)) continue;
                bool shipToShip = ears.Home._type == ObjectBase.ObjectType.Vessel && t._type == ObjectBase.ObjectType.Vessel;
                float y = t.transform.position.y;
                float depth = y < 0f && t is Submarine ? -y * Conv.FeetPerUnit : float.NaN;
                AddEchoes(ears, t, other.Name, rel, nm, depth, shipToShip);
            }
            CollectOwnTorpedoes(ears);
        }
        catch (Exception ex) { ModLog.Debug("Sonar own-ship check: " + ex.Message); }
    }

    /// <summary>An own ship or submarine a sonar can hear at all (an intercept receiver only one that pings).</summary>
    private static bool Audible(ObjectBase t, SonarParameters sp)
    {
        if (t == null || !(t is Vessel || t is Submarine) || t._isUndetectable || t._sensorData == null || !t._hasControl) return false;
        return !sp._isActiveIntercept || t.IsActiveSonarsOn.Value;
    }

    private static Ears Listen(UnitRow unit, SensorSystemSonar sonar, SonarParameters sp, bool active, SonarState st)
    {
        var env = Singleton<SeaPower.Environment>.Instance;
        GeoPosition here = unit.Unit.getGeoPosition();
        float ambient = env.getOceanNoise(here);
        if (!float.IsNaN(st.OwnNoise))
            ambient = 10f * (float)Math.Log10(Math.Pow(10.0, ambient / 10.0) + Math.Pow(10.0, st.OwnNoise / 10.0));
        return new Ears
        {
            Unit = unit, Home = unit.Unit, Pos = sonar.SensorPosition, Here = here, Sp = sp, St = st, Active = active,
            Ambient = ambient, XLayer = env.getXLayerAttenuation(), Duct = env.getSurfaceDuct(here),
        };
    }

    /// <summary>The target is outside the baffles and in the line of sight of the listening unit.</summary>
    private static bool InEarshot(in Ears ears, ObjectBase t, out float rel, out float nm)
    {
        GeoMath.BearingRange(ears.Here, t.getGeoPosition(), out float brg, out nm);
        rel = GeoMath.Relative(ears.Unit.Heading, brg);
        if (GeoMath.InBaffles(ears.St.BaffleFrom, ears.St.BaffleTo, rel)) return false;
        float dist = (t.transform.position - ears.Home.transform.position).magnitude;
        return Singleton<UnitSensorCache>.Instance.UnitsHaveDirectlLOS(ears.Home, t, dist);
    }

    /// <summary>What the sonar hears of the target, passive and (when pinging) active, above the detection threshold.</summary>
    private static void AddEchoes(in Ears ears, ObjectBase t, string name, float rel, float nm, float depth, bool shipToShip)
    {
        SonarParameters sp = ears.Sp;
        float unityDist = (t.transform.position - ears.Pos).magnitude;
        if (t._sensorData._baseNoise >= 0f)
        {
            float signal = shipToShip ? ShipPassive(t, sp, ears.Ambient, ears.Duct, unityDist)
                                      : (float)SonarCalculator.GetPassiveSonarEcho(t, ears.Home, ears.Pos, sp._gain, ears.Ambient, ears.XLayer, ears.Duct)._signalValue;
            if (signal >= sp._detectionTreshold) ownHeard.Add(new Heard { Name = name, Rel = rel, DepthFt = depth, Signal = signal });
        }
        if (ears.Active && t._sensorData._maxTargetReflectionStrength >= 0f)
        {
            bool lf = sp._frequency == SonarParameters.SonarFrequency.LF, mf = sp._frequency == SonarParameters.SonarFrequency.MF;
            float signal = shipToShip ? ShipActive(t, sp, ears.Ambient, ears.Duct, unityDist)
                                      : (float)SonarCalculator.GetActiveSonarEcho(t, ears.Home, ears.Pos, sp._outputPower, sp._activeGain, ears.Ambient, ears.XLayer, ears.Duct, false, false, false, lf, lf || mf)._signalValue;
            if (signal >= sp._detectionTreshold) ownHeard.Add(new Heard { Name = name, Rel = rel, RangeM = nm * Conv.MetersPerNm, DepthFt = depth, Signal = signal, Active = true });
        }
    }

    /// <summary>
    /// The player's own torpedoes in the water. The game makes no sonar tracks of them either, so they go through the same public sonar
    /// sums as an enemy torpedo (its noise from speed and cavitation; an echo when pinging), with the same baffles, line of sight and threshold.
    /// Lets a player switch off the game's track display and steer a wire-guided torpedo by ear. Display only.
    /// </summary>
    private static void CollectOwnTorpedoes(in Ears ears)
    {
        ObjectBase home = ears.Home;
        if (!Settings.SonarOwnTorpedoes.Value || home._taskforce == null || !Singleton<ObjectsManager>.InstanceExists()) return;
        if (ears.Sp._isActiveIntercept) return;          // an intercept receiver hears pings only
        var weapons = Singleton<ObjectsManager>.Instance._listOfAllWeapons;
        for (int i = 0; i < weapons.Count; i++)
        {
            if (!(weapons[i] is Torpedo t) || t == null || t.IsDestroyed || t._launchPlatform == null || t._sensorData == null) continue;
            if (!ReferenceEquals(t._taskforce, home._taskforce) || t.transform.position.y >= 0f) continue;   // own side, in the water
            if (!InEarshot(ears, t, out float rel, out float nm)) continue;
            float depth = -t.transform.position.y * Conv.FeetPerUnit;
            string name = "own " + TorpedoName(t) + (ReferenceEquals(t._launchPlatform, home) ? "" : " (" + t._launchPlatform.getName() + ")");
            AddEchoes(ears, t, name, rel, nm, depth, false);
        }
    }

    private static string TorpedoName(Torpedo t)
    {
        try { string n = t.getName(); if (!string.IsNullOrEmpty(n)) return n; }
        catch (Exception) { /* a torpedo without its parameters loaded has no name: the plain word below */ }
        return "torpedo";
    }

    // The public per-contact sums return "not heard" for any surface ship listening to a surface ship, while the game's batch model, the one
    // enemy ships go through, does hear them. So for that pair the same sums without the block; two surface ships are both above the layer.
    private static float TransmissionLoss(float unityDist)
    {
        float nm = Math.Max(0.001f, unityDist * Conv.NmPerUnit);
        return 20f * (float)Math.Log10(nm * Conv.YardsPerNm) + nm * Globals._sonarEnvironmentParameters._absorptionFactor;
    }

    private static float ShipPassive(ObjectBase t, SonarParameters sp, float ambient, float duct, float unityDist)
    {
        SensorData d = t._sensorData;
        float noise = d._baseNoise + (t._powerSystemInUse?.AdditionalNoise ?? 0f)
            + Math.Max(0f, Math.Abs(t._velocityInKnots) - d._creepSpeedKts) * d._flowNoise + t._cavitationIntensity * d._cavitationNoise;
        return noise - ambient + sp._gain - TransmissionLoss(unityDist) + duct;
    }

    private static float ShipActive(ObjectBase t, SonarParameters sp, float ambient, float duct, float unityDist)
    {
        SensorData d = t._sensorData;
        float reflection = (d._minTargetReflectionStrength + d._maxTargetReflectionStrength) / 2f;
        return sp._outputPower + reflection - ambient + sp._activeGain - 2f * TransmissionLoss(unityDist) + duct;
    }

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
        Waterfall wf = win.FallOf(unit, name);
        SonarParameters sp = sonar._sonar?._sp;
        bool active = ReadState(st, unit, sonar, name, sp);
        ReadDepths(st, unit, sonar);
        st.Marks.Clear();
        ownHeard.Clear();
        if (st.On) CollectOwnShips(unit, pic, sonar, sp, active, st);
        foreach (Heard o in ownHeard)
            st.Marks.Add(new SonarState.Mark
            {
                Label = o.Name, Rel = o.Rel, RangeM = o.Active ? o.RangeM : float.NaN, Age = 0f, DepthFt = o.DepthFt, Active = o.Active,
            });
        if (st.On) MarkContacts(st, pic, unit, name);
        if (wf.LastVersion == pic.Version) return;
        wf.LastVersion = pic.Version;
        PushRow(wf, st, pic, unit, name);
    }

    /// <summary>Mode, baffles and own noise of the chosen sonar; returns whether it pings.</summary>
    private static bool ReadState(SonarState st, UnitRow unit, SensorSystemSonar sonar, string name, SonarParameters sp)
    {
        st.Valid = true;
        st.UnitName = unit.Name;
        st.SonarName = name;
        st.Heading = unit.Heading;
        st.SpeedKts = unit.SpeedKts;
        st.Towed = sonar.IsTowedSensor || sonar.IsVDS;
        // Off, broken or a towed array still on its winch: the sonar hears nothing, whatever tracks the game still keeps for a moment.
        st.On = sonar._sonar != null && sonar.CanProduceContacts && (sonar._parentTowedSystem == null || sonar._parentTowedSystem.TowedSystemState != TowedSystem.SystemState.SystemRetracted);
        bool active = sonar._sonar != null && sonar._sonar.IsActive.Value;
        st.Mode = (st.Towed ? "towed " : "") + (active ? "active" : "passive") + (sonar.IsOn.Value ? "" : ", off");
        bool baffles = sp != null && sp._baffles != UnityEngine.Vector2.zero && !st.Towed;
        st.BaffleFrom = baffles ? sp._baffles.x : float.NaN;
        st.BaffleTo = baffles ? sp._baffles.y : float.NaN;
        st.OwnNoise = OwnNoise(unit, sp);
        return active;
    }

    /// <summary>The same call the game's sonar makes for its own-ship noise term; NaN when the unit or sonar has no noise data.</summary>
    private static float OwnNoise(UnitRow unit, SonarParameters sp)
    {
        ObjectBase u = unit.Unit;
        SensorData sd = u._sensorData;
        if (sd == null || sp == null || sd._baseNoise < 0f) return float.NaN;
        return Compat.OwnNoiseDb(sd._baseNoise, sd._machineryRaftingGeneration, unit.SpeedKts, sd._flowNoise, sp._type,
            u._powerSystemInUse?.AdditionalNoise ?? 0f, u._cavitationIntensity, sd._cavitationNoise);
    }

    private static void ReadDepths(SonarState st, UnitRow unit, SensorSystemSonar sonar)
    {
        st.DepthFt = unit.DepthFt;
        GeoPosition geo = unit.Unit.getGeoPosition();
        try
        {
            float layer = Singleton<SeaPower.Environment>.Instance.getLayerHeight(geo);
            st.LayerFt = layer < 0f ? -layer * Conv.FeetPerUnit : float.NaN;
        }
        catch (Exception) { st.LayerFt = float.NaN; }   // no sound-speed profile at this place: no layer drawn
        float ay = sonar.SensorPosition.y;
        st.ArrayFt = ay < 0f ? -ay * Conv.FeetPerUnit : float.NaN;
        float bed = Terrain.HeightAt(geo);
        st.SeabedFt = bed < 0f ? -bed * Conv.FeetPerUnit : float.NaN;
    }

    /// <summary>The live tracks of this sonar: the hits of the unit's sonar of this name.</summary>
    private static IEnumerable<(ContactRow, Hit)> SonarHits(SensorPicture pic, UnitRow unit, string name)
    {
        foreach (ContactRow cr in pic.Contacts)
            foreach (Hit h in cr.Hits)
                if (h.Unit == unit && h.Live && (h.Kind == Kind.Passive || h.Kind == Kind.Active) && h.SensorName == name)
                    yield return (cr, h);
    }

    private static void MarkContacts(SonarState st, SensorPicture pic, UnitRow unit, string name)
    {
        foreach ((ContactRow cr, Hit h) in SonarHits(pic, unit, name))
        {
            float dep = float.IsNaN(h.AltitudeUnits) || h.AltitudeUnits > 0f ? float.NaN : -h.AltitudeUnits * Conv.FeetPerUnit;
            st.Marks.Add(new SonarState.Mark
            {
                Label = cr.Label, Rel = GeoMath.Relative(unit.Heading, h.Bearing), RangeM = h.RangeM, Age = (float)(pic.Time - h.FixTime),
                DepthFt = dep, DepthErrFt = h.AltitudeError * Conv.FeetPerUnit, Active = h.Kind == Kind.Active,
            });
        }
    }

    /// <summary>One waterfall row: the own ships and torpedoes heard, then the sonar's contacts.</summary>
    private static void PushRow(Waterfall wf, SonarState st, SensorPicture pic, UnitRow unit, string name)
    {
        var dots = wf.Push(float.IsNaN(st.OwnNoise) ? 0f : st.OwnNoise, unit.Heading);
        foreach (Heard o in ownHeard)
            dots.Add(new Waterfall.Dot { Bin = Bin(o.Rel), Width = 1, Level = Level(Math.Max(0f, Math.Min(1f, o.Signal / 30f))), Active = o.Active });
        if (!st.On) return;
        foreach ((ContactRow _, Hit h) in SonarHits(pic, unit, name))
        {
            float lvl = float.IsNaN(h.Signal) ? 0.6f : Math.Max(0f, Math.Min(1f, h.Signal / 30f));
            dots.Add(new Waterfall.Dot
            {
                Bin = Bin(GeoMath.Relative(unit.Heading, h.Bearing)),
                Width = (byte)Math.Max(1, Math.Min(8, (int)(h.BearingError / 2f) + 1)),
                Level = Level(lvl),
                Active = h.Kind == Kind.Active,
            });
        }
    }

    /// <summary>The waterfall column of a relative bearing (-180..180, starboard positive).</summary>
    private static short Bin(float rel) => (short)Math.Max(0, Math.Min(SonarWindow.Bins - 1, (int)((rel + 180f) / 360f * SonarWindow.Bins)));

    private static byte Level(float brightness) => (byte)(70 + 185 * brightness);
}

public sealed class SonarSurface : TubeSurface
{
    internal static event Action Changed;
    private SonarWindow Win => DataContext as SonarWindow;
    internal SonarCursor CursorLayer;
    private readonly Dictionary<int, StreamGeometryContext> batches = new Dictionary<int, StreamGeometryContext>();
    private readonly List<(int key, StreamGeometry geometry)> batchList = new List<(int, StreamGeometry)>();

    public SonarSurface()
    {
        MouseMove += (s, e) => CursorLayer?.MoveTo(e.GetPosition(this));
        MouseLeave += (s, e) => CursorLayer?.MoveTo(new Point(-1, -1));
    }

    /// <summary>The waterfall's rectangle in a surface of this size (shared with the cursor layer).</summary>
    internal static bool Layout(float w, float h, out Rect area, out float stripW)
    {
        stripW = Math.Min(250f, w * 0.3f);
        float ww = w - stripW - 24, hh = h - 34 - 26;
        area = new Rect(8, 34, Math.Max(0f, ww), Math.Max(0f, hh));
        return ww >= 60 && hh >= 60;
    }

    private static void AddRect(StreamGeometryContext c, float x, float y, float w, float h)
    {
        c.BeginFigure(new Point(x, y), true, true);
        c.LineTo(new Point(x + w, y), false, false);
        c.LineTo(new Point(x + w, y + h), false, false);
        c.LineTo(new Point(x, y + h), false, false);
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
        if (!Layout(w, h, out Rect area, out float stripW)) return;
        Waterfall wf = win.FallShown();
        dc.DrawRectangle(Header, PenOf(Lines, 1f), area);
        DrawBaffles(dc, st, area);
        DrawBearingGrid(dc, area);
        if (win.ShowEchoes) DrawEchoes(dc, st, area);
        else DrawWaterfall(dc, st, wf, area);
        Draw(dc, win.ShowEchoes ? "Active echoes by bearing relative to the bow and range; fades with the age of the fix. Passive sonar gives no range: see the waterfall." : "Bearing relative to the bow (the stern is at both edges), newest at the top.  Brighter = stronger signal excess.", area.X, area.Y + area.Height + 6, 10, Normal);
        DrawNoise(dc, st, wf, area);
        new DepthStrip(this, st, area.X + area.Width + 16, stripW - 8, area.Y, area.Y + area.Height).Draw(dc);
    }

    /// <summary>The baffle sector shaded across the waterfall, in runs of 2 degree columns.</summary>
    private void DrawBaffles(DrawingContext dc, SonarState st, Rect area)
    {
        if (float.IsNaN(st.BaffleFrom)) return;
        float binW = area.Width / SonarWindow.Bins;
        float run0 = -1f;
        for (int b = 0; b <= SonarWindow.Bins; b++)
        {
            bool inside = b < SonarWindow.Bins && GeoMath.InBaffles(st.BaffleFrom, st.BaffleTo, b * 2f - 180f + 1f);
            if (inside && run0 < 0f) run0 = b;
            if (!inside && run0 >= 0f)
            {
                dc.DrawRectangle(Fade(Lines, 0.55f), null, new Rect(area.X + run0 * binW, area.Y, (b - run0) * binW, area.Height));
                run0 = -1f;
            }
        }
        DrawCentered(dc, "baffles", area.X + area.Width * 0.08f, area.Y + area.Height / 2f, 10, Normal);
        DrawCentered(dc, "baffles", area.X + area.Width * 0.92f, area.Y + area.Height / 2f, 10, Normal);
    }

    private void DrawBearingGrid(DrawingContext dc, Rect area)
    {
        for (int deg = -150; deg <= 150; deg += 30)
        {
            float x = area.X + (deg + 180f) / 360f * area.Width;
            dc.DrawLine(GridPen, new Point(x, area.Y), new Point(x, area.Y + area.Height));
            DrawCentered(dc, deg == 0 ? "bow" : (deg > 0 ? "+" : "") + deg, x, area.Y - 14, 10, Normal);
        }
    }

    private void DrawWaterfall(DrawingContext dc, SonarState st, Waterfall wf, Rect area)
    {
        if (wf != null && wf.Count > 0) DrawDots(dc, wf, area);
        if (!st.On)
            DrawCentered(dc, "sonar is not listening", area.X + area.Width / 2, area.Y + area.Height / 2f - 24, 12, Emphasis);
        DrawRight(dc, "now", area.X + area.Width - 4, area.Y + 2, 9, Normal);
        DrawRight(dc, SonarWindow.Rows + " s ago", area.X + area.Width - 4, area.Y + area.Height - 12, 9, Normal);
    }

    /// <summary>One geometry per colour and brightness step instead of one rectangle per dot.</summary>
    private void DrawDots(DrawingContext dc, Waterfall wf, Rect area)
    {
        float binW = area.Width / SonarWindow.Bins, rowH = area.Height / SonarWindow.Rows;
        batches.Clear();
        batchList.Clear();
        for (int age = 0; age < wf.Count; age++)
        {
            var dots = wf.Rows[wf.Back(age)];
            float y = area.Y + age * rowH;
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
                AddRect(c, area.X + d.Bin * binW - dw / 2f + binW / 2f, y, dw, Math.Max(1.5f, rowH + 0.5f));
            }
        }
        foreach (StreamGeometryContext c in batches.Values) c.Dispose();
        foreach ((int key, StreamGeometry g) in batchList)
            dc.DrawGeometry(Fade(key >= 64 ? Blue : Green, (key & 63) / 32f), null, g);
        batches.Clear();
        batchList.Clear();
    }

    /// <summary>The own noise line at the top, with a small graph of it over the waterfall's time.</summary>
    private void DrawNoise(DrawingContext dc, SonarState st, Waterfall wf, Rect area)
    {
        string noise = float.IsNaN(st.OwnNoise) ? "own noise n/a" : "own noise " + st.OwnNoise.ToString("0") + " dB";
        Draw(dc, noise + "   speed " + st.SpeedKts.ToString("0") + " kt", area.X, 8, 11, Highlight);
        if (wf == null || wf.Count <= 1) return;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < wf.Count; i++) { float n = wf.Noise[wf.Back(i)]; if (n < lo) lo = n; if (n > hi) hi = n; }
        float gx = area.X + 240, gw = Math.Min(180f, area.Width - 250);
        if (hi - lo <= 0.5f || gw <= 30) return;
        for (int i = 1; i < wf.Count; i++)
        {
            float xa = gx + gw * (1f - (i - 1f) / SonarWindow.Rows), xb = gx + gw * (1f - (float)i / SonarWindow.Rows);
            float ya = 24 - 14 * (wf.Noise[wf.Back(i - 1)] - lo) / (hi - lo), yb = 24 - 14 * (wf.Noise[wf.Back(i)] - lo) / (hi - lo);
            dc.DrawLine(PenOf(Emphasis, 1f), new Point(xa, ya), new Point(xb, yb));
        }
    }

    private void DrawEchoes(DrawingContext dc, SonarState st, Rect area)
    {
        float maxM = 4000f;
        foreach (SonarState.Mark m in st.Marks)
            if (!float.IsNaN(m.RangeM) && m.RangeM > maxM) maxM = m.RangeM;
        float step = maxM > 40000f ? 20000f : maxM > 10000f ? 5000f : 1000f;
        maxM = (float)Math.Ceiling(maxM * 1.05f / step) * step;
        float x0 = area.X, y0 = area.Y, ww = area.Width, hh = area.Height;
        for (float r = step; r <= maxM + 1f; r += step)
        {
            float y = y0 + hh - hh * r / maxM;
            dc.DrawLine(GridPen, new Point(x0, y), new Point(x0 + ww, y));
            DrawRight(dc, Conv.Nm(r).ToString("0.#") + " nm", x0 + ww - 3, y + 1, 9, Normal);
        }
        int n = 0;
        foreach (SonarState.Mark m in st.Marks)
        {
            if (float.IsNaN(m.RangeM)) continue;
            n++;
            float x = x0 + (m.Rel + 180f) / 360f * ww, y = y0 + hh - hh * m.RangeM / maxM;
            Brush c = Fade(Blue, Math.Max(0.35f, 1f - m.Age / 90f));
            dc.DrawEllipse(c, null, new Point(x, y), 4, 4);
            Draw(dc, m.Label + " " + Conv.Nm(m.RangeM).ToString("0.0") + " nm  " + m.Age.ToString("0") + " s", x + 7, y - 6, 10, c);
        }
        if (n == 0) DrawCentered(dc, "no active echo yet", x0 + ww / 2, y0 + hh / 2f, 12, Normal);
    }

    internal static void RaiseChanged() => Changed?.Invoke();
}

/// <summary>
/// The depth strip right of the waterfall. Scaled to what the player works with (own depth, array, layer, contacts), not to a seabed
/// miles down: with the bottom at 30,000 ft everything of interest sat in the top few pixels and the labels piled up. A deep seabed
/// is written at the bottom. Labels: own things in the left column, contacts in the right one; each column pushed apart so no two
/// overlap, with a short leader line from the label to its mark when it had to move.
/// </summary>
internal readonly struct DepthStrip
{
    private sealed class Label { public float Y, Shown, MarkX; public string Text; public Brush Brush; }

    private readonly Surface s;
    private readonly SonarState st;
    private readonly float sx, sw, top, bottom, maxDepth;
    private readonly bool bedShown;

    public DepthStrip(Surface surface, SonarState state, float x, float width, float top, float bottom)
    {
        s = surface; st = state; sx = x; sw = width; this.top = top; this.bottom = bottom;
        float max = 300f;
        foreach (float d in new[] { st.LayerFt * 1.4f, st.DepthFt * 1.3f, st.ArrayFt * 1.3f })
            if (!float.IsNaN(d) && d > max) max = d;
        foreach (SonarState.Mark m in st.Marks)
            if (!float.IsNaN(m.DepthFt) && m.DepthFt * 1.2f > max) max = m.DepthFt * 1.2f;
        bedShown = !float.IsNaN(st.SeabedFt) && st.SeabedFt <= max * 1.5f;
        if (bedShown) max = Math.Max(max, st.SeabedFt);
        maxDepth = (float)Math.Ceiling(max / 100f) * 100f;
    }

    private float YOf(float d) => top + (bottom - top) * Math.Max(0f, Math.Min(1f, d / maxDepth));

    public void Draw(DrawingContext dc)
    {
        dc.DrawRectangle(s.Header, s.PenOf(s.Lines, 1f), new Rect(sx, top, sw, bottom - top));
        s.DrawCentered(dc, "Depth (ft)", sx + sw / 2, top - 28, 10, s.Highlight);
        dc.DrawRectangle(Surface.Fade(s.Blue, 0.18f), null, new Rect(sx + 1, top + 1, sw - 2, (!float.IsNaN(st.LayerFt) ? YOf(st.LayerFt) : bottom) - top - 1));
        DrawScale(dc);
        var left = new List<Label> { new Label { Y = top + 8, Text = "surface", Brush = s.Normal } };
        if (!float.IsNaN(st.LayerFt)) left.Add(new Label { Y = YOf(st.LayerFt), Text = "layer " + st.LayerFt.ToString("0"), Brush = s.Emphasis });
        // columns: own labels | own mark, array | contact marks | contact labels (to the right of their marks, never over them)
        float ownX = sx + sw * 0.30f, arrX = sx + sw * 0.36f, conX = sx + sw * 0.44f;
        DrawOwn(dc, left, ownX, arrX);
        List<Label> right = DrawMarks(dc, conX);
        LayOut(left, top + 2, bottom - 14);
        LayOut(right, top + 2, bottom - 26);
        foreach (Label l in left)
        {
            if (l.MarkX > 0f && Math.Abs(l.Shown - l.Y) > 3f) dc.DrawLine(s.PenOf(Surface.Fade(l.Brush, 0.5f), 1f), new Point(sx + 34, l.Shown), new Point(l.MarkX - 4, l.Y));
            s.Draw(dc, l.Text, sx + 4, l.Shown - 6, 9, l.Brush);
        }
        foreach (Label l in right)
        {
            if (Math.Abs(l.Shown - l.Y) > 3f) dc.DrawLine(s.PenOf(Surface.Fade(l.Brush, 0.5f), 1f), new Point(conX + 4, l.Y), new Point(conX + 10, l.Shown));
            s.Draw(dc, l.Text, conX + 12, l.Shown - 6, 9, l.Brush);
        }
    }

    /// <summary>Scale ticks on the left edge, the seabed and the layer line.</summary>
    private void DrawScale(DrawingContext dc)
    {
        float tick = maxDepth > 4000f ? 1000f : maxDepth > 1500f ? 500f : maxDepth > 600f ? 200f : 100f;
        for (float d = tick; d < maxDepth; d += tick)
        {
            float y = YOf(d);
            dc.DrawLine(s.GridPen, new Point(sx, y), new Point(sx + 5, y));
            s.Draw(dc, d.ToString("0"), sx + 7, y - 6, 8, Surface.Fade(s.Normal, 0.7f));
        }
        if (bedShown)
        {
            float yb = YOf(st.SeabedFt);
            dc.DrawRectangle(Surface.Fade(s.Normal, 0.45f), null, new Rect(sx + 1, yb, sw - 2, Math.Max(1f, bottom - yb)));
        }
        s.DrawRight(dc, !float.IsNaN(st.SeabedFt) ? "seabed " + st.SeabedFt.ToString("0") + (bedShown ? "" : " (below)") : "", sx + sw - 3, bottom - 13, 9, s.Normal);
        if (!float.IsNaN(st.LayerFt))
        {
            float yl = YOf(st.LayerFt);
            dc.DrawLine(s.PenOf(s.Emphasis, 1.5f), new Point(sx, yl), new Point(sx + sw, yl));
        }
    }

    private void DrawOwn(DrawingContext dc, List<Label> left, float ownX, float arrX)
    {
        if (!float.IsNaN(st.DepthFt))
        {
            float yd = YOf(st.DepthFt);
            dc.DrawEllipse(s.Highlight, null, new Point(ownX, yd), 4, 4);
            left.Add(new Label { Y = yd, Text = "own " + st.DepthFt.ToString("0"), Brush = s.Highlight, MarkX = ownX });
        }
        if (float.IsNaN(st.ArrayFt)) return;
        float ya = YOf(st.ArrayFt);
        dc.DrawLine(s.PenOf(s.Green, 1.5f), new Point(ownX, YOf(float.IsNaN(st.DepthFt) ? 0f : st.DepthFt)), new Point(arrX, ya));
        dc.DrawRectangle(s.Green, null, new Rect(arrX - 3, ya - 3, 6, 6));
        if (float.IsNaN(st.DepthFt) || Math.Abs(st.ArrayFt - st.DepthFt) > 1f)
            left.Add(new Label { Y = ya, Text = "array " + st.ArrayFt.ToString("0"), Brush = s.Green, MarkX = arrX });
    }

    /// <summary>One mark per contact (passive and active of the same), with its depth uncertainty; returns their labels.</summary>
    private List<Label> DrawMarks(DrawingContext dc, float conX)
    {
        var right = new List<Label>();
        var seen = new HashSet<string>();
        foreach (SonarState.Mark m in st.Marks)
        {
            if (float.IsNaN(m.DepthFt) || !seen.Add(m.Label)) continue;
            float ym = YOf(m.DepthFt);
            Brush mc = Surface.Fade(m.Active ? s.Blue : s.Green, Math.Max(0.4f, 1f - m.Age / 60f));
            if (m.DepthErrFt > 1f)
            {
                // faint, with small end caps, so a vague estimate does not cut through the strip
                Pen ep = s.PenOf(Surface.Fade(mc, 0.3f), 1f);
                float ya0 = YOf(Math.Max(0f, m.DepthFt - m.DepthErrFt)), ya1 = YOf(m.DepthFt + m.DepthErrFt);
                dc.DrawLine(ep, new Point(conX, ya0), new Point(conX, ya1));
                dc.DrawLine(ep, new Point(conX - 3, ya0), new Point(conX + 3, ya0));
                dc.DrawLine(ep, new Point(conX - 3, ya1), new Point(conX + 3, ya1));
            }
            dc.DrawEllipse(mc, null, new Point(conX, ym), 3, 3);
            right.Add(new Label { Y = ym, Text = m.Label + "  " + m.DepthFt.ToString("0"), Brush = mc, MarkX = conX });
        }
        return right;
    }

    /// <summary>Spreads labels so that each sits at least one text line below the one above it, as close to its mark as it can.</summary>
    private static void LayOut(List<Label> labels, float minY, float maxY)
    {
        const float line = 12f;
        labels.Sort((p, q) => p.Y.CompareTo(q.Y));
        float next = minY + 6f;
        foreach (Label l in labels) { l.Shown = Math.Max(l.Y, next); next = l.Shown + line; }
        // too many to fit: pull the bottom ones back up
        float limit = maxY;
        for (int i = labels.Count - 1; i >= 0; i--) { if (labels[i].Shown > limit) labels[i].Shown = limit; limit = labels[i].Shown - line; }
    }
}

/// <summary>The sonar display's measuring cursor: bearing (true and relative to the bow) and age under the mouse, on its own layer.</summary>
public sealed class SonarCursor : CursorSurface
{
    private SonarWindow Win => DataContext as SonarWindow;

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        SonarWindow win = Win;
        if (win == null || win.ShowEchoes || Pointer.X < 0) return;
        SonarState st = win.State;
        if (!st.Valid || !SonarSurface.Layout(w, h, out Rect a, out _)) return;
        if (Pointer.X < a.X || Pointer.X > a.X + a.Width || Pointer.Y < a.Y || Pointer.Y > a.Y + a.Height) return;
        float rel = (float)((Pointer.X - a.X) / a.Width * 360f - 180f);
        float secs = (float)((Pointer.Y - a.Y) / a.Height * SonarWindow.Rows);
        float brg = SensorPicture.Wrap360(st.Heading + rel);
        Pen cp = PenOf(Highlight, 1f);
        dc.DrawLine(cp, new Point(Pointer.X, a.Y), new Point(Pointer.X, a.Y + a.Height));
        dc.DrawLine(cp, new Point(a.X, Pointer.Y), new Point(a.X + a.Width, Pointer.Y));
        string t = "brg " + Fmt.Compass(brg) + "  rel " + (rel > 0 ? "+" : "") + rel.ToString("0") + "  " + secs.ToString("0") + " s ago";
        float tx = Pointer.X > a.X + a.Width - 190 ? (float)Pointer.X - 190 : (float)Pointer.X + 8;
        float ty = Pointer.Y > a.Y + a.Height - 20 ? (float)Pointer.Y - 18 : (float)Pointer.Y + 6;
        DrawTag(dc, t, tx, ty, 188);
    }
}
