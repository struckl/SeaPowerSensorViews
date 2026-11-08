using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SeaPowerSensorViews;

/// <summary>BepInEx config at <c>BepInEx/config/dev.sewerlabs.seapowersensorviews.cfg</c>.</summary>
internal static class Settings
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> VerboseLog;
    public static ConfigEntry<bool> BarButtons;
    public static ConfigEntry<string> BoardKey;
    public static ConfigEntry<string> ScopeKey;
    public static ConfigEntry<string> SonarKey;
    public static ConfigEntry<float> MemorySeconds;
    public static ConfigEntry<bool> FollowSelection;

    // map layers
    public static ConfigEntry<bool> LinesVisible;
    public static ConfigEntry<string> LinesWho;
    public static ConfigEntry<float> DimOpacity;
    public static ConfigEntry<float> LineWidth;
    public static ConfigEntry<bool> LostLines;
    public static ConfigEntry<float> WedgeNm;
    public static ConfigEntry<bool> ZoomedWedges;
    public static ConfigEntry<string> LinesKey;
    // quick switches (also entries of the map's Layers menu)
    public static ConfigEntry<bool> LinesAllUnits, LinesRadar, LinesEsm, LinesVisual, LinesSonar, HideTrackedEsm;
    public static ConfigEntry<bool> RealisticSweep, RetroLook;
    public static ConfigEntry<float> VisualLifetime;
    public static ConfigEntry<bool> ZoomAnimation;
    public static ConfigEntry<bool> CoverageVisible;
    public static ConfigEntry<string> CoverageKey;
    public static ConfigEntry<float> CoverageAltitude;
    public static ConfigEntry<float> TargetRcs;
    public static ConfigEntry<bool> CoverRadar, CoverEsm, CoverVisual, CoverSonar, TerrainShadows;
    public static ConfigEntry<string> VisualSize;
    public static ConfigEntry<int> Rays;

    /// <summary>Bumped whenever a setting changes, so the map layers know to rebuild.</summary>
    public static int Version;

    public const string AllUnits = "All, selected bright", SelectedOnly = "Selected only";
    private const int Layout = 2;          // 2 = 0.3.0: "Units shown" defaults to the selected unit only
    private static ConfigEntry<int> layoutVersion;

    public static void Init()
    {
        string path = Path.Combine(Paths.ConfigPath, Plugin.Guid + ".cfg");
        bool existed = File.Exists(path);
        var file = new ConfigFile(path, saveOnInit: true);
        Enabled = file.Bind("1. General", "Enable mod", true, "Master switch. Needs a restart.");
        VerboseLog = file.Bind("1. General", "Verbose log", false, "Write debug detail to BepInEx/LogOutput.log.");
        BarButtons = file.Bind("2. Opening the windows", "Bottom bar buttons", true,
            "Three small toggle buttons (Board, Scope, Sonar) at the right end of the bottom bar, next to the game's own window toggles.");
        BoardKey = file.Bind("2. Opening the windows", "Sensor board key", "Ctrl+Insert",
            "Hot key that toggles the sensor board. Modifiers Ctrl, Shift, Alt joined with +, then a Unity KeyCode name. Empty = none. " +
            "The defaults are keys the game does not bind by default (not F-keys: with Ctrl held the free camera would still pass them on).");
        ScopeKey = file.Bind("2. Opening the windows", "Radar scope key", "Ctrl+PageUp", "Hot key that toggles the radar scope.");
        SonarKey = file.Bind("2. Opening the windows", "Sonar display key", "Ctrl+PageDown", "Hot key that toggles the sonar display.");
        MemorySeconds = file.Bind("3. Views", "Lost contact memory (s)", 60f,
            new ConfigDescription("How long a contact a sensor lost stays on the board and scope, dimmed, with the age of its last fix.",
                new AcceptableValueRange<float>(0f, 600f)));
        FollowSelection = file.Bind("3. Views", "Follow the selected unit", true,
            "The scope, the sonar display and the board's sensor panel show the unit selected in the game. Off = the unit clicked on the board.");

        LinesVisible = file.Bind("4. Map: sensor lines", "Show sensor lines", true,
            "The 'Sensor lines' entry of the map's Layers menu (kept in step with it). Lines from each own unit to each contact it holds, " +
            "from the track estimates only.");
        LinesWho = file.Bind("4. Map: sensor lines", "Units shown", SelectedOnly,
            new ConfigDescription("Only the selected unit's lines (nothing while no unit is selected), or all own units' lines with the " +
                "selected unit's bright and the others dimmed. With many units, all lines are a lot to draw.",
                new AcceptableValueList<string>(SelectedOnly, AllUnits)));
        DimOpacity = file.Bind("4. Map: sensor lines", "Dimmed line opacity", 0.35f,
            new ConfigDescription("0 = invisible, 1 = as bright as the selected unit's.", new AcceptableValueRange<float>(0f, 1f)));
        LineWidth = file.Bind("4. Map: sensor lines", "Line weight", 1f,
            new ConfigDescription("Multiplier on the game's own line weight (its map styling ini uses 1 pixel).", new AcceptableValueRange<float>(0.5f, 4f)));
        LostLines = file.Bind("4. Map: sensor lines", "Show lost contacts", true,
            "A contact a sensor has just lost keeps a faint line for the 'lost contact memory' time (section 3).");
        WedgeNm = file.Bind("4. Map: sensor lines", "Bearing wedge length (nm)", 40f,
            new ConfigDescription("Length of the line and wedge drawn for bearing-only contacts (ESM, passive sonar) that have no plotted position yet. Once the game plots the contact (its map marker), the line and wedge run to the marker's distance instead.", new AcceptableValueRange<float>(5f, 300f)));
        ZoomedWedges = file.Bind("4. Map: sensor lines", "Fill wedges when zoomed in", false,
            "Off: zoomed in so far that a bearing wedge is longer than the screen, it is drawn as its bearing line only (the filled wedges " +
            "covered the whole map there and cost frame rate). On: always filled.");
        LinesKey = file.Bind("4. Map: sensor lines", "Toggle key", "Ctrl+Shift+PageUp", "Hot key for the sensor lines layer. Empty = none.");
        LinesAllUnits = file.Bind("4. Map: sensor lines", "Quick: all units", false,
            "Layers menu entry 'Sensor lines: all units'. On = every own unit's lines whatever 'Units shown' says (the selected unit's bright).");
        LinesRadar = file.Bind("4. Map: sensor lines", "Quick: radar lines", true, "Layers menu entry 'Sensor lines: radar' (search and fire-control radar).");
        LinesEsm = file.Bind("4. Map: sensor lines", "Quick: ESM lines", true, "Layers menu entry 'Sensor lines: ESM' (bearing lines and cones).");
        LinesVisual = file.Bind("4. Map: sensor lines", "Quick: visual lines", true, "Layers menu entry 'Sensor lines: visual'.");
        LinesSonar = file.Bind("4. Map: sensor lines", "Quick: sonar lines", true, "Layers menu entry 'Sensor lines: sonar' (passive and active).");
        HideTrackedEsm = file.Bind("4. Map: sensor lines", "Hide ESM of tracked contacts", true,
            "Layers menu entry 'Sensor lines: hide ESM of tracked contacts'. An ESM bearing line and cone is left out for a contact that a radar " +
            "(or other ranging sensor) holds as a live track: the game has linked the emission to that contact, so the cone adds nothing.");
        ZoomAnimation = file.Bind("3. Views", "Radar scope: zoom animation", true,
            "On: when the range changes (mouse wheel, - and + buttons) the contacts glide to the new scale in about a quarter of a second instead of jumping. Off: they jump.");
        VisualLifetime = file.Bind("3. Views", "Visual contact lifetime (s)", 30f,
            new ConfigDescription("A contact held only by eyes or infrared counts as lost this many seconds after its last sighting (it then fades " +
                "for the 'lost contact memory' time). 0 = as long as the game keeps the track (its own maximum age, which can be long).",
                new AcceptableValueRange<float>(0f, 600f)));
        RetroLook = file.Bind("3. Views", "Radar and sonar: period look", true,
            "On: the radar scope and the sonar display look like the green phosphor tubes of the period - dark ground, dim rings, bright traces, a sweep with a glowing trail. Off: the game's own colours (as in 0.3.0). Applies to windows opened afterwards.");
        RealisticSweep = file.Bind("3. Views", "Radar scope: blips appear when the sweep passes", true,
            "On: a radar contact is painted on the scope only when the sweep passes over it and then fades until the next pass, like a real " +
            "radar. Off: contacts are always shown at their latest position, as in version 0.3.0.");

        CoverageVisible = file.Bind("5. Map: coverage", "Show coverage", false,
            "The 'Sensor coverage' entry of the map's Layers menu (kept in step with it). For the unit selected in the game: where its sensors " +
            "would hold an imagined target - radar against the target altitude below with terrain shadows, ESM, visual, passive sonar.");
        CoverageKey = file.Bind("5. Map: coverage", "Toggle key", "Ctrl+Shift+PageDown", "Hot key for the coverage layer. Empty = none.");
        CoverageAltitude = file.Bind("5. Map: coverage", "Target altitude (ft)", 100f,
            new ConfigDescription("Altitude of the imagined target for radar coverage; the radar scope's slider changes it too.", new AcceptableValueRange<float>(0f, 80000f)));
        TargetRcs = file.Bind("5. Map: coverage", "Target radar cross section (m2)", 10f,
            new ConfigDescription("Imagined target for the radar equation; the coverage never exceeds the radar's own maximum range or the horizon.", new AcceptableValueRange<float>(0.01f, 10000f)));
        CoverRadar = file.Bind("5. Map: coverage", "Radar", true, "Search radar coverage (terrain-shadowed).");
        CoverEsm = file.Bind("5. Map: coverage", "ESM", true, "ESM listening range against an emitter like the unit's own search radar.");
        CoverVisual = file.Bind("5. Map: coverage", "Visual", true, "Visual range against a target of the size class below, limited by the horizon.");
        CoverSonar = file.Bind("5. Map: coverage", "Passive sonar", true, "Passive sonar range with the dent of the baffle sector.");
        TerrainShadows = file.Bind("5. Map: coverage", "Terrain shadows", true, "Cut the radar coverage where terrain blocks the line of sight (checked a few rays per frame).");
        VisualSize = file.Bind("5. Map: coverage", "Visual target size", "Medium",
            new ConfigDescription("Size class of the imagined target for the visual ring.", new AcceptableValueList<string>("VerySmall", "Small", "SemiSmall", "Meager", "Medium", "SemiLarge", "Large", "VeryLarge")));
        Rays = file.Bind("5. Map: coverage", "Rays", 72,
            new ConfigDescription("Bearings the coverage polygons are sampled at.", new AcceptableValueRange<int>(24, 180)));

        // One-time change for configs from before 0.3.0: they hold the old default "All", which made the map very busy (and slow)
        // with many units. A player who wants all lines sets it back once.
        layoutVersion = file.Bind("9. Internal", "Settings layout", existed ? 1 : Layout, "Used by the mod to update old settings. Do not change.");
        if (layoutVersion.Value < 2 && LinesWho.Value == AllUnits)
        {
            LinesWho.Value = SelectedOnly;
            ModLog.Info("Settings: 'Units shown' is now 'Selected only' (the new default); set it back to 'All, selected bright' to see every unit's lines.");
        }
        if (layoutVersion.Value < Layout) layoutVersion.Value = Layout;

        file.SettingChanged += (s, e) => Version++;
    }

    /// <summary>Parses "Ctrl+Shift+F6"; false when empty or unreadable.</summary>
    public static bool TryParseKey(string text, out KeyCode key, out bool ctrl, out bool shift, out bool alt)
    {
        key = KeyCode.None; ctrl = shift = alt = false;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (string raw in text.Split('+'))
        {
            string part = raw.Trim();
            if (part.Equals("Ctrl", System.StringComparison.OrdinalIgnoreCase) || part.Equals("Control", System.StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else if (part.Equals("Shift", System.StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (part.Equals("Alt", System.StringComparison.OrdinalIgnoreCase)) alt = true;
            else if (!System.Enum.TryParse(part, true, out key)) return false;
        }
        return key != KeyCode.None;
    }
}
