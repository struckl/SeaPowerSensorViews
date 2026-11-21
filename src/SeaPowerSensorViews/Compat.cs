using System;
using System.Reflection;
using SeaPower;

namespace SeaPowerSensorViews;

/// <summary>
/// Calls bound at run time, so a game update that changes a signature does not break the DLL.
/// SonarAcoustics.OwnNoiseDB has four optional parameters after its first eight: a compiled call has to pass them all, so it is
/// found by reflection and called with the game's own defaults for the rest.
/// </summary>
internal static class Compat
{
    private static MethodInfo ownNoise;
    private static ParameterInfo[] ownNoiseParams;
    private static bool resolved, failed;

    public static float OwnNoiseDb(float baseNoise, int rafting, float speedKts, float flowNoise, SonarParameters.SonarType type,
        float additionalNoise, float cavitationIntensity, float cavitationNoise)
    {
        if (!resolved)
        {
            resolved = true;
            foreach (MethodInfo m in typeof(SonarAcoustics).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "OwnNoiseDB") continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length >= 8 && p[4].ParameterType == typeof(SonarParameters.SonarType) && (ownNoise == null || p.Length > ownNoiseParams.Length))
                {
                    ownNoise = m;
                    ownNoiseParams = p;
                }
            }
            if (ownNoise == null) Rig.Log.Warn("SonarAcoustics.OwnNoiseDB not found (game updated?) - the sonar display shows no own noise.");
        }
        if (ownNoise == null) return float.NaN;
        var args = new object[ownNoiseParams.Length];
        args[0] = baseNoise; args[1] = rafting; args[2] = speedKts; args[3] = flowNoise; args[4] = type;
        args[5] = additionalNoise; args[6] = cavitationIntensity; args[7] = cavitationNoise;
        for (int i = 8; i < args.Length; i++)
            args[i] = ownNoiseParams[i].HasDefaultValue ? ownNoiseParams[i].DefaultValue : null;
        try { return (float)ownNoise.Invoke(null, args); }
        catch (Exception ex)
        {
            // It fails the same way on every call (once a second while the sonar display is open): said once.
            if (!failed) Rig.Log.Warn("SonarAcoustics.OwnNoiseDB failed - the sonar display shows no own noise: " + (ex.InnerException ?? ex).Message);
            failed = true;
            return float.NaN;
        }
    }
}
