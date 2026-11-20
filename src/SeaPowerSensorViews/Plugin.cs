using System.Runtime.CompilerServices;
using AnchorChain;

namespace SeaPowerSensorViews;

/// <summary>
/// Entry point, started by a mod loader (Keel or Anchor Chain) through TriggerEntryPoint.
/// Sea Power exists in two versions with different map code (0.8.7's flat map and the 3D-globe build); the windows are the
/// same on both but the map layers are not (layer list, projection), and a mod compiled against one fails on the other. So the
/// package carries two builds of the same sources: SeaPowerSensorViews.dll for the globe build and SeaPowerSensorViewsFlat.dll
/// (FLATMAP defined) for 0.8.7. Each looks at the game it runs on and stays off when it is not the one it was made for
/// (ModEntry, in the shared skeleton).
/// </summary>
[ACPlugin(PluginGuid, Name, Version)]
public class Plugin : IAnchorChainMod
{
    public const string Guid = "dev.sewerlabs.seapowersensorviews";
    public const string Name = "R.A.T.S. Sensor Views";
    public const string Version = "0.5.0";

#if FLATMAP
    // Both loaders skip a plugin whose GUID they have already seen, so the two builds register under different ones.
    public const string PluginGuid = Guid + ".flatmap";
#else
    public const string PluginGuid = Guid;
#endif

    public void TriggerEntryPoint() => ModEntry.Trigger(Start);

    // Apart from the check in Trigger, so nothing in here is compiled on a game it isn't for.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start() => ModEntry.Start<Host>(Settings.Bind, Hooks.Declare);
}
