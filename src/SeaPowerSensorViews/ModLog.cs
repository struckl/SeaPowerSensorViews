using BepInEx.Logging;

namespace SeaPowerSensorViews;

internal static class ModLog
{
    private static readonly ManualLogSource Source = Logger.CreateLogSource("SeaPowerSensorViews");

    public static void Info(string message) => Source.LogInfo(message);
    public static void Warn(string message) => Source.LogWarning(message);
    public static void Error(string message) => Source.LogError(message);

    public static void Debug(string message)
    {
        if (Settings.VerboseLog != null && Settings.VerboseLog.Value)
            Source.LogInfo("[dbg] " + message);
    }
}
