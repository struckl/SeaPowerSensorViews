using System;
using System.Collections.Generic;
using MapControl;
using SeaPower;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// The map side of the mod: the two Layers-menu entries ("Sensor lines", "Sensor coverage"), the map controls they are laid into,
/// and the data behind them. Everything here is touched only after a mission's UI exists (so after NoesisGUI runs).
/// </summary>
internal static class MapSet
{
    private static LayerEntry linesLayer, coverageLayer;
    private static readonly List<MapView> Views = new List<MapView>();
    private static readonly HashSet<MapBase> Undecoratable = new HashSet<MapBase>();
    private static readonly LineSet Lines = new LineSet();
    private static readonly CoverageJob Coverage = new CoverageJob();
    private static bool active;
    private static int linesPictureVersion = -1, linesSettings = -1, linesSelection = -1;
    private static int errors;

    public static bool LinesOn => linesLayer != null ? linesLayer.Visible : Settings.LinesVisible.Value;
    public static bool CoverageOn => coverageLayer != null ? coverageLayer.Visible : Settings.CoverageVisible.Value;

    /// <summary>Whether the snapshot has to be kept fresh for the map even when no window is open.</summary>
    public static bool NeedsPicture => active && Views.Count > 0 && (LinesOn || CoverageOn);

    public static void MissionReady()
    {
        active = true;
        linesLayer ??= new LayerEntry(603, "SEWERLABS_SENSOR_LINES", "Sensor lines (who sees what)", Settings.LinesVisible);
        coverageLayer ??= new LayerEntry(604, "SEWERLABS_SENSOR_COVERAGE", "Sensor coverage (selected unit)", Settings.CoverageVisible);
        linesLayer.EnsureRegistered();
        coverageLayer.EnsureRegistered();
        Coverage.Reset();
        linesPictureVersion = -1;
    }

    public static void MissionEnded()
    {
        active = false;
        foreach (MapView v in Views) v.Detach();
        Views.Clear();
        Undecoratable.Clear();
        Lines.Lines.Clear();
        Lines.Version++;
        LineBuilder.LastTolerance = double.MaxValue;
        Coverage.Reset();
    }

    public static void ToggleLines() => linesLayer?.Toggle();

    public static void ToggleCoverage() => coverageLayer?.Toggle();

    /// <summary>Every frame from each map control that shows the live map: lays the layers into the ones not yet decorated.</summary>
    public static void Rendered(object rendered)
    {
        if (!active) return;
        try
        {
            MapBase map = ((RenderedLayer)rendered).ParentMap;
            if (map == null || Undecoratable.Contains(map)) return;
            foreach (MapView known in Views)
                if (known.Map == map) return;
            try
            {
                Views.Add(new MapView(map));
                ModLog.Info("Sensor lines and coverage laid into a map.");
            }
            catch (Exception ex)
            {
                Undecoratable.Add(map);
                ModLog.Warn("Could not add the sensor layers to a map: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (errors++ < 5) ModLog.Error("map layers: " + ex);
        }
    }

    /// <summary>Every frame while a mission runs.</summary>
    public static void Update(double realNow)
    {
        if (!active) return;
        try
        {
            linesLayer.EnsureRegistered();
            coverageLayer.EnsureRegistered();
            for (int i = Views.Count - 1; i >= 0; i--)
            {
                if (!Views[i].Alive) { Views[i].Detach(); Views.RemoveAt(i); }
            }
            if (Views.Count == 0) return;
            bool lines = LinesOn, cover = CoverageOn;
            SensorPicture pic = SensorPicture.Current;
            UnitRow selected = SelectedRow(pic);
            if (lines)
            {
                int selUid = selected?.Uid ?? -1;
                // a line counts as moved at one screen pixel of the most zoomed-in map; zoomed in further, compare again at once
                double tolerance = double.MaxValue;
                foreach (MapView v in Views) tolerance = Math.Min(tolerance, v.DegreesPerPixel());
                if (tolerance == double.MaxValue) tolerance = 0.002;
                bool finer = tolerance < LineBuilder.LastTolerance * 0.5;
                if (pic.Version != linesPictureVersion || Settings.Version != linesSettings || selUid != linesSelection || finer)
                {
                    linesPictureVersion = pic.Version;
                    linesSettings = Settings.Version;
                    linesSelection = selUid;
                    long tl = Perf.Start();
                    LineBuilder.Build(pic, selected, Lines, tolerance);
                    Perf.Stop(Perf.Part.Lines, tl);
                }
            }
            if (cover)
            {
                long tc = Perf.Start();
                Coverage.Tick(realNow, selected);
                Perf.Stop(Perf.Part.Coverage, tc);
            }
            foreach (MapView v in Views)
                v.Update(lines, Lines, cover, Coverage.Result);
        }
        catch (Exception ex)
        {
            if (errors++ < 5) ModLog.Error("map update: " + ex);
        }
    }

    private static UnitRow SelectedRow(SensorPicture pic)
    {
        try
        {
            ObjectBase sel = Singleton<RenderPosition>.InstanceExists() ? Singleton<RenderPosition>.Instance.getSelectedObject() : null;
            return pic.RowFor(sel);
        }
        catch (Exception) { return null; }
    }
}
