using System.Collections.Generic;
using SeaPower;

namespace SeaPowerSensorViews.Data;

/// <summary>How a sensor holds a contact. The letter is what the board shows.</summary>
internal enum Kind { Radar, FireControl, Esm, Passive, Active, Visual, Mad }

/// <summary>The side a contact is on, as the game's plotting table has it.</summary>
internal enum Relation { Unknown, Friendly, Neutral, Hostile }

internal static class KindInfo
{
    /// <summary>The kinds in the order the board writes their letters into a cell.</summary>
    public static readonly Kind[] BoardOrder = { Kind.Radar, Kind.FireControl, Kind.Esm, Kind.Passive, Kind.Active, Kind.Visual, Kind.Mad };

    public static char Letter(Kind k) => k switch
    {
        Kind.Radar => 'R', Kind.FireControl => 'F', Kind.Esm => 'E', Kind.Passive => 'S', Kind.Active => 'A', Kind.Visual => 'V', _ => 'M',
    };

    public static string Name(Kind k) => k switch
    {
        Kind.Radar => "Search radar",
        Kind.FireControl => "Fire control radar",
        Kind.Esm => "ESM (bearing only)",
        Kind.Passive => "Passive sonar",
        Kind.Active => "Active sonar",
        Kind.Visual => "Visual / infrared",
        _ => "MAD",
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

    public static Relation Of(RelationsState state) => state switch
    {
        RelationsState.Friendly => Relation.Friendly,
        RelationsState.Neutral => Relation.Neutral,
        RelationsState.Hostile => Relation.Hostile,
        _ => Relation.Unknown,
    };
}

/// <summary>One sensor of one own unit holding one contact: the newest track it has (or had).</summary>
internal sealed class Hit
{
    public UnitRow Unit;
    public int ContactId;
    public Kind Kind;
    public string SensorName;
    public Unity.Collections.FixedString64Bytes SensorNameRaw;
    public double FixTime;          // mission session time of the track's epoch
    public float Bearing;           // degrees true, from the unit
    public float RangeM = float.NaN;
    public float RangeErrorM;       // one standard deviation of the range estimate (0 when the track has none)
    public float AltitudeUnits = float.NaN;
    public float BearingError;
    public float AltitudeError;
    public bool BearingOnly;
    public float Signal = float.NaN;  // signal value the sensor measured (sonar: signal excess dB)
    public bool Live;               // the track is still valid; false = remembered
    public int Stamp;
    public float MaxAge;
    public readonly List<KeyValuePair<float, float>> History = new List<KeyValuePair<float, float>>();   // (fix time, bearing) of each new fix, last 10 min

    /// <summary>The sensor measured a range (radar, active sonar, visual, MAD); a range of a metre or less is no measurement.</summary>
    public bool Ranged => !BearingOnly && RangeM > 1f;
}

internal sealed class SensorRow
{
    public SensorSystem System;
    public string Name;
    public string Letter;           // R F E S A V M J(ECM)
    public string Detail;           // range / frequency band / mode, for the sensor panel
    public bool SearchRadar;        // a radar that is not a fire-control radar (letter R)
    public bool On;
    public bool Inoperable;
    public string Arc;              // "all round" or "-60..+60"
    public float ArcMin = -181f, ArcMax = 181f;
    public bool Active;             // radar / active sonar emitting
}

internal sealed class Strobe
{
    /// <summary>Seconds a radar warning or jam strobe stays after the game last reported it; the scope fades it over this time.</summary>
    public const float Lifetime = 8f;

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
    public int StrobeVersion;       // bumped when a strobe comes or goes
    public int Stamp;
}

internal sealed class ContactRow
{
    public int Id;
    public string Label;            // "T123" plus the class when the game shows it
    public string LabelClass;
    public int Listed;              // stamp of the refresh that put it into Contacts
    public Relation Relation;
    public GeoPosition Plot;        // where the game's plotting table puts the contact (the map marker; its estimate, not the real object)
    public bool HasPlot;
    public bool LiveRanged;         // a live track from a ranging sensor (radar, active sonar, visual, MAD) holds it
    public readonly List<Hit> Hits = new List<Hit>();
    public int Stamp;

    /// <summary>Plotted, and a live ranged track backs that position.</summary>
    public bool HasRangedFix => HasPlot && LiveRanged;
}
