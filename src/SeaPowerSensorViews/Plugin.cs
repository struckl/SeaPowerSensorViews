using System;
using System.Runtime.CompilerServices;
using AnchorChain;
using HarmonyLib;
using UnityEngine;

namespace SeaPowerSensorViews;

/// <summary>
/// Entry point, loaded by Anchor Chain (the Sea Power chain loader on top of BepInEx 5).
/// <para>Sea Power exists in two versions with different map code (0.8.7's flat map and the 3D-globe build); the windows are
/// the same on both but the map layers are not (layer list, projection), and a mod compiled against one fails on the
/// other. So the package carries two builds of the same sources: <c>SeaPowerSensorViews.dll</c> for the globe build and
/// <c>SeaPowerSensorViewsFlat.dll</c> (<c>FLATMAP</c> defined) for 0.8.7. Each looks at the game it runs on and stays off when
/// it is not the one it was made for.</para>
/// </summary>
[ACPlugin(PluginGuid, Name, Version)]
public class Plugin : IAnchorChainMod
{
    public const string Guid = "dev.sewerlabs.seapowersensorviews";
    public const string Version = "0.2.0";

#if FLATMAP
    // Anchor Chain skips a plugin whose GUID it has already seen, so the two builds register under different ones.
    public const string PluginGuid = Guid + ".flatmap";
    public const string Name = "Sea Power: R.A.T.S. Sensor Views (flat map)";
#else
    public const string PluginGuid = Guid;
    public const string Name = "Sea Power: R.A.T.S. Sensor Views";
#endif

    // Anchor Chain loads assemblies with Assembly.LoadFile, so the same DLL found in two folders would be two
    // separate assemblies with separate statics. The marker lives on the AppDomain so only the first one starts.
    private const string LoadedMarker = Guid + ".loaded";

    public void TriggerEntryPoint()
    {
        // Only the 3D-globe build has MapDisplayState. Nothing in this method touches a game type that differs.
        bool globeGame = Type.GetType("SeapowerUI.MapDisplayState, Seapower-Scripts") != null;
#if FLATMAP
        if (globeGame)
        {
            ModLog.Info("This game has the globe map: the globe build runs, the flat-map build stays off.");
            return;
        }
#else
        if (!globeGame)
        {
            ModLog.Info("This game has the flat map: the flat-map build runs, the globe build stays off.");
            return;
        }
#endif
        Start();
    }

    // Apart from the check above, so nothing in here is compiled on a game it isn't for.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start()
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
            if (!Hooks.ApplyGameHooks(new Harmony(Guid)))
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
