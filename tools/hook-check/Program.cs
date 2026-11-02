using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

// hook-check --against <Managed folder>   (checks the fixed list below; exit code 1 when something is missing)
internal static class Program
{
    private static int Main(string[] args)
    {
        string dir = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--against") dir = args[i + 1];
        if (dir == null) { Console.WriteLine("usage: hook-check --against <Managed folder>"); return 2; }
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(dir);
        var module = ModuleDefinition.ReadModule(Path.Combine(dir, "Seapower-Scripts.dll"), new ReaderParameters { AssemblyResolver = resolver });
        int bad = 0;

        // Harmony targets (Hooks.cs): type, method, expected parameter list ("" = none; null = any, unique by name)
        bad += Method(module, "SeapowerUI.ViewModels.MainGameViewModel", "OnStart", "");
        bad += Method(module, "SeaPower.GameMain", "OnDestroy", "");
        bad += Method(module, "MapControl.RenderedLayer", "OnRender", null);

        // reflection lookups of GameApi.NewLayer (globe build only): GizmoLayerManager.Entry and MapLayer(MapDisplayState, in Entry, bool)
        TypeDefinition manager = module.GetType("SeaPower.LiveMap.GizmoLayerManager") ?? module.Types.FirstOrDefault(t => t.Name == "GizmoLayerManager");
        bool globe = module.GetType("SeapowerUI.MapDisplayState") != null;
        Console.WriteLine($"  game has MapDisplayState (globe build): {globe}");
        if (globe)
        {
            TypeDefinition entry = manager?.NestedTypes.FirstOrDefault(t => t.Name == "Entry");
            bad += Report("GizmoLayerManager+Entry nested type", entry != null);
            TypeDefinition layer = module.GetType("SeapowerUI.MapLayer");
            var ctor = layer?.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 3 &&
                m.Parameters[0].ParameterType.Name == "MapDisplayState" && m.Parameters[1].ParameterType.IsByReference && m.Parameters[2].ParameterType.Name == "Boolean");
            bad += Report("MapLayer(MapDisplayState, ref Entry, bool) constructor", ctor != null);
            bad += Report("MapDisplayState.Tactical", module.GetType("SeapowerUI.MapDisplayState")?.Properties.Any(p => p.Name == "Tactical") == true
                                                      || module.GetType("SeapowerUI.MapDisplayState")?.Fields.Any(f => f.Name == "Tactical") == true);
            bad += Report("MapBase.TryLocationToView", module.GetType("MapControl.MapBase")?.Methods.Any(m => m.Name == "TryLocationToView") == true);
        }
        else
        {
            bad += Report("GizmoLayerManager.Instance", manager?.Properties.Any(p => p.Name == "Instance") == true || manager?.Fields.Any(f => f.Name == "Instance") == true);
            bad += Report("MapLayer(int, string, bool) constructor", module.GetType("SeapowerUI.MapLayer")?.Methods
                .Any(m => m.IsConstructor && m.Parameters.Count == 3 && m.Parameters[0].ParameterType.Name == "Int32") == true);
        }
        bad += Report("SonarAcoustics.OwnNoiseDB (bound by reflection in Compat.cs)", module.GetType("SeaPower.SonarAcoustics")?.Methods.Any(m => m.Name == "OwnNoiseDB") == true);
        Console.WriteLine(bad == 0 ? "all present" : $"{bad} MISSING");
        return bad == 0 ? 0 : 1;
    }

    private static int Method(ModuleDefinition module, string type, string name, string parameters)
    {
        TypeDefinition t = module.GetType(type);
        var found = t?.Methods.Where(m => m.Name == name).ToList();
        if (found == null || found.Count == 0) return Report($"{type}.{name}", false);
        string list = string.Join(", ", found.Select(m => $"({string.Join(",", m.Parameters.Select(p => p.ParameterType.Name))})"));
        bool ok = parameters == null ? found.Count == 1 : found.Any(m => string.Join(",", m.Parameters.Select(p => p.ParameterType.Name)) == parameters);
        return Report($"{type}.{name} {list}" + (parameters == null && found.Count > 1 ? " (not unique by name!)" : ""), ok);
    }

    private static int Report(string what, bool ok)
    {
        Console.WriteLine($"  [{(ok ? "ok" : "MISSING")}] {what}");
        return ok ? 0 : 1;
    }
}
