using System;
using System.Collections.Generic;
using MapControl;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// The map side of the mod: the two Layers-menu entries ("Sensor lines", "Sensor coverage"), the map controls they are laid into,
/// and the data behind them. Everything here is touched only after a mission's UI exists (so after NoesisGUI runs).
/// </summary>
internal static class MapSet
{
    /// <summary>The fastest the lines are built again (s): about every snapshot of a running game (10 a second).</summary>
    private const double LinesMinInterval = 0.09;
    /// <summary>Line tolerance (degrees) when no map can say how big a pixel is.</summary>
    private const double FallbackTolerance = 0.002;

    private static LayerEntry linesLayer, coverageLayer;
    private static readonly List<MapView> Views = new List<MapView>();
    private static readonly HashSet<MapBase> Undecoratable = new HashSet<MapBase>();
    private static readonly LineSet Lines = new LineSet();
    private static readonly CoverageJob Coverage = new CoverageJob();
    private static bool active;
    private static int linesPictureVersion = -1, linesSettings = -1, linesSelection = -1;
    private static double linesNextAt;
    private static int dueGroup;
    private static int errors;

    public static bool LinesOn => linesLayer != null ? linesLayer.Visible : Settings.LinesVisible.Value;
    public static bool CoverageOn => coverageLayer != null ? coverageLayer.Visible : Settings.CoverageVisible.Value;

    /// <summary>Whether the snapshot has to be kept fresh for the map even when no window is open.</summary>
    public static bool NeedsPicture => active && Views.Count > 0 && (LinesOn || CoverageOn);

    public static void MissionReady()
    {
        active = true;
        linesLayer ??= new LayerEntry(Rig.Log, 603, "SEWERLABS_SENSOR_LINES", "Sensor lines (who sees what)", Settings.LinesVisible);
        coverageLayer ??= new LayerEntry(Rig.Log, 604, "SEWERLABS_SENSOR_COVERAGE", "Sensor coverage (selected unit)", Settings.CoverageVisible);
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
        Lines.Clear();
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
                Rig.Log.Info("Sensor lines and coverage laid into a map.");
            }
            catch (Exception ex)
            {
                Undecoratable.Add(map);
                Rig.Log.Warn("Could not add the sensor layers to a map: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (errors++ < 5) Rig.Log.Error("map layers: " + ex);
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
            UnitRow selected = pic.RowFor(GameSelection.Current());
            if (lines) UpdateLines(realNow, pic, selected);
            if (cover)
            {
                long tc = Perf.Start();
                Coverage.Tick(realNow, selected, CoverageJob.Inputs.FromSettings());
                Perf.Stop(Perf.Part.Coverage, tc);
            }
            foreach (MapView v in Views)
                v.Update(lines, Lines, cover, Coverage.Result);
        }
        catch (Exception ex)
        {
            if (errors++ < 5) Rig.Log.Error("map update: " + ex);
        }
    }

    private static void UpdateLines(double realNow, SensorPicture pic, UnitRow selected)
    {
        int selUid = selected?.Uid ?? -1;
        // a line counts as moved at one screen pixel of the most zoomed-in map; zoomed in further, compare again at once
        double tolerance = double.MaxValue;
        foreach (MapView v in Views) tolerance = Math.Min(tolerance, v.DegreesPerPixel());
        if (tolerance == double.MaxValue) tolerance = FallbackTolerance;
        bool finer = tolerance < Lines.Tolerance * 0.5;
        // Every snapshot (up to 10 a second while the game runs). Twice a second let the lines trail a moving unit visibly,
        // more so at higher time compression; Build still leaves the paths alone unless a line moved a pixel.
        bool pictureDue = pic.Version != linesPictureVersion && realNow >= linesNextAt;
        // settings that change the lines, a new selection or a closer zoom draw everything at once
        bool all = Settings.LineData.Version != linesSettings || selUid != linesSelection || finer;
        if (!pictureDue && !all) return;
        linesNextAt = realNow + LinesMinInterval;
        linesPictureVersion = pic.Version;
        // Other units' lines follow one group per build in turn (each about twice a second).
        dueGroup = (dueGroup + 1) % LineBuilder.SharedGroups;
        linesSettings = Settings.LineData.Version;
        linesSelection = selUid;
        long tl = Perf.Start();
        LineBuilder.Build(pic, selected, LineOptions(), Lines, tolerance, all, dueGroup);
        Perf.Stop(Perf.Part.Lines, tl);
    }

    private static LineBuilder.Options LineOptions() => new LineBuilder.Options
    {
        OnlySelected = Settings.LinesWho.Value == Settings.SelectedOnly,
        Lost = Settings.LostLines.Value,
        HideTrackedEsm = Settings.HideTrackedEsm.Value,
        Radar = Settings.LinesRadar.Value,
        Esm = Settings.LinesEsm.Value,
        Visual = Settings.LinesVisual.Value,
        Sonar = Settings.LinesSonar.Value,
        WedgeKm = Settings.WedgeNm.Value * Conv.KmPerNm,
    };
}
