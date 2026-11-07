using System;
using System.Reflection;
using SeaPower;

namespace SeaPowerSensorViews;

/// <summary>
/// Terrain height at a place, for every game build: <c>TerrainUtils.GetHeightAt</c> gained parameters over time (0.8.7 and the
/// first globe beta: (GeoPosition, bool, bool); globe beta 261107: one more bool), so a direct call compiled against one build
/// fails on the other. The method is found once by reflection and called with its own defaults for everything after the place.
/// </summary>
internal static class Terrain
{
    private static MethodInfo method;
    private static object[] args;
    private static bool looked, reported;

    /// <summary>Height in game units (negative below sea level); NaN if the game's method is not there.</summary>
    public static float HeightAt(GeoPosition geo)
    {
        if (!looked)
        {
            looked = true;
            foreach (MethodInfo m in typeof(TerrainUtils).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "GetHeightAt" || m.ReturnType != typeof(float)) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 0 || ps[0].ParameterType != typeof(GeoPosition)) continue;
                bool ok = true;
                var a = new object[ps.Length];
                for (int i = 1; i < ps.Length; i++)
                {
                    if (ps[i].HasDefaultValue) a[i] = ps[i].DefaultValue;
                    else if (ps[i].ParameterType == typeof(bool)) a[i] = false;
                    else { ok = false; break; }
                }
                if (!ok) continue;
                if (method == null || ps.Length < method.GetParameters().Length) { method = m; args = a; }
            }
            if (method == null) ModLog.Warn("TerrainUtils.GetHeightAt not found (game update?) - the sonar display shows no seabed depth.");
        }
        if (method == null) return float.NaN;
        try
        {
            args[0] = geo;
            return (float)method.Invoke(null, args);
        }
        catch (Exception ex)
        {
            if (!reported) ModLog.Warn("terrain height: " + (ex.InnerException ?? ex).Message);
            reported = true;
            return float.NaN;
        }
    }
}
