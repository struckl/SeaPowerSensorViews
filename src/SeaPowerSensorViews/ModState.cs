using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SeaPowerSensorViews;

/// <summary>
/// Anchor Chain loads every mod folder it finds, ticked in the game's Mod Manager or not. The Mod Manager keeps its
/// choice in <c>usersettings.ini</c> (<c>ModNDirectory=&lt;folder&gt;,&lt;True|False&gt;,&lt;workshop id&gt;</c>), so the
/// mod reads its own entry there and stays out when it is unticked.
/// </summary>
internal static class ModState
{
    /// <summary>False only when the Mod Manager lists this mod's folder as unticked; anything unclear counts as on.</summary>
    public static bool TickedInModManager()
    {
        try
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "");
            string file = Path.Combine(Application.persistentDataPath, "usersettings.ini");
            if (folder.Length == 0 || !File.Exists(file))
                return true;
            foreach (string line in File.ReadAllLines(file))
            {
                int eq = line.IndexOf('=');
                if (eq < 0 || !line.StartsWith("Mod", StringComparison.Ordinal) ||
                    !line.Substring(0, eq).EndsWith("Directory", StringComparison.Ordinal))
                    continue;
                string[] parts = line.Substring(eq + 1).Split(',');
                if (parts.Length >= 2 && parts[0].Trim().Equals(folder, StringComparison.OrdinalIgnoreCase))
                    return !parts[1].Trim().Equals("False", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            ModLog.Debug($"Mod Manager state unreadable: {ex.Message}");
        }
        return true;
    }
}
