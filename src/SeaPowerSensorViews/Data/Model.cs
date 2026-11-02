using System.Collections.Generic;
using SeaPower;

namespace SeaPowerSensorViews.Data;

/// <summary>How a sensor holds a contact. The letter is what the board shows.</summary>
internal enum Kind { Radar, FireControl, Esm, Passive, Active, Visual, Mad }

internal static class KindInfo
{
    public static readonly char[] Letters = { 'R', 'F', 'E', 'S', 'A', 'V', 'M' };
    public static readonly string[] Names =
    {
        "Search radar", "Fire control radar", "ESM (bearing only)", "Passive sonar", "Active sonar", "Visual / infrared", "MAD",
    };

    public static bool TryMap(SensorSystem.SensorType type, out Kind kind)
    {
        switch (type)
        {
            case SensorSystem.SensorType.SearchRadar: kind = Kind.Radar; return true;
            case SensorSystem.SensorType.FCR: kind = Kind.FireControl; return true;
            case SensorSystem.SensorType.ESM: kind = Kind.Esm; return true;
            case SensorSystem.SensorType.PassiveSonar:
            case SensorSystem.SensorType.ActiveIntercept: kind = Kind.Passive; return true;
            case SensorSystem.SensorType.ActiveSonar: kind = Kind.Active; return true;
            case SensorSystem.SensorType.Visual: kind = Kind.Visual; return true;
            case SensorSystem.SensorType.MAD: kind = Kind.Mad; return true;
            default: kind = Kind.Radar; return false;
        }
    }
}

/// <summary>One sensor of one own unit holding one contact: the newest track it has (or had).</summary>
internal sealed class Hit
{
    public UnitRow Unit;
    public int ContactId;
    public Kind Kind;
    public string SensorName;
    public double FixTime;          // mission session time of the track's epoch
    public float Bearing;           // degrees true, from the unit
    public float RangeM = float.NaN;
    public float AltitudeUnits = float.NaN;
    public float BearingError;
    public bool BearingOnly;
    public float Signal = float.NaN;  // signal value the sensor measured (sonar: signal excess dB)
    public bool Live;               // the track is still valid; false = remembered
    public int Stamp;
    public float MaxAge;
}

internal sealed class SensorRow
{
    public string Name;
    public string Letter;           // R F E S A V M J(ECM)
    public string Detail;           // range / frequency band / mode, for the sensor panel
    public bool On;
    public bool Inoperable;
    public string State;
    public string Arc;              // "all round" or "-60..+60"
    public float ArcMin = -181f, ArcMax = 181f;
    public bool Active;             // radar / active sonar emitting
}

internal sealed class Strobe
{
    public float Bearing;
    public double Time;
    public bool Jam;
}

internal sealed class UnitRow
{
    public ObjectBase Unit;
    public Unity.Entities.Entity Entity;
    public int Uid;
    public string Name;
    public string Kind;             // ship / sub / air ...
    public bool Emcon;
    public float Heading;
    public float SpeedKts;
    public float DepthFt;           // positive below the surface, NaN above
    public float AltitudeFt = float.NaN;
    public float SensorHeightUnits;
    public readonly List<SensorRow> Sensors = new List<SensorRow>();
    public readonly List<Strobe> Strobes = new List<Strobe>();
    public int Stamp;
}

internal sealed class ContactRow
{
    public int Id;
    public string Label;            // "T123" plus the class when the game shows it
    public int Relation;            // 0 unknown, 1 friendly, 2 neutral, 3 hostile
    public readonly List<Hit> Hits = new List<Hit>();
    public int Stamp;
}
