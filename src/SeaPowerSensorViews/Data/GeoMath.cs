using System;
using SeaPower;

namespace SeaPowerSensorViews.Data;

/// <summary>Bearing and distance between two positions (great circle).</summary>
internal static class GeoMath
{
    public static void BearingRange(GeoPosition a, GeoPosition b, out float bearing, out float nm)
    {
        const double Rad = Math.PI / 180.0, EarthNm = 3440.065;
        double la = a._latitude * Rad, lb = b._latitude * Rad, dl = (b._longitude - a._longitude) * Rad;
        double s = Math.Sin((lb - la) / 2), t = Math.Sin(dl / 2);
        double h = s * s + Math.Cos(la) * Math.Cos(lb) * t * t;
        nm = (float)(2 * EarthNm * Math.Asin(Math.Min(1.0, Math.Sqrt(h))));
        double y = Math.Sin(dl) * Math.Cos(lb), x = Math.Cos(la) * Math.Sin(lb) - Math.Sin(la) * Math.Cos(lb) * Math.Cos(dl);
        bearing = SensorPicture.Wrap360((float)(Math.Atan2(y, x) / Rad));
    }
}
