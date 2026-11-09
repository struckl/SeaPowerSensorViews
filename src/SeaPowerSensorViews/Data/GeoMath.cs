using System;
using SeaPower;

namespace SeaPowerSensorViews.Data;

/// <summary>Bearing and distance between two positions, measured the way the game measures them.</summary>
internal static class GeoMath
{
    /// <summary>
    /// The game's own measure: the one its sensors use for a track's bearing and the one that fits the offset the map lines are drawn
    /// with. A great-circle bearing differs from it away from the equator (on the flat map by up to about 10 degrees at 45 N and 18 at
    /// 60 N), which turned lines away from the marker they were meant to end on.
    /// </summary>
    public static void BearingRange(GeoPosition a, GeoPosition b, out float bearing, out float nm)
    {
        (float heading, float meters) = a.HeadingRangeMeters(b);
        bearing = SensorPicture.Wrap360(heading);
        nm = meters / 1852f;
    }

    /// <summary>
    /// Where a bearing line of this hit points from <paramref name="origin"/>: once the game holds the contact's position from a ranging
    /// sensor, at that position; until then along the sensor's own last bearing, which ages as both ships move.
    /// </summary>
    public static float BearingNow(ContactRow cr, Hit hit, GeoPosition origin)
    {
        if (!cr.HasRangedFix) return hit.Bearing;
        BearingRange(origin, cr.Plot, out float bearing, out float nm);
        return nm > 0.01f ? bearing : hit.Bearing;
    }
}
