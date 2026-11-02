using System;
using AnchorChain;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerSensorViews;

/// <summary>Entry point, loaded by Anchor Chain (the Sea Power chain loader on top of BepInEx 5).</summary>
[ACPlugin(Guid, Name, Version)]
public class Plugin : IAnchorChainMod
{
    public const string Guid = "dev.sewerlabs.seapowersensorviews";
    public const string Name = "Sea Power: R.A.T.S. Sensor Views";
    public const string Version = "0.1.0";

    private const string LoadedMarker = Guid + ".loaded";

    public void TriggerEntryPoint()
    {
        if (AppDomain.CurrentDomain.GetData(LoadedMarker) != null)
        {
            ModLog.Warn("Already loaded from another folder; skipping this copy.");
            return;
        }
        AppDomain.CurrentDomain.SetData(LoadedMarker, true);
        try
        {
            if (!ModState.TickedInModManager())
            {
                ModLog.Info("Unticked in the Mod Manager - inactive.");
                return;
            }
            Settings.Init();
            if (!Settings.Enabled.Value)
            {
                ModLog.Info("Disabled in config.");
                return;
            }
            if (!Hooks.Apply(new Harmony(Guid)))
                return;
            var go = new GameObject("SeaPowerSensorViews");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Host>();
            ModLog.Info(Name + " " + Version + " loaded.");
        }
        catch (Exception ex)
        {
            ModLog.Error("Startup failed, mod inactive: " + ex);
        }
    }
}
