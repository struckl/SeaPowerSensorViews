using System;
using SeaPower;
using SeaPowerSensorViews.Data;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>Double click on the sensor board: bring the game's camera to the unit or contact.</summary>
internal static class Jump
{
    public static void ToUnit(UnitRow row)
    {
        try
        {
            if (row?.Unit == null || row.Unit.IsDestroyed || !Singleton<RenderPosition>.InstanceExists()) return;
            Singleton<RenderPosition>.Instance.switchToObject(row.Unit, true);
            ModLog.Debug("Jump to unit " + row.Name + ".");
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
            Hit best = BestFix(cr, prefer);
            if (best == null && !cr.HasPlot)      // only bearings and no place plotted: go to the unit that holds it
            {
                Hit newest = Newest(cr);
                if (newest != null) ToUnit(newest.Unit);
                return;
            }
            GeoPosition at = cr.HasPlot ? cr.Plot : best.Unit.Unit.getGeoPosition().OffsetByDistanceOnHeadingUnity(best.RangeM * Conv.KmPerMeter, best.Bearing);
            RenderPosition cam = Singleton<RenderPosition>.Instance;
            cam.detachCameraFromTarget();
            cam.setGlobalPosition(at);
            ModLog.Debug("Jump to contact " + cr.Label + ".");
        }
        catch (Exception ex) { ModLog.Warn("Jump to contact failed: " + ex.Message); }
    }

    /// <summary>The preferred unit's ranged fix if it has one, else the freshest ranged fix of any unit; null when no sensor ranges it.</summary>
    private static Hit BestFix(ContactRow cr, UnitRow prefer)
    {
        Hit best = null;
        foreach (Hit h in cr.Hits)
        {
            if (!h.Ranged || h.Unit?.Unit == null) continue;
            bool better = best == null
                || ((h.Unit == prefer) != (best.Unit == prefer) ? h.Unit == prefer : h.FixTime > best.FixTime);
            if (better) best = h;
        }
        return best;
    }

    private static Hit Newest(ContactRow cr)
    {
        Hit newest = null;
        foreach (Hit h in cr.Hits)
            if (h.Unit?.Unit != null && (newest == null || h.FixTime > newest.FixTime)) newest = h;
        return newest;
    }
}
