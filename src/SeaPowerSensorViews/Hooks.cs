#if FLATMAP
using SeaPowerSensorViews.Ui;
#else
using SeaPowerSensorViews.Globe.Ui;
#endif

namespace SeaPowerSensorViews;

/// <summary>
/// Every game method the mod hooks (HookRegistry, in the shared skeleton):
/// - MainGameViewModel.OnStart / GameMain.OnDestroy - mission UI ready / mission unloading (MissionHooks), at load.
/// - MapControl.RenderedLayer.OnRender - runs every frame for the map control that shows the live map; hands the map over so the
/// sensor-line and coverage layers can be laid into it. Applied once NoesisGUI runs (patching it earlier would run Noesis
/// dependency-property registration before Noesis exists - a native crash).
/// - OptionsMenuView..ctor - the Sensor Views tab of the Options menu (OptionsPage), also once NoesisGUI runs; optional.
/// All resolve in both games' Seapower-Scripts.dll with the same signature (tools/hook-check).
/// </summary>
internal static class Hooks
{
    public static void Declare()
    {
        MissionHooks.Declare();
        HookRegistry.Add("Map", HookStage.AfterNoesis, "no sensor lines or coverage on the map")
            .Hook("MapControl.RenderedLayer", "OnRender", null, typeof(Hooks), postfix: nameof(MapRendered));
        OptionsPage.Declare("Sensor Views", OptionsRows.Fill);
    }

    private static void MapRendered(object __instance) => Map.MapSet.Rendered(__instance);
}
