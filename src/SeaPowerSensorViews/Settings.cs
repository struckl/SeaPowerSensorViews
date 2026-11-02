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

    public static void Init()
    {
        var file = new ConfigFile(Path.Combine(Paths.ConfigPath, Plugin.Guid + ".cfg"), saveOnInit: true);
        Enabled = file.Bind("1. General", "Enable mod", true, "Master switch. Needs a restart.");
        VerboseLog = file.Bind("1. General", "Verbose log", false, "Write debug detail to BepInEx/LogOutput.log.");
        BarButtons = file.Bind("2. Opening the windows", "Bottom bar buttons", true,
            "Three small toggle buttons (Board, Scope, Sonar) at the right end of the bottom bar, next to the game's own window toggles.");
        BoardKey = file.Bind("2. Opening the windows", "Sensor board key", "Ctrl+F4",
            "Hot key that toggles the sensor board. Modifiers Ctrl, Shift, Alt joined with +, then a Unity KeyCode name. Empty = none. " +
            "The defaults are keys the game does not bind by default.");
        ScopeKey = file.Bind("2. Opening the windows", "Radar scope key", "Ctrl+F6", "Hot key that toggles the radar scope.");
        SonarKey = file.Bind("2. Opening the windows", "Sonar display key", "Ctrl+F8", "Hot key that toggles the sonar display.");
        MemorySeconds = file.Bind("3. Views", "Lost contact memory (s)", 60f,
            new ConfigDescription("How long a contact a sensor lost stays on the board and scope, dimmed, with the age of its last fix.",
                new AcceptableValueRange<float>(0f, 600f)));
        FollowSelection = file.Bind("3. Views", "Follow the selected unit", true,
            "The scope, the sonar display and the board's sensor panel show the unit selected in the game. Off = the unit clicked on the board.");
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
