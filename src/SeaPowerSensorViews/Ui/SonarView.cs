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
    private struct Heard { public string Name; public float Rel, RangeM, DepthFt, Signal; public bool Active; }
    private static readonly System.Collections.Generic.List<Heard> ownHeard = new System.Collections.Generic.List<Heard>();

    /// <summary>
    /// The game makes no sonar tracks of the player's own task force, so its other ships go through the game's sonar sums here, with the
    /// same filters, baffles, line of sight and detection threshold as a contact (the public per-contact sums; the game's batch model is private). Display only.
    /// </summary>
    private static void CollectOwnShips(UnitRow unit, SensorPicture pic, SensorSystemSonar sonar, SonarParameters sp, bool active, SonarState st)
    {
        if (sp == null) return;
        try
        {
            ObjectBase home = unit.Unit;
            var pos = sonar.SensorPosition;
            if (pos.y > 0f) return;      // not in the water
            var env = Singleton<SeaPower.Environment>.Instance;
            GeoPosition here = home.getGeoPosition();
            float ambient = env.getOceanNoise(here);
            if (!float.IsNaN(st.OwnNoise))
                ambient = 10f * (float)Math.Log10(Math.Pow(10.0, ambient / 10.0) + Math.Pow(10.0, st.OwnNoise / 10.0));
            float xLayer = env.getXLayerAttenuation(), duct = env.getSurfaceDuct(here);
            foreach (UnitRow other in pic.Units)
            {
                ObjectBase t = other.Unit;
                if (ReferenceEquals(other, unit) || t == null || !(t is Vessel || t is Submarine) || t._isUndetectable || t._sensorData == null || !t._hasControl) continue;
                if (sp._isActiveIntercept && !t.IsActiveSonarsOn.Value) continue;
                GeoMath.BearingRange(here, t.getGeoPosition(), out float brg, out float nm);
                float rel = Ang.DeltaAngle(unit.Heading, brg);
                if (!float.IsNaN(st.BaffleFrom) && (st.BaffleFrom < st.BaffleTo ? (st.BaffleFrom < rel && rel < st.BaffleTo) : (st.BaffleTo >= rel || rel >= st.BaffleFrom))) continue;
                float dist = (t.transform.position - home.transform.position).magnitude;
                if (!Singleton<UnitSensorCache>.Instance.UnitsHaveDirectlLOS(home, t, dist)) continue;
                float unityDist = (t.transform.position - pos).magnitude;
                bool shipToShip = home._type == ObjectBase.ObjectType.Vessel && t._type == ObjectBase.ObjectType.Vessel;
                float y = t.transform.position.y;
                float depth = y < 0f && t is Submarine ? -y * SensorPicture.FeetPerUnit : float.NaN;
                if (t._sensorData._baseNoise >= 0f)
                {
                    float signal = shipToShip ? ShipPassive(t, sp, ambient, duct, unityDist)
                                              : (float)SonarCalculator.GetPassiveSonarEcho(t, home, pos, sp._gain, ambient, xLayer, duct)._signalValue;
                    if (signal >= sp._detectionTreshold) ownHeard.Add(new Heard { Name = other.Name, Rel = rel, DepthFt = depth, Signal = signal });
                }
                if (active && t._sensorData._maxTargetReflectionStrength >= 0f)
                {
                    bool lf = sp._frequency == SonarParameters.SonarFrequency.LF, mf = sp._frequency == SonarParameters.SonarFrequency.MF;
                    float signal = shipToShip ? ShipActive(t, sp, ambient, duct, unityDist)
                                              : (float)SonarCalculator.GetActiveSonarEcho(t, home, pos, sp._outputPower, sp._activeGain, ambient, xLayer, duct, false, false, false, lf, lf || mf)._signalValue;
                    if (signal >= sp._detectionTreshold) ownHeard.Add(new Heard { Name = other.Name, Rel = rel, RangeM = nm * 1852f, DepthFt = depth, Signal = signal, Active = true });
                }
            }
            CollectOwnTorpedoes(unit, home, pos, here, sp, active, st, ambient, xLayer, duct);
        }
        catch (Exception ex) { ModLog.Debug("Sonar own-ship check: " + ex.Message); }
    }

    /// <summary>
    /// The player's own torpedoes in the water. The game makes no sonar tracks of them either, so they go through the same public sonar
    /// sums as an enemy torpedo (its noise from speed and cavitation; an echo when pinging), with the same baffles, line of sight and threshold.
    /// Lets a player switch off the game's track display and steer a wire-guided torpedo by ear. Display only.
    /// </summary>
    private static void CollectOwnTorpedoes(UnitRow unit, ObjectBase home, UnityEngine.Vector3 pos, GeoPosition here, SonarParameters sp, bool active,
        SonarState st, float ambient, float xLayer, float duct)
    {
        if (!Settings.SonarOwnTorpedoes.Value || home._taskforce == null || !Singleton<ObjectsManager>.InstanceExists()) return;
        var weapons = Singleton<ObjectsManager>.Instance._listOfAllWeapons;
        for (int i = 0; i < weapons.Count; i++)
        {
            if (!(weapons[i] is Torpedo t) || t == null || t.IsDestroyed || t._launchPlatform == null || t._sensorData == null) continue;
            if (!ReferenceEquals(t._taskforce, home._taskforce) || t.transform.position.y >= 0f) continue;   // own side, in the water
            if (sp._isActiveIntercept) continue;          // an intercept receiver hears pings only
            GeoMath.BearingRange(here, t.getGeoPosition(), out float brg, out float nm);
            float rel = Ang.DeltaAngle(unit.Heading, brg);
            if (!float.IsNaN(st.BaffleFrom) && (st.BaffleFrom < st.BaffleTo ? (st.BaffleFrom < rel && rel < st.BaffleTo) : (st.BaffleTo >= rel || rel >= st.BaffleFrom))) continue;
            float dist = (t.transform.position - home.transform.position).magnitude;
            if (!Singleton<UnitSensorCache>.Instance.UnitsHaveDirectlLOS(home, t, dist)) continue;
            float depth = -t.transform.position.y * SensorPicture.FeetPerUnit;
            string name = "own " + TorpedoName(t) + (ReferenceEquals(t._launchPlatform, home) ? "" : " (" + t._launchPlatform.getName() + ")");
            if (t._sensorData._baseNoise >= 0f)
            {
                float signal = (float)SonarCalculator.GetPassiveSonarEcho(t, home, pos, sp._gain, ambient, xLayer, duct)._signalValue;
                if (signal >= sp._detectionTreshold) ownHeard.Add(new Heard { Name = name, Rel = rel, DepthFt = depth, Signal = signal });
            }
            if (active && t._sensorData._maxTargetReflectionStrength >= 0f)
            {
                bool lf = sp._frequency == SonarParameters.SonarFrequency.LF, mf = sp._frequency == SonarParameters.SonarFrequency.MF;
                float signal = (float)SonarCalculator.GetActiveSonarEcho(t, home, pos, sp._outputPower, sp._activeGain, ambient, xLayer, duct, false, false, false, lf, lf || mf)._signalValue;
                if (signal >= sp._detectionTreshold) ownHeard.Add(new Heard { Name = name, Rel = rel, RangeM = nm * 1852f, DepthFt = depth, Signal = signal, Active = true });
            }
        }
    }

    private static string TorpedoName(Torpedo t)
    {
        try { string n = t.getName(); if (!string.IsNullOrEmpty(n)) return n; } catch (Exception) { }
        return "torpedo";
    }

    // The public per-contact sums return "not heard" for any surface ship listening to a surface ship, while the game's batch model, the one
    // enemy ships go through, does hear them. So for that pair the same sums without the block; two surface ships are both above the layer.
    private const float NmPerUnityDistance = 0.036285132f;

    private static float TransmissionLoss(float unityDist)
    {
        float nm = Math.Max(0.001f, unityDist * NmPerUnityDistance);
        return 20f * (float)Math.Log10(nm * 2025f) + nm * Globals._sonarEnvironmentParameters._absorptionFactor;
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
        // Off, broken or a towed array still on its winch: the sonar hears nothing, whatever tracks the game still keeps for a moment.
        bool listening = sonar._sonar != null && sonar.CanProduceContacts && (sonar._parentTowedSystem == null || sonar._parentTowedSystem.TowedSystemState != TowedSystem.SystemState.SystemRetracted);
        st.On = listening;
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
        ownHeard.Clear();
        if (listening) CollectOwnShips(unit, pic, sonar, sp, active, st);
        foreach (Heard o in ownHeard)
            st.Marks.Add(new SonarState.Mark
            {
                Label = o.Name, Rel = o.Rel, RangeM = o.Active ? o.RangeM : float.NaN, Age = 0f, DepthFt = o.DepthFt, Active = o.Active,
            });
        if (listening) foreach (ContactRow cr in pic.Contacts)
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
        foreach (Heard o in ownHeard)
        {
            int ownBin = Math.Max(0, Math.Min(SonarWindow.Bins - 1, (int)((o.Rel + 180f) / 360f * SonarWindow.Bins)));
            dots.Add(new Waterfall.Dot { Bin = (short)ownBin, Width = 1, Level = (byte)(70 + 185 * Math.Max(0f, Math.Min(1f, o.Signal / 30f))), Active = o.Active });
        }
        if (listening) foreach (ContactRow cr in pic.Contacts)
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
            DrawCentered(dc, "sonar is not listening", x0 + ww / 2, y0 + hh / 2f - 24, 12, Emphasis);
        }
        else DrawEchoes(dc, st, x0, y0, ww, hh);
        if (!win.ShowEchoes)
        {
        DrawRight(dc, "now", x0 + ww - 4, y0 + 2, 9, Normal);
        DrawRight(dc, SonarWindow.Rows + " s ago", x0 + ww - 4, y0 + hh - 12, 9, Normal);
        }
        Draw(dc, win.ShowEchoes ? "Active echoes by bearing relative to the bow and range; fades with the age of the fix. Passive sonar gives no range: see the waterfall." : "Bearing relative to the bow (the stern is at both edges), newest at the top.  Brighter = stronger signal excess.", x0, y0 + hh + 6, 10, Normal);

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
        // Scaled to what the player works with (own depth, array, layer, contacts), not to a seabed miles down: with the bottom at
        // 30,000 ft everything of interest sat in the top few pixels and the labels piled up. A deep seabed is written at the bottom.
        float sx = x0 + ww + 16, sw = stripW - 8;
        float maxDepth = 300f;
        foreach (float d in new[] { st.LayerFt * 1.4f, st.DepthFt * 1.3f, st.ArrayFt * 1.3f })
            if (!float.IsNaN(d) && d > maxDepth) maxDepth = d;
        foreach (SonarState.Mark m in st.Marks)
            if (!float.IsNaN(m.DepthFt) && m.DepthFt * 1.2f > maxDepth) maxDepth = m.DepthFt * 1.2f;
        bool bedShown = !float.IsNaN(st.SeabedFt) && st.SeabedFt <= maxDepth * 1.5f;
        if (bedShown) maxDepth = Math.Max(maxDepth, st.SeabedFt);
        maxDepth = (float)Math.Ceiling(maxDepth / 100f) * 100f;
        float top = y0, bottom = y0 + hh;
        dc.DrawRectangle(Header, PenOf(Lines, 1f), new Rect(sx, top, sw, bottom - top));
        DrawCentered(dc, "Depth (ft)", sx + sw / 2, y0 - 28, 10, Highlight);
        Func<float, float> yOf = d => top + (bottom - top) * Math.Max(0f, Math.Min(1f, d / maxDepth));
        dc.DrawRectangle(Fade(Blue, 0.18f), null, new Rect(sx + 1, top + 1, sw - 2, (!float.IsNaN(st.LayerFt) ? yOf(st.LayerFt) : bottom) - top - 1));
        // scale ticks on the left edge
        float tick = maxDepth > 4000f ? 1000f : maxDepth > 1500f ? 500f : maxDepth > 600f ? 200f : 100f;
        for (float d = tick; d < maxDepth; d += tick)
        {
            float y = yOf(d);
            dc.DrawLine(PenOf(Fade(Lines, 0.8f), 1f), new Point(sx, y), new Point(sx + 5, y));
            Draw(dc, d.ToString("0"), sx + 7, y - 6, 8, Fade(Normal, 0.7f));
        }
        if (bedShown)
        {
            float yb = yOf(st.SeabedFt);
            dc.DrawRectangle(Fade(Normal, 0.45f), null, new Rect(sx + 1, yb, sw - 2, Math.Max(1f, bottom - yb)));
        }
        DrawRight(dc, !float.IsNaN(st.SeabedFt) ? "seabed " + st.SeabedFt.ToString("0") + (bedShown ? "" : " (below)") : "", sx + sw - 3, bottom - 13, 9, Normal);
        if (!float.IsNaN(st.LayerFt))
        {
            float yl = yOf(st.LayerFt);
            dc.DrawLine(PenOf(Emphasis, 1.5f), new Point(sx, yl), new Point(sx + sw, yl));
        }

        // Labels: own things in the left column, contacts in the right one; each column pushed apart so no two overlap,
        // with a short leader line from the label to its mark when it had to move.
        var left = new System.Collections.Generic.List<DepthLabel>();
        var right = new System.Collections.Generic.List<DepthLabel>();
        left.Add(new DepthLabel { Y = top + 8, Text = "surface", Brush = Normal });
        if (!float.IsNaN(st.LayerFt)) left.Add(new DepthLabel { Y = yOf(st.LayerFt), Text = "layer " + st.LayerFt.ToString("0"), Brush = Emphasis });
        float ownX = sx + sw * 0.42f, arrX = sx + sw * 0.5f, conX = sx + sw * 0.62f;
        if (!float.IsNaN(st.DepthFt))
        {
            float yd = yOf(st.DepthFt);
            dc.DrawEllipse(Highlight, null, new Point(ownX, yd), 4, 4);
            left.Add(new DepthLabel { Y = yd, Text = "own " + st.DepthFt.ToString("0"), Brush = Highlight, MarkX = ownX });
        }
        if (!float.IsNaN(st.ArrayFt))
        {
            float ya = yOf(st.ArrayFt);
            dc.DrawLine(PenOf(Green, 1.5f), new Point(ownX, yOf(float.IsNaN(st.DepthFt) ? 0f : st.DepthFt)), new Point(arrX, ya));
            dc.DrawRectangle(Green, null, new Rect(arrX - 3, ya - 3, 6, 6));
            if (float.IsNaN(st.DepthFt) || Math.Abs(st.ArrayFt - st.DepthFt) > 1f)
                left.Add(new DepthLabel { Y = ya, Text = "array " + st.ArrayFt.ToString("0"), Brush = Green, MarkX = arrX });
        }
        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (SonarState.Mark m in st.Marks)
        {
            if (float.IsNaN(m.DepthFt) || !seen.Add(m.Label)) continue;     // one mark per contact (passive and active of the same)
            float ym = yOf(m.DepthFt);
            Brush mc = Fade(m.Active ? Blue : Green, Math.Max(0.4f, 1f - m.Age / 60f));
            if (m.DepthErrFt > 1f)
                dc.DrawLine(PenOf(mc, 1f), new Point(conX, yOf(Math.Max(0f, m.DepthFt - m.DepthErrFt))), new Point(conX, yOf(m.DepthFt + m.DepthErrFt)));
            dc.DrawEllipse(mc, null, new Point(conX, ym), 3, 3);
            right.Add(new DepthLabel { Y = ym, Text = m.Label + "  " + m.DepthFt.ToString("0"), Brush = mc, MarkX = conX });
        }
        LayOut(left, top + 2, bottom - 14);
        LayOut(right, top + 2, bottom - 26);
        foreach (DepthLabel l in left)
        {
            if (l.MarkX > 0f && Math.Abs(l.Shown - l.Y) > 3f) dc.DrawLine(PenOf(Fade(l.Brush, 0.5f), 1f), new Point(sx + 34, l.Shown), new Point(l.MarkX - 4, l.Y));
            Draw(dc, l.Text, sx + 4, l.Shown - 6, 9, l.Brush);
        }
        foreach (DepthLabel l in right)
        {
            if (Math.Abs(l.Shown - l.Y) > 3f) dc.DrawLine(PenOf(Fade(l.Brush, 0.5f), 1f), new Point(conX + 4, l.Y), new Point(sx + sw - 60, l.Shown));
            DrawRight(dc, l.Text, sx + sw - 3, l.Shown - 6, 9, l.Brush);
        }
    }

    private sealed class DepthLabel { public float Y, Shown, MarkX; public string Text; public Brush Brush; }

    /// <summary>Spreads labels so that each sits at least one text line below the one above it, as close to its mark as it can.</summary>
    private static void LayOut(System.Collections.Generic.List<DepthLabel> labels, float minY, float maxY)
    {
        const float line = 12f;
        labels.Sort((p, q) => p.Y.CompareTo(q.Y));
        float next = minY + 6f;
        foreach (DepthLabel l in labels) { l.Shown = Math.Max(l.Y, next); next = l.Shown + line; }
        // too many to fit: pull the bottom ones back up
        float limit = maxY;
        for (int i = labels.Count - 1; i >= 0; i--) { if (labels[i].Shown > limit) labels[i].Shown = limit; limit = labels[i].Shown - line; }
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
            Brush c = Fade(Blue, Math.Max(0.35f, 1f - m.Age / 90f));
            dc.DrawEllipse(c, null, new Point(x, y), 4, 4);
            Draw(dc, m.Label + " " + (m.RangeM / 1852f).ToString("0.0") + " nm  " + m.Age.ToString("0") + " s", x + 7, y - 6, 10, c);
        }
        if (n == 0) DrawCentered(dc, "no active echo yet", x0 + ww / 2, y0 + hh / 2f, 12, Normal);
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
