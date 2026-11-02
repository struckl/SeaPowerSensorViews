using System;
using HarmonyLib;

namespace SeaPowerSensorViews;

/// <summary>
/// The only two Harmony hooks: mission UI ready and mission unloading (the same plain game methods Pop-Out uses). Both
/// targets are resolved before anything is patched. Everything else is read from the game, nothing else is patched.
/// </summary>
internal static class Hooks
{
    public static bool Apply(Harmony harmony)
    {
        Type vm = AccessTools.TypeByName("SeapowerUI.ViewModels.MainGameViewModel");
        Type main = AccessTools.TypeByName("SeaPower.GameMain");
        var onStart = vm == null ? null : AccessTools.Method(vm, "OnStart", Type.EmptyTypes);
        var onDestroy = main == null ? null : AccessTools.Method(main, "OnDestroy", Type.EmptyTypes);
        if (onStart == null || onDestroy == null)
        {
            ModLog.Error("MainGameViewModel.OnStart / GameMain.OnDestroy not found (game updated?) - mod inactive.");
            return false;
        }
        harmony.Patch(onStart, postfix: new HarmonyMethod(typeof(Hooks), nameof(MissionReady)));
        harmony.Patch(onDestroy, prefix: new HarmonyMethod(typeof(Hooks), nameof(MissionUnloading)));
        return true;
    }

    private static void MissionReady() => Host.Instance?.MissionReady();
    private static void MissionUnloading() => Host.Instance?.MissionEnded();
}
