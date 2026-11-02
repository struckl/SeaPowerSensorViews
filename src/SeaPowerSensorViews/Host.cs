using System;
using SeaPower;
using SeaPowerSensorViews.Data;
using SeaPowerSensorViews.Ui;
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

    private void Awake() => Instance = this;

    internal void MissionReady()
    {
        setupPending = true;
        tries = 0;
        nextTry = 0f;
    }

    internal void MissionEnded()
    {
        setupPending = false;
        active = false;
        try { Kit.Teardown(); }
        catch (Exception ex) { ModLog.Error("mission end: " + ex.Message); }
        SensorPicture.Current.Reset();
    }

    private void Update()
    {
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
            if (!Kit.AnyOpen()) return;
            SensorPicture pic = SensorPicture.Current;
            pic.PollStrobes();
            if (pic.Tick(Time.unscaledTime))
            {
                for (int i = 0; i < 3; i++)
                    if (Kit.Window(i) is SonarWindow sw && Kit.IsOpen(sw))
                        SonarSampler.Update(sw);
                Registry.RefreshAll();
            }
            else if (Time.unscaledTime >= nextAnimate)
            {
                nextAnimate = Time.unscaledTime + 0.2f;      // the scope's sweep: about five redraws a second
                for (int i = 0; i < Registry.Surfaces.Count; i++)
                    if (Registry.Surfaces[i].Animated) Registry.Surfaces[i].InvalidateVisual();
            }
        }
        catch (Exception ex)
        {
            if (errors++ < 5) ModLog.Error("update: " + ex);
        }
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
