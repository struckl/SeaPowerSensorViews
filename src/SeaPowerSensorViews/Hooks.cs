using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SeaPowerSensorViews;

/// <summary>
/// Every game method the mod hooks, in two stages; all targets of a stage are resolved before anything is patched.
/// <list type="bullet">
/// <item><c>MainGameViewModel.OnStart</c> / <c>GameMain.OnDestroy</c> - mission UI ready / mission unloading.</item>
/// <item><c>MapControl.RenderedLayer.OnRender</c> - runs every frame for the map control that shows the live map; hands the
/// map over so the sensor-line and coverage layers can be laid into it. Applied once NoesisGUI runs (patching it earlier
/// would run Noesis dependency-property registration before Noesis exists - a native crash), see <see cref="Host"/>.</item>
/// </list>
/// All three resolve in both games' Seapower-Scripts.dll with the same signature (tools/hook-check).
/// </summary>
internal static class Hooks
{
    private sealed class Hook
    {
        public string Type, Method, Prefix, Postfix;
        public Type[] Args;                   // null = unique by name
    }

    private static readonly Hook[] GameHooks =
    {
        new Hook { Type = "SeapowerUI.ViewModels.MainGameViewModel", Method = "OnStart", Args = Type.EmptyTypes, Postfix = nameof(MissionReady) },
        new Hook { Type = "SeaPower.GameMain", Method = "OnDestroy", Args = Type.EmptyTypes, Prefix = nameof(MissionUnloading) },
    };

    private static readonly Hook[] MapHooks =
    {
        new Hook { Type = "MapControl.RenderedLayer", Method = "OnRender", Postfix = nameof(MapRendered) },
    };

    private static Harmony harmony;

    public static bool ApplyGameHooks(Harmony instance)
    {
        harmony = instance;
        return Apply(GameHooks, "mod inactive");
    }

    public static bool ApplyMapHooks() => harmony != null && Apply(MapHooks, "no sensor lines or coverage on the map");

    private static bool Apply(Hook[] hooks, string consequence)
    {
        var resolved = new List<(Hook hook, MethodBase target)>();
        foreach (Hook hook in hooks)
        {
            Type type = AccessTools.TypeByName(hook.Type);
            MethodBase target = type == null ? null : AccessTools.Method(type, hook.Method, hook.Args);
            if (target == null)
            {
                ModLog.Error(hook.Type + "." + hook.Method + " not found (game updated?) - " + consequence + ".");
                return false;
            }
            resolved.Add((hook, target));
        }
        foreach ((Hook hook, MethodBase target) in resolved)
        {
            harmony.Patch(target,
                prefix: hook.Prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Hooks), hook.Prefix)),
                postfix: hook.Postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(Hooks), hook.Postfix)));
        }
        return true;
    }

    private static void MissionReady() => Host.Instance?.MissionReady();

    private static void MissionUnloading() => Host.Instance?.MissionEnded();

    private static void MapRendered(object __instance) => Host.Instance?.MapRendered(__instance);
}
