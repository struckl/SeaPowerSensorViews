using System;
using SeaPower;
using SeaPowerSensorViews.Data;
using SeaPowerSensorViews.Map;
#if FLATMAP
using SeaPowerSensorViews.Ui;
#else
using SeaPowerSensorViews.Globe.Ui;
#endif
using UnityEngine;

namespace SeaPowerSensorViews;

/// <summary>Lives on a hidden object: sets the windows up when a mission's UI is ready, refreshes the snapshot while a window is open, hot keys.</summary>
internal sealed class Host : MonoBehaviour
{
    public static Host Instance;

    private bool setupPending;
    private bool active;
    private float nextTry;
    private int tries;
    private float nextAnimate;
    private float nextSync;
    private enum HookState { Waiting, Applied, Failed }
    private HookState mapHooks;
    private float nextHookCheck;

    private void Awake() => Instance = this;

    internal void MissionReady()
    {
        try { Map.MapSet.MissionReady(); }
        catch (Exception ex) { ModLog.Error("map layers off for this mission: " + ex); }
        setupPending = true;
        tries = 0;
        nextTry = 0f;
    }

    internal void MissionEnded()
    {
        setupPending = false;
        active = false;
        try { Map.MapSet.MissionEnded(); }
        catch (Exception ex) { ModLog.Error("mission end (map): " + ex.Message); }
        try { Kit.Teardown(); }
        catch (Exception ex) { ModLog.Error("mission end: " + ex.Message); }
        SensorPicture.Current.Reset();
    }

    internal void MapRendered(object layer) => Map.MapSet.Rendered(layer);

    private void Update()
    {
        if (mapHooks == HookState.Waiting && Time.unscaledTime >= nextHookCheck)
        {
            nextHookCheck = Time.unscaledTime + 1f;
            // A WindowHost registers itself with WindowHostManager when it is built, which only happens while NoesisGUI runs
            // (the main menu view has one) - from then on the map control's Noesis types are safe to patch.
            if (Singleton<SeapowerUI.WindowHostManager>.InstanceExists())
            {
                try
                {
                    mapHooks = Hooks.ApplyMapHooks() ? HookState.Applied : HookState.Failed;
                    if (mapHooks == HookState.Applied) ModLog.Info("Map hook in place.");
                    try { Hooks.ApplyOptionsHooks(); }
                    catch (Exception ex) { ModLog.Warn("Options page hook failed - settings only in the config file: " + ex.Message); }
                }
                catch (Exception ex)
                {
                    mapHooks = HookState.Failed;
                    ModLog.Error("Map hook failed - nothing on the map: " + ex);
                }
            }
        }
        if (setupPending && Time.unscaledTime >= nextTry)
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
        if (!active) return;
        try
        {
            if (Time.unscaledTime >= nextSync)
            {
                nextSync = Time.unscaledTime + 0.5f;
                Kit.Sync();
            }
            bool windows = Kit.AnyOpen();
            SensorPicture pic = SensorPicture.Current;
            if (windows || Map.MapSet.NeedsPicture)
            {
                long t = Perf.Start();
                if (windows) pic.PollStrobes();
                bool fresh = pic.Tick(Time.unscaledTime);
                Perf.Stop(Perf.Part.Picture, t);
                if (fresh && windows)
                    OnSnapshot();
            }
            Map.MapSet.Update(Time.unscaledTime);
            Perf.Frame();
            if (windows && Time.unscaledTime >= nextAnimate)
            {
                nextAnimate = Time.unscaledTime + 0.04f;     // the scope's sweep: 25 redraws a second
                for (int i = 0; i < Registry.Surfaces.Count; i++)
                    if (Registry.Surfaces[i].Animated) Registry.Surfaces[i].Animate();
            }
        }
        catch (Exception ex)
        {
            if (errors++ < 5) ModLog.Error("update: " + ex);
        }
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

    private int errors;

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
            if (errors++ < 5) ModLog.Error("hot key: " + ex.Message);
        }
    }

    private static bool Match(string text, Event ev) =>
        Settings.TryParseKey(text, out KeyCode key, out bool ctrl, out bool shift, out bool alt)
        && ev.keyCode == key && ev.control == ctrl && ev.shift == shift && ev.alt == alt;
}
