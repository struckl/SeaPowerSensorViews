using System.Diagnostics;
using System.Text;

namespace SeaPowerSensorViews;

/// <summary>
/// Time spent in the mod's parts, summed and written to the log every ten seconds (with "Verbose log" on), so a test shows
/// which part costs frame time. A part is timed with <c>long t = Perf.Start(); ... Perf.Stop(Perf.Part.X, t);</c>.
/// </summary>
internal static class Perf
{
    public enum Part { Picture, Lines, MapBuild, Coverage, WindowDraw, Count }

    private static readonly long[] Ticks = new long[(int)Part.Count];
    private static readonly int[] Calls = new int[(int)Part.Count];
    private static int frames;
    private static long windowStart;

    public static long Start() => Settings.VerboseLog.Value ? Stopwatch.GetTimestamp() : 0;

    public static void Stop(Part part, long start)
    {
        if (start == 0) return;
        Ticks[(int)part] += Stopwatch.GetTimestamp() - start;
        Calls[(int)part]++;
    }

    /// <summary>Once per frame from the host.</summary>
    public static void Frame()
    {
        if (!Settings.VerboseLog.Value) return;
        long now = Stopwatch.GetTimestamp();
        if (windowStart == 0) windowStart = now;
        frames++;
        if (now - windowStart < Stopwatch.Frequency * 10) return;
        var sb = new StringBuilder("perf over ").Append(frames).Append(" frames (ms per frame, calls):");
        for (int i = 0; i < (int)Part.Count; i++)
        {
            sb.Append(' ').Append((Part)i).Append(' ')
              .Append((Ticks[i] * 1000.0 / Stopwatch.Frequency / frames).ToString("0.00")).Append(" (").Append(Calls[i]).Append(')');
            Ticks[i] = 0;
            Calls[i] = 0;
        }
        ModLog.Info(sb.ToString());
        frames = 0;
        windowStart = now;
    }
}
