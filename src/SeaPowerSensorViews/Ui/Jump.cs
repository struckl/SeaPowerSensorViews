using System;
using SeaPower;
using SeaPowerSensorViews.Data;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

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

/// <summary>Double click on the sensor board: bring the game's camera to the unit or contact.</summary>
internal static class Jump
{
    public static void ToUnit(UnitRow row)
    {
        try
        {
            if (row?.Unit == null || row.Unit.IsDestroyed || !Singleton<RenderPosition>.InstanceExists()) return;
            Singleton<RenderPosition>.Instance.switchToObject(row.Unit, true);
        }
        catch (Exception ex) { ModLog.Warn("Jump to unit failed: " + ex.Message); }
    }

    /// <summary>
    /// To where the sensors think the contact is (the newest ranged fix, preferring <paramref name="prefer"/>'s): the real object is
    /// never used, so the jump gives away nothing the track does not. A bearing-only contact has no place: the camera goes to the
    /// unit that holds it instead.
    /// </summary>
    public static void ToContact(ContactRow cr, UnitRow prefer)
    {
        try
        {
            if (cr == null || !Singleton<RenderPosition>.InstanceExists()) return;
            Hit best = null;
            foreach (Hit h in cr.Hits)
            {
                if (h.BearingOnly || float.IsNaN(h.RangeM) || h.Unit?.Unit == null) continue;
                bool better = best == null
                    || (h.Unit == prefer) != (best.Unit == prefer) ? h.Unit == prefer : h.FixTime > best.FixTime;
                if (better) best = h;
            }
            if (best == null)
            {
                foreach (Hit h in cr.Hits)
                    if (h.Unit?.Unit != null && (best == null || h.FixTime > best.FixTime)) best = h;
                if (best != null) ToUnit(best.Unit);
                return;
            }
            GeoPosition at = best.Unit.Unit.getGeoPosition().OffsetByDistanceOnHeadingUnity(best.RangeM / 1000f, best.Bearing);
            RenderPosition cam = Singleton<RenderPosition>.Instance;
            cam.detachCameraFromTarget();
            cam.setGlobalPosition(at);
        }
        catch (Exception ex) { ModLog.Warn("Jump to contact failed: " + ex.Message); }
    }
}
