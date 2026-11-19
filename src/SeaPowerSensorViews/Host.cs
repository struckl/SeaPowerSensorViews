using System;
using SeaPower;
using SeaPowerSensorViews.Data;
#if FLATMAP
using SeaPowerSensorViews.Ui;
#else
using SeaPowerSensorViews.Globe.Ui;
#endif
using UnityEngine;

namespace SeaPowerSensorViews;

/// <summary>Lives on a hidden object: sets the windows up when a mission's UI is ready, refreshes the snapshot while a window is open, hot keys.</summary>
internal sealed class Host : ModHost
{
    private bool setupPending;
    private bool active;
    private float nextTry;
    private int tries;
    private float nextAnimate;
    private float nextSync, nextSlow;

    protected override string StageTwoReady => "Map hook in place.";

    protected override void OnMissionReady()
    {
        try { Map.MapSet.MissionReady(); }
        catch (Exception ex) { ModLog.Error("map layers off for this mission: " + ex); }
        setupPending = true;
        tries = 0;
        nextTry = 0f;
    }

    protected override void OnMissionEnded()
    {
        setupPending = false;
        active = false;
        try { Map.MapSet.MissionEnded(); }
        catch (Exception ex) { ModLog.Error("mission end (map): " + ex.Message); }
        try { Kit.Teardown(); }
        catch (Exception ex) { ModLog.Error("mission end: " + ex.Message); }
        SensorPicture.Current.Reset();
    }

    protected override void Tick()
    {
        if (setupPending && Time.unscaledTime >= nextTry)
            TrySetup();
        if (!active) return;
        if (Time.unscaledTime >= nextSync)
        {
            nextSync = Time.unscaledTime + 0.5f;
            Kit.Sync();
        }
        bool windows = Kit.AnyOpen();
        if (windows || Map.MapSet.NeedsPicture)
            RefreshPicture(windows);
        Map.MapSet.Update(Time.unscaledTime);
        Perf.Frame();
        if (windows && Time.unscaledTime >= nextAnimate)
        {
            nextAnimate = Time.unscaledTime + 0.04f;     // the scope's sweep: 25 redraws a second
            for (int i = 0; i < Registry.Surfaces.Count; i++)
                if (Registry.Surfaces[i].Animated) Registry.Surfaces[i].Animate();
        }
    }

    private void TrySetup()
    {
        nextTry = Time.unscaledTime + 0.5f;
        try
        {
            if (Kit.Setup()) { setupPending = false; active = true; }
            else if (++tries > 40) { setupPending = false; ModLog.Warn("The main game view never appeared - sensor windows are off for this mission."); }
        }
        catch (Exception ex)
        {
            setupPending = false;
            ModLog.Error("window setup failed, sensor windows are off for this mission: " + ex);
        }
    }

    private void RefreshPicture(bool windows)
    {
        SensorPicture pic = SensorPicture.Current;
        long t = Perf.Start();
        if (windows) pic.PollStrobes();
        // With the scope open the snapshot follows the game's clock (10 a second at most) so its lines and contacts are from the
        // same moment as the world; the board and sonar are fine with one a second.
        bool scopeOpen = windows && Kit.Window(1) is ScopeWindow sc && Kit.IsOpen(sc);
        bool fast = scopeOpen || Map.MapSet.NeedsPicture;       // the map's sensor lines follow the game's clock too
        bool fresh = fast ? pic.Tick(Time.unscaledTime, 0.1, true) : pic.Tick(Time.unscaledTime);
        Perf.Stop(Perf.Part.Picture, t);
        if (!fresh || !windows) return;
        // The sonar waterfall takes one row per slow snapshot (150 rows = 150 s), the board and the sonar redraw once a
        // second; only the scope's surfaces follow the fast snapshots.
        if (Time.unscaledTime >= nextSlow) { nextSlow = Time.unscaledTime + 0.95f; OnSnapshot(); }
        else
            for (int i = 0; i < Registry.Surfaces.Count; i++)
                if (Registry.Surfaces[i] is ScopeSurface) Registry.Surfaces[i].InvalidateVisual();
    }

    private static void OnSnapshot()
    {
        for (int i = 0; i < 3; i++)
            if (Kit.Window(i) is SonarWindow sw && Kit.IsOpen(sw))
                SonarSampler.Update(sw);
        if (Kit.Window(1) is ScopeWindow scope && Kit.IsOpen(scope))
            scope.PushAltitude();
        SonarSurface.RaiseChanged();            // the sonar chooser's name line
        Registry.RefreshAll();
    }

    private void OnGUI()
    {
        if (!active) return;
        Event ev = Event.current;
        if (ev == null || ev.type != EventType.KeyDown) return;
        try
        {
            if (Singleton<InputHandler>.InstanceExists() && Singleton<InputHandler>.Instance.typingActive) return;
            if (Match(Settings.BoardKey.Value, ev)) { Kit.Toggle(Kit.Window(0)); ev.Use(); }
            else if (Match(Settings.ScopeKey.Value, ev)) { Kit.Toggle(Kit.Window(1)); ev.Use(); }
            else if (Match(Settings.SonarKey.Value, ev)) { Kit.Toggle(Kit.Window(2)); ev.Use(); }
            else if (Match(Settings.LinesKey.Value, ev)) { Map.MapSet.ToggleLines(); ev.Use(); }
            else if (Match(Settings.CoverageKey.Value, ev)) { Map.MapSet.ToggleCoverage(); ev.Use(); }
        }
        catch (Exception ex)
        {
            ReportError("hot key: " + ex.Message);
        }
    }

    private static bool Match(string text, Event ev) =>
        Settings.TryParseKey(text, out KeyCode key, out bool ctrl, out bool shift, out bool alt)
        && ev.keyCode == key && ev.control == ctrl && ev.shift == shift && ev.alt == alt;
}
