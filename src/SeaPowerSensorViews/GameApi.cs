#if !FLATMAP
using System;
using System.Reflection;
using SeaPower.LiveMap;
using SeapowerUI;

namespace SeaPowerSensorViews;

/// <summary>
/// The few game members the mod needs that the game keeps internal, looked up once by reflection. Each lookup that
/// fails is logged once and its feature falls back (see the callers).
/// </summary>
internal static class GameApi
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Type LayerEntry = typeof(GizmoLayerManager).GetNestedType("Entry", BindingFlags.NonPublic);

    private static readonly ConstructorInfo LayerCtor = LayerEntry == null ? null :
        typeof(MapLayer).GetConstructor(Any, null, new[] { typeof(MapDisplayState), LayerEntry.MakeByRefType(), typeof(bool) }, null);

    /// <summary>A new entry for a map's Layers menu; null if the game's layer type has changed.</summary>
    public static MapLayer NewLayer(MapDisplayState owner, int number, string name, bool visible)
    {
        if (LayerCtor == null)
            return null;
        object entry = Activator.CreateInstance(LayerEntry, number, name, true);
        return (MapLayer)LayerCtor.Invoke(new[] { owner, entry, (object)visible });
    }
}
#endif
