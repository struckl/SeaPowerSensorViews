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
        nm = Conv.Nm(meters);
    }

    /// <summary>
    /// Moves a sensor's own fix (bearing and range from <paramref name="origin"/>) onto the contact's plotted position, the map marker,
    /// when the game plots it: lines and scope marks then end on the marker instead of on an older estimate of one sensor.
    /// </summary>
    public static void ToPlot(ContactRow cr, GeoPosition origin, ref float bearing, ref float nm)
    {
        if (!cr.HasPlot) return;
        BearingRange(origin, cr.Plot, out float pb, out float pnm);
        if (pnm > 0.01f) { bearing = pb; nm = pnm; }
    }

    /// <summary>
    /// Where a bearing line of this hit points from <paramref name="origin"/>: once the game holds the contact's position from a ranging
    /// sensor, at that position; until then along the sensor's own last bearing, which ages as both ships move.
    /// </summary>
    public static float BearingNow(ContactRow cr, Hit hit, GeoPosition origin)
    {
        float bearing = hit.Bearing, nm = 0f;
        if (cr.HasRangedFix) ToPlot(cr, origin, ref bearing, ref nm);
        return bearing;
    }

    /// <summary>
    /// The game's Sonar.IsInBaffles: <paramref name="rel"/> is the bearing from the bow (starboard positive, -180..180), the sector
    /// runs from <paramref name="from"/> to <paramref name="to"/> and may wrap through the stern. NaN = no baffles.
    /// </summary>
    public static bool InBaffles(float from, float to, float rel)
    {
        if (float.IsNaN(from)) return false;
        return from < to ? (from < rel && rel < to) : (to >= rel || rel >= from);
    }

    /// <summary>Relative bearing from <paramref name="heading"/> to <paramref name="bearing"/>, -180..180, starboard positive.</summary>
    public static float Relative(float heading, float bearing) => UnityEngine.Mathf.DeltaAngle(heading, bearing);
}

/// <summary>The object selected in the game.</summary>
internal static class GameSelection
{
    public static ObjectBase Current()
    {
        try { return Singleton<RenderPosition>.InstanceExists() ? Singleton<RenderPosition>.Instance.getSelectedObject() : null; }
        catch (Exception) { return null; }   // between missions the camera has no selection to ask about
    }
}
