using System;
using SeaPowerSensorViews.Data;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>How the windows write ages and bearings.</summary>
internal static class Fmt
{
    public static string Age(double seconds)
    {
        int s = (int)Math.Max(0, seconds);
        return s < 60 ? s + "s" : (s / 60) + ":" + (s % 60).ToString("00");
    }

    public static string Compass(float deg) => ((int)Math.Round(SensorPicture.Wrap360(deg)) % 360).ToString("000");
}
