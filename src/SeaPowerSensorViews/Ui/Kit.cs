using System;
using System.Collections.Generic;
using Noesis;
using SeaPower;
using SeapowerUI;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Ui;

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
        public string Text;
    }

    private static readonly List<Entry> Entries = new List<Entry>();
    private static MainGameView view;
    private static bool syncing;
    private static bool subscribed;

    public static bool Active => view != null;

    private const string Header =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
        "xmlns:local='clr-namespace:SeapowerUI' xmlns:sv='clr-namespace:SeaPowerSensorViews.Ui'";

    private static string Template(string type, string content, int width, int height, int x, int y, bool dynamicTemplate) =>
        "<DataTemplate DataType='{x:Type sv:" + type + "}'>" +
        "<local:Window " + (dynamicTemplate ? "Template='{DynamicResource WindowLayoutNoScroll}' " : "") +
        "Width='" + width + "' Height='" + height + "' X='" + x + "' Y='" + y + "'>" +
        "<local:Window.Header><TextBlock Text='{Binding Title}'/></local:Window.Header>" +
        "<sv:" + content + "/></local:Window></DataTemplate>";

    private static ResourceDictionary Dictionary(bool dynamicTemplate) =>
        (ResourceDictionary)GUI.ParseXaml(
            "<ResourceDictionary " + Header + ">" +
            Template("BoardWindow", "BoardView", 900, 640, 150, 80, dynamicTemplate) +
            Template("ScopeWindow", "ScopeView", 660, 700, 260, 100, dynamicTemplate) +
            Template("SonarWindow", "SonarView", 860, 560, 200, 130, dynamicTemplate) +
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
        Entries.Add(new Entry { Window = new BoardWindow(), Text = "Board" });
        Entries.Add(new Entry { Window = new ScopeWindow(), Text = "Scope" });
        Entries.Add(new Entry { Window = new SonarWindow(), Text = "Sonar" });
        view = main;
        if (Settings.BarButtons.Value)
        {
            try { AddBarButtons(main); }
            catch (Exception ex) { ModLog.Warn("Bottom bar buttons not added (" + ex.Message + ") - use the hot keys."); }
        }
        if (!subscribed)
        {
            subscribed = true;
            WindowHostCommands.CloseWindowCommand.CanExecuteChanged += (s, e) => Sync();
        }
        ModLog.Info("Windows registered: sensor board, radar scope, sonar display" + (Settings.BarButtons.Value ? " (bottom bar buttons added)." : "."));
        return true;
    }

    public static void Teardown()
    {
        foreach (Entry e in Entries) e.Window.Pinned = null;
        Entries.Clear();
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

        var strip = (StackPanel)GUI.ParseXaml("<StackPanel xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Orientation='Horizontal'/>");
        foreach (Entry entry in Entries)
        {
            var button = (ToggleButton)GUI.ParseXaml(
                "<ToggleButton xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Focusable='False' Height='32' " +
                "Style='{DynamicResource SimpleToggleButton}'>" +
                "<Border Height='32' MinWidth='46' Padding='5'><Border.Style><Style TargetType='Border'><Style.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='False'><Setter Property='Background' Value='Transparent'/></Trigger>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='{DynamicResource Brush.Item.BackgroundOver}'/></Trigger>" +
                "</Style.Triggers></Style></Border.Style>" +
                "<TextBlock HorizontalAlignment='Center' VerticalAlignment='Center' Text='" + entry.Text + "'/></Border></ToggleButton>");
            entry.Button = button;
            entry.Label = (TextBlock)((Border)button.Content).Child;
            button.ToolTip = entry.Window.Title + " (" + HotKeyText(entry.Window) + ")";
            Entry captured = entry;
            button.Checked += (s, e) => { if (!syncing) Open(captured); };
            button.Unchecked += (s, e) => { if (!syncing) Close(captured); };
            strip.Children.Add(button);
        }
        Grid.SetRow(strip, 2);
        Grid.SetColumn(strip, newColumn);
        grid.Children.Add(strip);
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
        if (view == null || IsOpen(e)) return;
        WindowHostCommands.OpenWindowCommand.Execute(e.Window, view);
    }

    private static void Close(Entry e)
    {
        if (view != null && IsOpen(e))
            WindowHostCommands.CloseWindowCommand.Execute(e.Window, view);
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
                if (e.Button == null) continue;
                bool open = IsOpen(e);
                if (e.Button.IsChecked != open) e.Button.IsChecked = open;
                Brush ink = e.Label.TryFindResource(open ? "Brush.Foreground.Emphasis" : "Brush.Foreground.Normal") as Brush;
                if (ink != null) e.Label.Foreground = ink;
            }
        }
        finally { syncing = false; }
    }

    public static bool AnyOpen()
    {
        if (view == null) return false;
        foreach (Entry e in Entries)
            if (IsOpen(e)) return true;
        return false;
    }

    public static bool IsOpen(SvWindow w)
    {
        foreach (Entry e in Entries)
            if (ReferenceEquals(e.Window, w)) return IsOpen(e);
        return false;
    }
}
