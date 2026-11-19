using System;
using System.Collections.Generic;
using Noesis;
using SeapowerUI;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>
/// Puts the three windows into the game's own window system.
/// A game window is a content object (a view model) in the main view's <c>WindowHost.Windows</c>; the host shows it through the
/// <c>DataTemplate</c> registered for the object's type, and that template is a <c>SeapowerUI.Window</c> with the game's chrome
/// (header, close button, resize thumbs, <c>WindowLayoutNoScroll</c>). The mod parses one resource dictionary with such a template
/// per window type and merges it into the main view's resources, then opens a window with the game's
/// <c>WindowHostCommands.OpenWindowCommand</c>, exactly as the game opens its own. Pop-Out Windows keys on the content's type name,
/// so it gives the new windows its pop-out button without knowing about this mod.
/// Opened from the flyout of a button added to the bottom bar (styled like the game's own) or from hot keys.
/// </summary>
internal static class Kit
{
    private sealed class Entry
    {
        public SvWindow Window;
        public string Text;
        public Chip Chip;
        public bool Open;           // as of the last Sync / Open / Close; asking the game's command costs a tree walk
    }

    /// <summary>A toggle in the flyout: peach while on, beige while off.</summary>
    private sealed class Chip
    {
        public ToggleButton Button;
        public TextBlock Label;
        public Border Face;
        public Func<bool> Read;     // map switches only: read on each Sync while the flyout is shown
        public bool Lit;

        public void Light(bool on)
        {
            if (Lit == on) return;
            Face.Background = on ? ChipOn : ChipOff;
            Label.Foreground = ChipInk;
            Lit = on;
        }
    }

    private static readonly List<Entry> Entries = new List<Entry>();
    private static readonly List<Chip> MapChips = new List<Chip>();
    private static MainGameView view;
    private static bool syncing;
    private static Border flyout;
    private static Border iconFace;
    private static ToggleButton barButton;

    // The game's own bar icons: beige, peach while lit, dark ink. Set directly (a popup may not see the game's resources).
    private const uint IdleColour = 0xFF898377, LitColour = 0xFFF1C693, InkColour = 0xFF2A2627;
    private static readonly Brush ChipOff = GameColours.Of(IdleColour), ChipOn = GameColours.Of(LitColour), ChipInk = GameColours.Of(InkColour);

    private static string Hex(uint argb) => "#" + argb.ToString("X8");

    private const string Ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";

    // The window types' own namespace, which differs between the two builds (see the namespace line above).
    private static readonly string Header =
        Ns + " xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
        "xmlns:local='clr-namespace:SeapowerUI' xmlns:sv='clr-namespace:" + typeof(SvWindow).Namespace + "'";

    // The game's resize command clamps a dragged edge to "size minus MinWidth/MinHeight": with no minimum (0) the window could
    // grow but never shrink.
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

    public static BoardWindow Board => Entries.Count > 0 ? (BoardWindow)Entries[0].Window : null;
    public static ScopeWindow Scope => Entries.Count > 1 ? (ScopeWindow)Entries[1].Window : null;
    public static SonarWindow Sonar => Entries.Count > 2 ? (SonarWindow)Entries[2].Window : null;

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
            // A game build without the no-scroll window layout: the plain one still works, only with scroll bars.
            ModLog.Warn("Window template with the game's no-scroll layout failed (" + ex.Message + "); using the default window layout.");
            dict = Dictionary(dynamicTemplate: false);
        }
        main.Resources.MergedDictionaries.Add(dict);

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
        MapChips.Clear();
        flyout = null;
        iconFace = null; barButton = null;
        view = null;
    }

    /// <summary>One icon button at the right end of the bottom bar; a click opens a flyout above it with the window and map toggles.</summary>
    private static void AddBarButtons(MainGameView main)
    {
        var grid = main.FindName("DisplayAreaGrid") as Grid;
        if (grid == null) throw new InvalidOperationException("DisplayAreaGrid not found");
        int newColumn = grid.ColumnDefinitions.Count;
        foreach (UIElement child in grid.Children)
            if (Grid.GetColumnSpan(child) == newColumn) Grid.SetColumnSpan(child, newColumn + 1);   // keep the full-width layers full width
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        barButton = BarButton();
        Border frame = Flyout();
        barButton.Click += (s, e) =>
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
        strip.Children.Add(barButton);
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

    private static ToggleButton BarButton()
    {
        string ink = Hex(InkColour);
        var icon = (Border)GUI.ParseXaml(
            "<Border " + Ns + " Width='30' Height='28' Background='" + Hex(IdleColour) + "'>" +
            "<Viewbox Margin='3'><Canvas Width='24' Height='24'>" +
            "<Ellipse Canvas.Left='2' Canvas.Top='2' Width='20' Height='20' Stroke='" + ink + "' StrokeThickness='2'/>" +
            "<Path Data='M12,2 L12,22 M2,12 L22,12' Stroke='" + ink + "' StrokeThickness='1'/>" +
            "<Path Data='M12,12 L12,2 A10,10 0 0 1 20.2,6.3 Z' Fill='" + ink + "'/>" +
            "<Ellipse Canvas.Left='14.5' Canvas.Top='14.5' Width='3.5' Height='3.5' Fill='" + ink + "'/>" +
            "</Canvas></Viewbox></Border>");
        iconFace = icon;
        var button = (ToggleButton)GUI.ParseXaml(
            "<ToggleButton " + Ns + " Focusable='False' Height='32' Margin='4,0,0,0' Style='{DynamicResource SimpleToggleButton}'/>");
        button.Content = icon;
        button.ToolTip = "R.A.T.S. sensor views: windows and map layers";
        return button;
    }

    /// <summary>The flyout: the three window toggles, then the map switches by group.</summary>
    private static Border Flyout()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        FlyoutHeader(panel, "Windows");
        var windowsRow = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Entry entry in Entries)
        {
            entry.Chip = MakeChip(entry.Text, entry.Window.Title + " (" + HotKeyText(entry.Window) + ")");
            Entry captured = entry;
            // The click decides, from the window's real state; the check mark is only ever set by Sync (events from it are ignored).
            entry.Chip.Button.Click += (s, e) => { ModLog.Debug("Chip clicked: " + captured.Text); Toggle(captured.Window); };
            windowsRow.Children.Add(entry.Chip.Button);
        }
        panel.Children.Add(windowsRow);
        WrapPanel row = null;
        SwitchGroup? group = null;
        foreach (MapSwitch sw in MapSwitch.All)
        {
            if (sw.Group != group)
            {
                group = sw.Group;
                FlyoutHeader(panel, MapSwitch.Heading(sw.Group));
                panel.Children.Add(row = new WrapPanel { Orientation = Orientation.Horizontal });
            }
            row.Children.Add(MapChip(sw));
        }
        var frame = (Border)GUI.ParseXaml(
            "<Border " + Ns + " Background='#F02C2D31' BorderBrush='#FF464850' BorderThickness='1' Padding='10,6,10,8' MaxWidth='440' " +
            "HorizontalAlignment='Right' VerticalAlignment='Bottom' Margin='0,0,8,2' Visibility='Collapsed'/>");
        frame.Child = panel;
        flyout = frame;
        return frame;
    }

    private static bool FlyoutOpen => flyout != null && flyout.Visibility == Visibility.Visible;

    private static void FlyoutHeader(Panel into, string text)
    {
        var t = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(2, 6, 0, 2) };
        if (t.TryFindResource("Brush.Foreground.Normal") is Brush ink) t.Foreground = ink;
        into.Children.Add(t);
    }

    private const string ChipXaml =
        "<ToggleButton " + Ns + " Focusable='False' Height='28' Margin='0,0,4,4' " +
        "Style='{DynamicResource SimpleToggleButton}'>" +
        "<Border Height='28' MinWidth='40' Padding='8,3'><TextBlock HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ToggleButton>";

    private static Chip MakeChip(string text, string tip)
    {
        var button = (ToggleButton)GUI.ParseXaml(ChipXaml);
        var face = (Border)button.Content;
        var chip = new Chip { Button = button, Face = face, Label = (TextBlock)face.Child, Lit = true };
        chip.Label.Text = text;
        button.ToolTip = tip;
        chip.Light(false);
        return chip;
    }

    /// <summary>A map switch in the flyout: lit while on, follows changes made elsewhere (board, Options, hot keys) on each Sync.</summary>
    private static ToggleButton MapChip(MapSwitch sw)
    {
        Chip chip = MakeChip(sw.Label, sw.Tip);
        chip.Read = sw.Read;
        chip.Button.Click += (s, e) =>
        {
            if (syncing) return;
            sw.Write(chip.Button.IsChecked == true);
            Sync();
        };
        MapChips.Add(chip);
        return chip.Button;
    }

    /// <summary>The bar icon is peach while the flyout is open; the chips inside show which windows are open.</summary>
    private static void SyncIcon()
    {
        if (iconFace == null) return;
        iconFace.Background = FlyoutOpen ? ChipOn : ChipOff;
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
                if (e.Chip == null) continue;
                if (e.Chip.Button.IsChecked != open) e.Chip.Button.IsChecked = open;
                e.Chip.Light(open);
            }
            if (FlyoutOpen)         // the map switches only matter while the flyout is shown
                foreach (Chip m in MapChips)
                {
                    bool on = m.Read();
                    if (m.Button.IsChecked != on) m.Button.IsChecked = on;
                    m.Light(on);
                }
        }
        finally { syncing = false; }
        SyncIcon();
    }

    /// <summary>
    /// A new snapshot. The sonar waterfall takes one row per slow snapshot (150 rows = 150 s), the board and the sonar redraw once a
    /// second (<paramref name="slow"/>); only the scope's picture follows the fast snapshots.
    /// </summary>
    public static void Snapshot(bool slow)
    {
        if (!slow)
        {
            foreach (Surface s in Registry.Surfaces)
                if (s is ScopeSurface) s.InvalidateVisual();
            return;
        }
        if (IsOpen(Sonar)) SonarSampler.Update(Sonar);
        if (IsOpen(Scope)) Scope.PushAltitude();
        SonarSurface.RaiseChanged();            // the sonar chooser's name line
        Registry.RefreshAll();
    }

    /// <summary>About 25 times a second while a window is open: the scope's sweep and blips, the range glide.</summary>
    public static void Animate()
    {
        for (int i = 0; i < Registry.Surfaces.Count; i++)
            if (Registry.Surfaces[i].Animated) Registry.Surfaces[i].Animate();
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
