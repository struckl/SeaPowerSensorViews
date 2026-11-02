using System;
using System.Reflection;
using SeaPower;

namespace SeaPowerSensorViews;

/// <summary>
/// Calls that differ between the flat 0.8.7 and the globe build, bound at run time so one DLL serves both.
/// SonarAcoustics.OwnNoiseDB gained four optional parameters in the globe build (a compiled call with 8 arguments
/// is a different method there); the first eight are the same on both.
/// </summary>
internal static class Compat
{
    private static MethodInfo ownNoise;
    private static ParameterInfo[] ownNoiseParams;
    private static bool resolved;

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
            if (ownNoise == null) ModLog.Warn("SonarAcoustics.OwnNoiseDB not found (game updated?) - the sonar display shows no own noise.");
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
            ModLog.Debug("OwnNoiseDB: " + ex.Message);
            return float.NaN;
        }
    }
}
