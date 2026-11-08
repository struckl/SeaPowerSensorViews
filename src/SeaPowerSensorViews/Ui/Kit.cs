using System;
using System.Collections.Generic;
using Noesis;
using SeaPower;
using SeapowerUI;
using SeaPowerSensorViews.Data;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>
/// Puts the three windows into the game's own window system.
/// <para>A game window is a content object (a view model) in the main view's <c>WindowHost.Windows</c>; the host shows
/// it through the <c>DataTemplate</c> registered for the object's type, and that template is a <c>SeapowerUI.Window</c>
/// with the game's chrome (header, close button, resize thumbs, <c>WindowLayoutNoScroll</c>). The mod parses one
/// resource dictionary with such a template per window type and merges it into the main view's resources, then opens a
/// window with the game's <c>WindowHostCommands.OpenWindowCommand</c>, exactly as the game opens its own. Pop-Out Windows
/// keys on the content's type name, so it gives the new windows its pop-out button without knowing about this mod.</para>
/// <para>Opened from three toggle buttons added to the bottom bar (styled like the game's own, lit while the window is open)
/// or from hot keys.</para>
/// </summary>
internal static class Kit
{
    private sealed class Entry
    {
        public SvWindow Window;
        public ToggleButton Button;
        public TextBlock Label;
        public Border Face;
        public string Text;
        public bool Open;           // as of the last Sync / Open / Close; asking the game's command costs a tree walk
        public bool Lit;
    }

    private static readonly List<Entry> Entries = new List<Entry>();
    private static MainGameView view;
    private static bool syncing;

    public static bool Active => view != null;

#if FLATMAP
    private const string XamlNamespace = "SeaPowerSensorViews.Ui";
#else
    private const string XamlNamespace = "SeaPowerSensorViews.Globe.Ui";
#endif

    private const string Header =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
        "xmlns:local='clr-namespace:SeapowerUI' xmlns:sv='clr-namespace:" + XamlNamespace + "'";

    // The game's resize command clamps a dragged edge to "size minus MinWidth/MinHeight": with no minimum (0) the window could
    // grow but never shrink. Found with the size probe below.
    private static string Template(string type, string content, int width, int height, int x, int y, bool dynamicTemplate, int minWidth, int minHeight) =>
        "<DataTemplate DataType='{x:Type sv:" + type + "}'>" +
        "<local:Window " + (dynamicTemplate ? "Template='{DynamicResource WindowLayoutNoScroll}' " : "") +
        "Width='" + width + "' Height='" + height + "' MinWidth='" + minWidth + "' MinHeight='" + minHeight + "' X='" + x + "' Y='" + y + "'>" +
        "<local:Window.Header><TextBlock Text='{Binding Title}'/></local:Window.Header>" +
        "<sv:" + content + "/></local:Window></DataTemplate>";

    private static ResourceDictionary Dictionary(bool dynamicTemplate) =>
        (ResourceDictionary)GUI.ParseXaml(
            "<ResourceDictionary " + Header + ">" +
            Template("BoardWindow", "BoardView", 900, 640, 150, 80, dynamicTemplate, 420, 240) +
            Template("ScopeWindow", "ScopeView", 660, 700, 260, 100, dynamicTemplate, 340, 380) +
            Template("SonarWindow", "SonarView", 860, 560, 200, 130, dynamicTemplate, 420, 240) +
            "</ResourceDictionary>");

    /// <summary>Called when the mission UI exists; true when everything is in place.</summary>
    public static bool Setup()
    {
        MainGameView main = MainGameView.Instance;
        if (main == null) return false;
        if (ReferenceEquals(main, view)) return true;
        Teardown();

        ResourceDictionary dict;
        try { dict = Dictionary(dynamicTemplate: true); }
        catch (Exception ex)
        {
            ModLog.Warn("Window template with the game's no-scroll layout failed (" + ex.Message + "); using the default window layout.");
            dict = Dictionary(dynamicTemplate: false);
        }
        main.Resources.MergedDictionaries.Add(dict);

        Entries.Clear();
        MapSwitches.Clear();
        Entries.Add(new Entry { Window = new BoardWindow(), Text = "Board" });
        Entries.Add(new Entry { Window = new ScopeWindow(), Text = "Radar scope" });
        Entries.Add(new Entry { Window = new SonarWindow(), Text = "Sonar" });
        view = main;
        if (Settings.BarButtons.Value)
        {
            try { AddBarButtons(main); Sync(); }
            catch (Exception ex) { ModLog.Warn("Bottom bar buttons not added (" + ex.Message + ") - use the hot keys."); }
        }
        // No CanExecuteChanged subscription: the game's commands re-query on every mouse move and key press, and a Sync each
        // time cost three tree walks per input event. The host syncs twice a second, and Open/Close update at once.
        ModLog.Info("Windows registered: sensor board, radar scope, sonar display" + (Settings.BarButtons.Value ? " (bottom bar buttons added)." : "."));
        return true;
    }

    public static void Teardown()
    {
        foreach (Entry e in Entries) e.Window.Pinned = null;
        Entries.Clear();
        MapSwitches.Clear();
        flyout = null;
        iconFace = null; barButton = null;
        view = null;
    }

    private static void AddBarButtons(MainGameView main)
    {
        var grid = main.FindName("DisplayAreaGrid") as Grid;
        if (grid == null) throw new InvalidOperationException("DisplayAreaGrid not found");
        int newColumn = grid.ColumnDefinitions.Count;
        foreach (UIElement child in grid.Children)
            if (Grid.GetColumnSpan(child) == newColumn) Grid.SetColumnSpan(child, newColumn + 1);   // keep the full-width layers full width
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        // One icon button; a click opens a flyout above it with the three window toggles and the map switches.
        const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
        const string Ink = "#FF2A2627";
        var icon = (Border)GUI.ParseXaml(
            "<Border " + Ns + " Width='30' Height='28' Background='" + IconIdle + "'>" +
            "<Viewbox Margin='3'><Canvas Width='24' Height='24'>" +
            "<Ellipse Canvas.Left='2' Canvas.Top='2' Width='20' Height='20' Stroke='" + Ink + "' StrokeThickness='2'/>" +
            "<Path Data='M12,2 L12,22 M2,12 L22,12' Stroke='" + Ink + "' StrokeThickness='1'/>" +
            "<Path Data='M12,12 L12,2 A10,10 0 0 1 20.2,6.3 Z' Fill='" + Ink + "'/>" +
            "<Ellipse Canvas.Left='14.5' Canvas.Top='14.5' Width='3.5' Height='3.5' Fill='" + Ink + "'/>" +
            "</Canvas></Viewbox></Border>");
        iconFace = icon;
        var barBtn = (ToggleButton)GUI.ParseXaml(
            "<ToggleButton " + Ns + " Focusable='False' Height='32' Margin='4,0,0,0' Style='{DynamicResource SimpleToggleButton}'/>");
        barBtn.Content = icon;
        barBtn.ToolTip = "R.A.T.S. sensor views: windows and map layers";
        barButton = barBtn;

        // the flyout
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        FlyoutHeader(panel, "Windows");
        var windowsRow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Entry entry in Entries)
        {
            ToggleButton button = Chip(entry.Text, out TextBlock label, out Border face);
            entry.Button = button;
            entry.Label = label;
            entry.Face = face;
            entry.Lit = false;
            button.ToolTip = entry.Window.Title + " (" + HotKeyText(entry.Window) + ")";
            Entry captured = entry;
            // The click decides, from the window's real state; the check mark is only ever set by Sync (events from it are ignored).
            button.Click += (s, e) => { ModLog.Debug("Chip clicked: " + captured.Text); Toggle(captured.Window); };
            windowsRow.Children.Add(button);
        }
        panel.Children.Add(windowsRow);

        FlyoutHeader(panel, "Map layers");
        var layers = new WrapPanel { Orientation = Orientation.Horizontal };
        MapChip(layers, "Sensor lines", () => SeaPowerSensorViews.Map.MapSet.LinesOn,
            v => { if (v != SeaPowerSensorViews.Map.MapSet.LinesOn) SeaPowerSensorViews.Map.MapSet.ToggleLines(); }, "Lines from each own unit to the contacts it holds");
        MapChip(layers, "Coverage", () => SeaPowerSensorViews.Map.MapSet.CoverageOn,
            v => { if (v != SeaPowerSensorViews.Map.MapSet.CoverageOn) SeaPowerSensorViews.Map.MapSet.ToggleCoverage(); }, "Where the selected unit's sensors would hold an imagined target");
        panel.Children.Add(layers);

        FlyoutHeader(panel, "Lines for");
        var who = new WrapPanel { Orientation = Orientation.Horizontal };
        MapChip(who, "Selected unit", () => !LinesForAll(), v => { if (v) SetLinesForAll(false); }, "Only the selected unit's lines");
        MapChip(who, "All units", LinesForAll, v => { if (v) SetLinesForAll(true); }, "Every own unit's lines, the selected one bright (coverage stays selected-unit only)");
        panel.Children.Add(who);

        FlyoutHeader(panel, "Lines of");
        var kinds = new WrapPanel { Orientation = Orientation.Horizontal };
        MapChip(kinds, "Radar", () => Settings.LinesRadar.Value, v => Settings.LinesRadar.Value = v, "Search and fire-control radar");
        MapChip(kinds, "ESM", () => Settings.LinesEsm.Value, v => Settings.LinesEsm.Value = v, "ESM bearing lines and cones");
        MapChip(kinds, "Visual", () => Settings.LinesVisual.Value, v => Settings.LinesVisual.Value = v, "Visual and infrared");
        MapChip(kinds, "Sonar", () => Settings.LinesSonar.Value, v => Settings.LinesSonar.Value = v, "Passive and active sonar");
        MapChip(kinds, "Hide tracked ESM", () => Settings.HideTrackedEsm.Value, v => Settings.HideTrackedEsm.Value = v,
            "Leave out an ESM line for a contact a radar holds as a live track");
        panel.Children.Add(kinds);

        FlyoutHeader(panel, "Coverage of (selected unit)");
        var cover = new WrapPanel { Orientation = Orientation.Horizontal };
        MapChip(cover, "Radar", () => Settings.CoverRadar.Value, v => Settings.CoverRadar.Value = v, "Search radar coverage, shadowed by terrain");
        MapChip(cover, "ESM", () => Settings.CoverEsm.Value, v => Settings.CoverEsm.Value = v, "ESM listening range");
        MapChip(cover, "Visual", () => Settings.CoverVisual.Value, v => Settings.CoverVisual.Value = v, "Visual range, limited by the horizon");
        MapChip(cover, "Sonar", () => Settings.CoverSonar.Value, v => Settings.CoverSonar.Value = v, "Passive sonar range");
        MapChip(cover, "Terrain shadows", () => Settings.TerrainShadows.Value, v => Settings.TerrainShadows.Value = v, "Cut the radar coverage where terrain blocks the line of sight");
        panel.Children.Add(cover);

        var frame = (Border)GUI.ParseXaml(
            "<Border " + Ns + " Background='#F02C2D31' BorderBrush='#FF464850' BorderThickness='1' Padding='10,6,10,8' MaxWidth='440' " +
            "HorizontalAlignment='Right' VerticalAlignment='Bottom' Margin='0,0,8,2' Visibility='Collapsed'/>");
        frame.Child = panel;
        flyout = frame;
        barBtn.Click += (s, e) =>
        {
            try
            {
                frame.Visibility = frame.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                ModLog.Debug("Flyout " + (FlyoutOpen ? "opened" : "closed") + ".");
                Sync();
            }
            catch (Exception ex) { ModLog.Error("Flyout click: " + ex); }
        };

        var strip = (StackPanel)GUI.ParseXaml("<StackPanel " + Ns + " Orientation='Horizontal'/>");
        strip.Children.Add(barBtn);
        Grid.SetRow(strip, 2);
        Grid.SetColumn(strip, newColumn);
        grid.Children.Add(strip);
        // The flyout is a panel laid over the map just above the bottom bar (not a popup): the map stays usable around it, like the
        // game's own windows, and it stays until the icon is clicked again.
        Grid.SetRow(frame, 0);
        Grid.SetRowSpan(frame, Math.Max(1, Math.Min(2, grid.RowDefinitions.Count - 1)));
        Grid.SetColumn(frame, 0);
        Grid.SetColumnSpan(frame, newColumn + 1);
        grid.Children.Add(frame);
        ModLog.Debug("Flyout panel added: grid rows " + grid.RowDefinitions.Count + ", columns " + (newColumn + 1) + ".");
    }

    private static Border flyout;
    private static bool FlyoutOpen => flyout != null && flyout.Visibility == Visibility.Visible;

    private const string IconIdle = "#FF898377";    // the game's own bar icons: beige, peach while lit
    private static Border iconFace;
    private static ToggleButton barButton;

    private static bool LinesForAll() => Settings.LinesAllUnits.Value || Settings.LinesWho.Value == Settings.AllUnits;

    private static void SetLinesForAll(bool all)
    {
        Settings.LinesAllUnits.Value = false;
        Settings.LinesWho.Value = all ? Settings.AllUnits : Settings.SelectedOnly;
    }

    private static void FlyoutHeader(Panel into, string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(2, 6, 0, 2) };
        if (t.TryFindResource("Brush.Foreground.Normal") is Brush ink) t.Foreground = ink;
        into.Children.Add(t);
    }

    private const string ChipXaml =
        "<ToggleButton xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Focusable='False' Height='28' Margin='0,0,4,4' " +
        "Style='{DynamicResource SimpleToggleButton}'>" +
        "<Border Height='28' MinWidth='40' Padding='8,3'><TextBlock HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ToggleButton>";

    private static readonly Brush ChipOff = new SolidColorBrush(Noesis.Color.FromRgb(0x89, 0x83, 0x77));    // the game's own bar icons: beige,
    private static readonly Brush ChipOn = new SolidColorBrush(Noesis.Color.FromRgb(0xF1, 0xC6, 0x93));     // peach when on
    private static readonly Brush ChipInk = new SolidColorBrush(Noesis.Color.FromRgb(0x2A, 0x26, 0x27));

    /// <summary>Peach when on, beige when off; colours are set directly (a popup may not see the game's resources).</summary>
    private static void Light(Border face, TextBlock label, bool on)
    {
        face.Background = on ? ChipOn : ChipOff;
        label.Foreground = ChipInk;
    }

    private static ToggleButton Chip(string text, out TextBlock label, out Border face)
    {
        var button = (ToggleButton)GUI.ParseXaml(ChipXaml);
        face = (Border)button.Content;
        label = (TextBlock)face.Child;
        label.Text = text;
        Light(face, label, false);
        return button;
    }

    /// <summary>A map switch in the flyout: lit while on, follows changes made elsewhere (Layers menu, hot keys) on each Sync.</summary>
    private static void MapChip(Panel into, string text, Func<bool> read, Action<bool> write, string tip)
    {
        ToggleButton button = Chip(text, out TextBlock label, out Border face);
        button.ToolTip = tip;
        var chip = new MapSwitch { Button = button, Label = label, Face = face, Read = read, Lit = false };
        button.Click += (s, e) =>
        {
            if (syncing) return;
            write(button.IsChecked == true);
            Sync();
        };
        MapSwitches.Add(chip);
        into.Children.Add(button);
    }

    private sealed class MapSwitch
    {
        public ToggleButton Button;
        public TextBlock Label;
        public Border Face;
        public Func<bool> Read;
        public bool Lit;
    }

    private static readonly List<MapSwitch> MapSwitches = new List<MapSwitch>();

    private static void Ink(Border face, TextBlock label, ref bool lit, bool on)
    {
        if (lit == on) return;
        Light(face, label, on);
        lit = on;
    }

    /// <summary>The bar icon is peach while the flyout is open.</summary>
    private static void SyncMain()
    {
        if (iconFace == null) return;
        bool on = FlyoutOpen;          // peach only while the flyout is open; the chips inside show which windows are open
        iconFace.Background = new SolidColorBrush(Noesis.Color.FromRgb(on ? (byte)0xF1 : (byte)0x89, on ? (byte)0xC6 : (byte)0x83, on ? (byte)0x93 : (byte)0x77));
        if (barButton != null && barButton.IsChecked == true) { syncing = true; barButton.IsChecked = false; syncing = false; }
    }

    private static string HotKeyText(SvWindow w)
    {
        string key = w is BoardWindow ? Settings.BoardKey.Value : w is ScopeWindow ? Settings.ScopeKey.Value : Settings.SonarKey.Value;
        return string.IsNullOrWhiteSpace(key) ? "no hot key" : key;
    }

    private static bool IsOpen(Entry e) =>
        view != null && WindowHostCommands.CloseWindowCommand.CanExecute(e.Window, view);

    private static void Open(Entry e)
    {
        if (view == null || IsOpen(e)) { ModLog.Debug("Open " + e.Text + ": already open or no view"); return; }
        try
        {
            WindowHostCommands.OpenWindowCommand.Execute(e.Window, view);
            e.Open = IsOpen(e);
            ModLog.Debug("Open " + e.Text + ": executed, open now = " + e.Open);
        }
        catch (Exception ex) { ModLog.Error("Open " + e.Text + ": " + ex); }
        Sync();
    }

    private static void Close(Entry e)
    {
        if (view != null && IsOpen(e))
            WindowHostCommands.CloseWindowCommand.Execute(e.Window, view);
        e.Open = false;
        Sync();
    }

    public static void Toggle(SvWindow window)
    {
        foreach (Entry e in Entries)
        {
            if (!ReferenceEquals(e.Window, window)) continue;
            if (IsOpen(e)) Close(e); else Open(e);
            return;
        }
    }

    public static SvWindow Window(int index) => index >= 0 && index < Entries.Count ? Entries[index].Window : null;

    /// <summary>The bar buttons follow whether their window is open (also when it was closed with its X or moved by Pop-Out).</summary>
    public static void Sync()
    {
        if (view == null || syncing) return;
        syncing = true;
        try
        {
            foreach (Entry e in Entries)
            {
                bool wasOpen = e.Open;
                bool open = e.Open = IsOpen(e);
                if (wasOpen != open) ModLog.Debug("Window " + e.Text + " is now " + (open ? "open" : "closed") + " (seen by sync).");
                if (e.Button == null) continue;
                if (e.Button.IsChecked != open) e.Button.IsChecked = open;
                Ink(e.Face, e.Label, ref e.Lit, open);
            }
            if (FlyoutOpen)         // the map switches only matter while the flyout is shown
                foreach (MapSwitch m in MapSwitches)
                {
                    bool on = false;
                    try { on = m.Read(); } catch (Exception) { }
                    if (m.Button.IsChecked != on) m.Button.IsChecked = on;
                    Ink(m.Face, m.Label, ref m.Lit, on);
                }
        }
        finally { syncing = false; }
        SyncMain();
    }

    public static bool AnyOpen()
    {
        if (view == null) return false;
        foreach (Entry e in Entries)
            if (e.Open) return true;
        return false;
    }

    public static bool IsOpen(SvWindow w)
    {
        foreach (Entry e in Entries)
            if (ReferenceEquals(e.Window, w)) return e.Open;
        return false;
    }
}

/// <summary>Diagnostic for "the window grows but will not shrink": logs, on each resize of a scope or sonar window, the size
/// numbers of the view and of every parent up to the game's Window (verbose log only, at most 80 lines).</summary>
internal static class SizeProbe
{
    private static int lines;

    public static void Watch(FrameworkElement view, string name)
    {
        view.SizeChanged += (s, e) => Log(view, name);
    }

    private static void Log(FrameworkElement view, string name)
    {
        if (lines >= 80) return;
        try
        {
            var sb = new System.Text.StringBuilder("[size] " + name + ": ");
            DependencyObject d = view;
            int depth = 0;
            while (d != null && depth++ < 12)
            {
                if (d is FrameworkElement fe)
                    sb.Append(fe.GetType().Name + " act=" + fe.ActualWidth.ToString("0") + "x" + fe.ActualHeight.ToString("0") +
                              " set=" + fe.Width.ToString("0") + "x" + fe.Height.ToString("0") +
                              " min=" + fe.MinWidth.ToString("0") + "x" + fe.MinHeight.ToString("0") +
                              " want=" + fe.DesiredSize.Width.ToString("0") + "x" + fe.DesiredSize.Height.ToString("0") + " | ");
                if (d is SeapowerUI.Window) break;
                d = VisualTreeHelper.GetParent(d);
            }
            lines++;
            ModLog.Debug(sb.ToString());
        }
        catch (Exception) { }
    }
}
