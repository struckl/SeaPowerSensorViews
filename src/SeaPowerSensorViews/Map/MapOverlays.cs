using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Map;

/// <summary>
/// Place for the map overlays of a later round (sensor lines from each own unit to the contacts it holds, bearing wedges,
/// sensor coverage). They read the same <see cref="SensorPicture"/> as the windows: Units, Contacts and every Hit (unit, kind,
/// bearing, range, age). The map is the part that differs between the flat 0.8.7 and the globe build, so code here is the one
/// that may need the two-build scheme (docs/both-versions.md in the workspace); the windows do not. Not built yet.
/// </summary>
internal interface IMapOverlay
{
    /// <summary>A new snapshot is ready (about once a second while a view or overlay is on).</summary>
    void OnSnapshot(SensorPicture picture);
}
