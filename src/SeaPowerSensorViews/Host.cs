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
    /// <summary>The main view comes a moment after the mission start: looked for every SetupRetry s, SetupTries times.</summary>
    private const float SetupRetry = 0.5f;
    private const int SetupTries = 40;
    /// <summary>The bar buttons follow windows closed with their own X about twice a second.</summary>
    private const float SyncInterval = 0.5f;
    /// <summary>The slow snapshot (board, sonar, waterfall row): about once a second, a little early so a 1 s tick is not missed.</summary>
    private const float SlowInterval = 0.95f;
    /// <summary>The scope's sweep: 25 redraws a second.</summary>
    private const float AnimateInterval = 0.04f;

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
            nextSync = Time.unscaledTime + SyncInterval;
            Kit.Sync();
        }
        bool windows = Kit.AnyOpen();
        if (windows || Map.MapSet.NeedsPicture)
            RefreshPicture(windows);
        Map.MapSet.Update(Time.unscaledTime);
        Perf.Frame();
        if (windows && Time.unscaledTime >= nextAnimate)
        {
            nextAnimate = Time.unscaledTime + AnimateInterval;
            Kit.Animate();
        }
    }

    private void TrySetup()
    {
        nextTry = Time.unscaledTime + SetupRetry;
        try
        {
            if (Kit.Setup()) { setupPending = false; active = true; }
            else if (++tries > SetupTries) { setupPending = false; ModLog.Warn("The main game view never appeared - sensor windows are off for this mission."); }
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
        // same moment as the world; the board and sonar are fine with one a second. The map's sensor lines follow the game's clock too.
        bool fast = (windows && Kit.IsOpen(Kit.Scope)) || Map.MapSet.NeedsPicture;
        var limits = new SensorPicture.Limits { MemorySeconds = Settings.MemorySeconds.Value, VisualSeconds = Settings.VisualLifetime.Value };
        bool fresh = pic.Tick(Time.unscaledTime, limits, fast ? 0.1 : 1.0, fast);
        Perf.Stop(Perf.Part.Picture, t);
        if (!fresh || !windows) return;
        bool slow = Time.unscaledTime >= nextSlow;
        if (slow) nextSlow = Time.unscaledTime + SlowInterval;
        Kit.Snapshot(slow);
    }

    private void OnGUI()
    {
        if (!active) return;
        Event ev = Event.current;
        if (ev == null || ev.type != EventType.KeyDown) return;
        try
        {
            if (Singleton<InputHandler>.InstanceExists() && Singleton<InputHandler>.Instance.typingActive) return;
            if (Match(Settings.BoardKey.Value, ev)) { Kit.Toggle(Kit.Board); ev.Use(); }
            else if (Match(Settings.ScopeKey.Value, ev)) { Kit.Toggle(Kit.Scope); ev.Use(); }
            else if (Match(Settings.SonarKey.Value, ev)) { Kit.Toggle(Kit.Sonar); ev.Use(); }
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
