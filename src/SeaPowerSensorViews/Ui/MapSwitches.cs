using System;
using SeaPowerSensorViews.Map;

namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: a leftover DLL of an older release must not share these names

internal enum SwitchGroup { Layers, LinesFor, LinesOf, Coverage }

/// <summary>
/// One map switch as the bottom bar's flyout and the sensor board's map bar show it: both are built from <see cref="All"/>. The
/// Options page shows the same settings as rows of the config entries, with its own longer texts.
/// </summary>
internal sealed class MapSwitch
{
    public SwitchGroup Group;
    public string Label;                // the flyout's chip
    public string BoardLabel;           // the board's check box or radio button; null = not on the board
    public string Tip;
    public string BoardTip;             // when the board's tip differs from the flyout's; "" = none on the board
    public bool BoardOwnRow;            // on the board in a row of its own under its group
    public Func<bool> Read;
    public Action<bool> Write;

    /// <summary>One of a choice (radio): only switching it on does something.</summary>
    public bool Choice;

    public string TipOnBoard => BoardTip == null ? Tip : BoardTip.Length > 0 ? BoardTip : null;

    private static bool LinesForAll() => Settings.LinesWho.Value == Settings.AllUnits;

    private static void SetLinesForAll(bool all) => Settings.LinesWho.Value = all ? Settings.AllUnits : Settings.SelectedOnly;

    private static MapSwitch Entry(SwitchGroup group, string label, string boardLabel, string tip, BepInEx.Configuration.ConfigEntry<bool> entry) =>
        new MapSwitch { Group = group, Label = label, BoardLabel = boardLabel, Tip = tip, Read = () => entry.Value, Write = v => entry.Value = v };

    public static readonly MapSwitch[] All =
    {
        new MapSwitch
        {
            Group = SwitchGroup.Layers, Label = "Sensor lines", BoardLabel = "Sensor lines", Tip = "Lines from each own unit to the contacts it holds",
            Read = () => MapSet.LinesOn, Write = v => { if (v != MapSet.LinesOn) MapSet.ToggleLines(); },
        },
        new MapSwitch
        {
            Group = SwitchGroup.Layers, Label = "Coverage", BoardLabel = "Coverage", Tip = "Where the selected unit's sensors would hold an imagined target",
            Read = () => MapSet.CoverageOn, Write = v => { if (v != MapSet.CoverageOn) MapSet.ToggleCoverage(); },
        },
        new MapSwitch
        {
            Group = SwitchGroup.LinesFor, Label = "Selected unit", BoardLabel = "selected unit", Tip = "Only the selected unit's lines", BoardTip = "", Choice = true,
            Read = () => !LinesForAll(), Write = v => { if (v) SetLinesForAll(false); },
        },
        new MapSwitch
        {
            Group = SwitchGroup.LinesFor, Label = "All units", BoardLabel = "all units", Choice = true,
            Tip = "Every own unit's lines, the selected one bright (coverage stays selected-unit only)",
            BoardTip = "Lines of every own unit, the selected one bright (coverage stays selected-unit only)",
            Read = LinesForAll, Write = v => { if (v) SetLinesForAll(true); },
        },
        Entry(SwitchGroup.LinesOf, "Radar", "radar", "Search and fire-control radar", Settings.LinesRadar),
        Entry(SwitchGroup.LinesOf, "ESM", "ESM", "ESM bearing lines and cones", Settings.LinesEsm),
        Entry(SwitchGroup.LinesOf, "Visual", "visual", "Visual and infrared", Settings.LinesVisual),
        Entry(SwitchGroup.LinesOf, "Sonar", "sonar", "Passive and active sonar", Settings.LinesSonar),
        new MapSwitch
        {
            Group = SwitchGroup.LinesOf, Label = "Hide tracked ESM", BoardLabel = "hide ESM of tracked", BoardOwnRow = true,
            Tip = "Leave out an ESM line for a contact a radar holds as a live track",
            Read = () => Settings.HideTrackedEsm.Value, Write = v => Settings.HideTrackedEsm.Value = v,
        },
        Entry(SwitchGroup.Coverage, "Radar", null, "Search radar coverage, shadowed by terrain", Settings.CoverRadar),
        Entry(SwitchGroup.Coverage, "ESM", null, "ESM listening range", Settings.CoverEsm),
        Entry(SwitchGroup.Coverage, "Visual", null, "Visual range, limited by the horizon", Settings.CoverVisual),
        Entry(SwitchGroup.Coverage, "Sonar", null, "Passive sonar range", Settings.CoverSonar),
        Entry(SwitchGroup.Coverage, "Terrain shadows", null, "Cut the radar coverage where terrain blocks the line of sight", Settings.TerrainShadows),
    };

    /// <summary>The flyout's heading of a group.</summary>
    public static string Heading(SwitchGroup g) => g switch
    {
        SwitchGroup.Layers => "Map layers",
        SwitchGroup.LinesFor => "Lines for",
        SwitchGroup.LinesOf => "Lines of",
        _ => "Coverage of (selected unit)",
    };
}
