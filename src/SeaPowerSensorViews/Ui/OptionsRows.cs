using System;

namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: a leftover DLL of an older release must not share these names

/// <summary>
/// The rows of the Sensor Views tab in the game's Options menu (the tab itself: Rig.Options, in R.A.T.S. Rigging). Hot keys
/// and the rarer settings stay in the config file.
/// </summary>
internal static class OptionsRows
{
    public static void Fill(OptionsBuilder b)
    {
        b.Note("Changes apply at once and are kept by the mod (Accept and Revert on this page do not affect them). " +
            "Hot keys and further settings: BepInEx\\config\\" + Plugin.Guid + ".cfg.");
        Lines(b);
        Coverage(b);
        Windows(b);
    }

    /// <summary>" or with KEY" for the hot key as the player has set it; nothing when there is none.</summary>
    private static string KeyHint(BepInEx.Configuration.ConfigEntry<string> key) =>
        string.IsNullOrWhiteSpace(key.Value) ? "" : " or with " + key.Value;

    private static void Lines(OptionsBuilder b)
    {
        b.Header("Sensor lines on the map");
        b.Note("Switch the layer itself in the map's Layers menu" + KeyHint(Settings.LinesKey) + ".");
        b.Choice("Units shown", Settings.LinesWho, new[] { Settings.SelectedOnly, Settings.AllUnits },
            "Selected only: the lines of the unit you have selected (nothing while no unit is selected). All: every own unit's lines, the selected unit's bright. With many units, all lines are a lot to draw.");
        b.Check("Hide ESM of tracked contacts", Settings.HideTrackedEsm,
            "Leave out the ESM bearing line and cone of a contact that a radar holds as a live track (the game linked the emission to it). Also a switch on the board and in the bottom-bar flyout.");
        b.Check("Show lost contacts", Settings.LostLines,
            "A contact a sensor has just lost keeps a faint line for the lost contact memory time.");
        b.Check("Fill wedges when zoomed in", Settings.ZoomedWedges,
            "Off: zoomed in so far that a bearing wedge is longer than the screen, only its bearing line is drawn (faster). On: always filled.");
        b.Slider("Dimmed line opacity", "Lines of the units that are not selected (with Units shown = All).",
            Settings.DimOpacity, 0f, 1f, 0.05f, v => Math.Round(v * 100) + "%");
        b.Slider("Line weight", "Multiplier on the game's own map line weight.",
            Settings.LineWidth, 0.5f, 4f, 0.25f, v => v.ToString("0.##") + "x");
        b.Slider("Bearing wedge length", "Length of the line and wedge for bearing-only contacts (ESM, passive sonar).",
            Settings.WedgeNm, 5f, 300f, 5f, v => Math.Round(v) + " nm");
    }

    private static void Coverage(OptionsBuilder b)
    {
        b.Header("Coverage of the selected unit");
        b.Note("Switch the layer in the map's Layers menu" + KeyHint(Settings.CoverageKey) + ".");
        b.Check("Radar", Settings.CoverRadar, "Search radar coverage against the target altitude (the scope's slider).");
        b.Check("Terrain shadows", Settings.TerrainShadows, "Cut the radar coverage where terrain blocks the line of sight.");
        b.Check("ESM", Settings.CoverEsm, "ESM listening range against an emitter like the unit's own search radar.");
        b.Check("Visual", Settings.CoverVisual, "Visual range, limited by the horizon.");
        b.Check("Passive sonar", Settings.CoverSonar, "Passive sonar range with the dent of the baffle sector.");
    }

    private static void Windows(OptionsBuilder b)
    {
        b.Header("Windows");
        b.Check("Follow the selected unit", Settings.FollowSelection,
            "The scope, the sonar display and the board's sensor panel show the unit selected in the game. Off: the unit clicked on the board.");
        b.Check("Radar and sonar: period look", Settings.RetroLook,
            "Green phosphor tubes like the radar and sonar displays of the period. Off: the game's own colours. Applies to windows opened afterwards.");
        b.Check("Radar scope: zoom animation", Settings.ZoomAnimation,
            "The contacts glide to the new scale when the range changes, instead of jumping.");
        b.Check("Radar scope: blips appear when the sweep passes", Settings.RealisticSweep,
            "On: a radar contact is painted when the sweep passes over it and fades until the next pass. Off: contacts are always shown at their latest position.");
        b.Check("Sonar display: own torpedoes", Settings.SonarOwnTorpedoes,
            "Your own torpedoes in the water show on the sonar display when that sonar would hear them, so a wire-guided torpedo can be followed by ear.");
        b.Slider("Lost contact memory", "How long a lost contact stays on the board and scope, dimmed.",
            Settings.MemorySeconds, 0f, 600f, 10f, v => Math.Round(v) + " s");
        b.Check("Bottom bar buttons", Settings.BarButtons,
            "The Board, Scope and Sonar buttons at the right end of the bottom bar. Takes effect at the next mission.");
    }
}
