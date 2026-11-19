namespace SeaPowerSensorViews.Data;

/// <summary>
/// Unit conversions. A game (Unity) unit is 67.2 m; the game's own nautical mile is 27.5603 units, a little more than
/// 1852 m / 67.2, and the mod uses the game's number wherever it turns units into nautical miles and back.
/// </summary>
internal static class Conv
{
    public const float MetersPerUnit = 67.2f;
    public const float UnitsPerMeter = 1f / MetersPerUnit;
    public const float FeetPerMeter = 3.28084f;
    public const float FeetPerUnit = MetersPerUnit * FeetPerMeter;
    public const float UnitsPerNm = 27.5603f;
    public const float NmPerUnit = 1f / UnitsPerNm;
    public const float MetersPerNm = 1852f;
    public const float KmPerNm = 1.852f;
    public const float KmPerMeter = 0.001f;
    /// <summary>The game's sonar sums take ranges in yards with this round figure.</summary>
    public const float YardsPerNm = 2025f;

    public static float Nm(float meters) => meters / MetersPerNm;

    /// <summary>Game units to km, for the game's OffsetByDistanceOnHeadingUnity (which takes km).</summary>
    public static float KmOfUnits(float units) => units * NmPerUnit * KmPerNm;
}
