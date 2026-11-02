using System;
using System.Collections.Generic;
using System.ComponentModel;
using SeaPower;
using SeaPowerSensorViews.Data;
using SeapowerUI;

namespace SeaPowerSensorViews.Ui;

/// <summary>
/// The content object of a window: the game's WindowHost shows it through the DataTemplate registered for its type
/// (see <see cref="Kit"/>), and it stays the same object when Pop-Out Windows moves the window to another monitor, so
/// the view state lives here, not in the view.
/// </summary>
public abstract class SvWindow : INotifyPropertyChanged, IDisposeOnClose
{
    public event PropertyChangedEventHandler PropertyChanged;
    private string title;

    public string Title
    {
        get => title;
        set { title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title))); }
    }

    // state shared by the views
    internal UnitRow Pinned;               // unit clicked on the board (used when "follow selection" is off)

    internal ObjectBase PinnedSel;         // the game's selection when the unit was pinned

    private static ObjectBase GameSelection()
    {
        try { return Singleton<RenderPosition>.InstanceExists() ? Singleton<RenderPosition>.Instance.getSelectedObject() : null; }
        catch (Exception) { return null; }   // no selection API in this state
    }

    internal void Pin(UnitRow row)
    {
        Pinned = row;
        PinnedSel = GameSelection();
    }

    /// <summary>The unit a view shows: a unit clicked on the board (until the game's selection changes), else the selected one.</summary>
    internal UnitRow Subject()
    {
        SensorPicture pic = SensorPicture.Current;
        ObjectBase sel = GameSelection();
        if (Pinned != null && pic.Units.Contains(Pinned) && (!Settings.FollowSelection.Value || sel == PinnedSel))
            return Pinned;
        if (Settings.FollowSelection.Value)
        {
            UnitRow row = pic.RowFor(sel);
            if (row != null) return row;
        }
        return pic.Units.Count > 0 ? pic.Units[0] : null;
    }

    public void Dispose() { }
}

public sealed class BoardWindow : SvWindow
{
    public BoardWindow() { Title = "R.A.T.S. Sensor Board"; }
    internal ContactRow SelContact;
    internal UnitRow SelUnit;
}

public sealed class ScopeWindow : SvWindow
{
    public ScopeWindow() { Title = "R.A.T.S. Radar Scope"; }
    internal static readonly float[] Ranges = { 5, 10, 20, 40, 80, 160, 320 };
    internal int RangeIndex = 3;
    /// <summary>Starts at the config value the map coverage layer uses and is written back to it about once a second (not on every slider step: each write saves the config file).</summary>
    internal float TargetAltitudeFt = Settings.CoverageAltitude.Value;

    internal void PushAltitude()
    {
        if (Math.Abs(Settings.CoverageAltitude.Value - TargetAltitudeFt) > 0.5f)
            Settings.CoverageAltitude.Value = TargetAltitudeFt;
    }
    internal bool HeadingUp;
    internal bool ShowHorizon = true;

    internal void StepRange(int d) => RangeIndex = Math.Max(0, Math.Min(Ranges.Length - 1, RangeIndex + d));
}

public sealed class SonarWindow : SvWindow
{
    public SonarWindow() { Title = "R.A.T.S. Sonar Display"; }
    internal const int Rows = 150;           // seconds of history
    internal const int Bins = 180;           // 2 degree bins across the relative bearing -180..180
    internal int SonarIndex;
    internal readonly Dictionary<string, Waterfall> Falls = new Dictionary<string, Waterfall>();
    internal SonarState State = new SonarState();
}

/// <summary>Bearing over time: one row per snapshot, sparse (only bins with a detection).</summary>
internal sealed class Waterfall
{
    public struct Dot { public short Bin; public byte Width; public byte Level; public bool Active; }

    public readonly List<Dot>[] Rows = new List<Dot>[SonarWindow.Rows];
    public readonly float[] Noise = new float[SonarWindow.Rows];
    public readonly float[] Heading = new float[SonarWindow.Rows];
    public int Head = -1;            // index of the newest row
    public int Count;
    public int LastVersion = -1;

    public Waterfall()
    {
        for (int i = 0; i < Rows.Length; i++) Rows[i] = new List<Dot>(8);
    }

    public List<Dot> Push(float noise, float heading)
    {
        Head = (Head + 1) % Rows.Length;
        Rows[Head].Clear();
        Noise[Head] = noise;
        Heading[Head] = heading;
        if (Count < Rows.Length) Count++;
        return Rows[Head];
    }

    /// <summary>Index of the row <paramref name="age"/> snapshots old (0 = newest).</summary>
    public int Back(int age) => (Head - age + Rows.Length * 2) % Rows.Length;
}

/// <summary>What the sonar display shows besides the waterfall, sampled on the main thread once a second.</summary>
internal sealed class SonarState
{
    public bool Valid;
    public string UnitName, SonarName, Mode;
    public bool Towed;
    public float BaffleFrom = float.NaN, BaffleTo = float.NaN;
    public float OwnNoise = float.NaN;
    public float SpeedKts;
    public float DepthFt = float.NaN;          // own depth (positive down)
    public float LayerFt = float.NaN;          // thermal layer depth (positive down)
    public float ArrayFt = float.NaN;          // sonar element depth
    public float SeabedFt = float.NaN;
    public float Heading;
}
