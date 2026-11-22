using System.Runtime.CompilerServices;
using AnchorChain;

namespace SeaPowerSensorViews;

/// <summary>
/// Entry point, run by R.A.T.S. Keel (it calls TriggerEntryPoint of an [ACPlugin] class). The start itself is R.A.T.S. Rigging's:
/// the plugin names it as a dependency, so Keel starts it first and keeps this mod out when it is missing or too old.
/// </summary>
[ACPlugin(Guid, Name, Version)]
[ACDependency("dev.sewerlabs.seapowerrigging", "1.0.0", null)]
public class Plugin : IAnchorChainMod
{
    public const string Guid = "dev.sewerlabs.seapowersensorviews";
    public const string Name = "R.A.T.S. Sensor Views";
    public const string Version = "0.6.1";

    public void TriggerEntryPoint() => Start();

    // Apart from the entry point, so this class has no Rigging type in it and loads on its own.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start() => Rig.Start<Host>(Settings.Bind, Hooks.Declare);
}
