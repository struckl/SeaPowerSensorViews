using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace SeaPowerSensorViews.Ui;

/// <summary>
/// The mod's settings in the game's Options menu. When an options page (SeapowerUI.OptionsMenuView, a Noesis UserControl) has
/// loaded, a tab "Sensor Views" is added to its tab control, styled like the game's own tabs; if the page has no tab control (a
/// game update), the settings go as a group at the end of the Audio tab instead, found through its named controls (the way the
/// Radio mod adds its group). Changes apply at once and are saved in the mod's config (the page's Accept / Revert do not touch them).
/// Hot keys and the rarer settings stay in the config file.
/// </summary>
internal static class OptionsPage
{
    private const string Tag = "SeaPowerSensorViews.Options";
    private static bool reported;

    /// <summary>From the options page's constructor (Hooks): builds the settings once the page is loaded.</summary>
    public static void ViewCreated(object instance)
    {
        if (!(instance is Noesis.FrameworkElement view)) return;
        view.Loaded += (s, e) => Attach(view);
    }

    private static void Attach(Noesis.FrameworkElement view)
    {
        try
        {
            Noesis.TabControl tabs = Find<Noesis.TabControl>(view, 24);
            if (tabs != null)
            {
                foreach (object item in tabs.Items)
                    if (item is Noesis.FrameworkElement fe && fe.Tag as string == Tag) return;
                var look = new Look(view);
                var panel = new Noesis.StackPanel { Orientation = Noesis.Orientation.Vertical, Margin = new Noesis.Thickness(20, 16, 20, 16) };
                Fill(look, panel);
                var tab = new Noesis.TabItem
                {
                    Tag = Tag,
                    Header = "Sensor Views",
                    Content = new Noesis.ScrollViewer { VerticalScrollBarVisibility = Noesis.ScrollBarVisibility.Auto, Content = panel },
                };
                foreach (object item in tabs.Items)
                    if (item is Noesis.TabItem other && other.Style != null) { tab.Style = other.Style; break; }
                tabs.Items.Add(tab);
                Report("Sensor Views settings added as a tab of the Options menu.");
                return;
            }
            Noesis.StackPanel audio = FindAudioPanel(view);
            if (audio == null)
            {
                Report("Options menu layout not recognised (game update?) - the Sensor Views settings are only in the config file.", warn: true);
                return;
            }
            for (int i = 0; i < audio.Children.Count; i++)
                if (audio.Children[i] is Noesis.FrameworkElement fe && fe.Tag as string == Tag) return;
            var group = new Noesis.StackPanel { Tag = Tag, Orientation = Noesis.Orientation.Vertical };
            var l = new Look(view);
            group.Children.Add(Header(l, "R.A.T.S. Sensor Views"));
            Fill(l, group);
            audio.Children.Add(group);
            Report("Sensor Views settings added to the Audio tab of the Options menu (no tab control found).");
        }
        catch (Exception ex)
        {
            Report("Sensor Views settings not added to the Options menu: " + ex.Message, warn: true);
        }
    }

    private static void Report(string text, bool warn = false)
    {
        if (reported) return;
        reported = true;
        if (warn) ModLog.Warn(text); else ModLog.Info(text);
    }

    // ---- the settings -------------------------------------------------------------------------------------------------

    private static void Fill(Look look, Noesis.StackPanel panel)
    {
        panel.Children.Add(Note(look, "Changes apply at once and are kept by the mod (Accept and Revert on this page do not affect them). " +
            "Hot keys and further settings: BepInEx\\config\\dev.sewerlabs.seapowersensorviews.cfg."));

        panel.Children.Add(Header(look, "Sensor lines on the map"));
        panel.Children.Add(Note(look, "Switch the layer itself in the map's Layers menu or with Ctrl+Shift+PageUp."));
        panel.Children.Add(Choice(look, "Units shown", Settings.LinesWho, new[] { Settings.SelectedOnly, Settings.AllUnits },
            "Selected only: the lines of the unit you have selected (nothing while no unit is selected). All: every own unit's lines, the selected unit's bright. With many units, all lines are a lot to draw."));
        panel.Children.Add(Check(look, "Show lost contacts", Settings.LostLines,
            "A contact a sensor has just lost keeps a faint line for the lost contact memory time."));
        panel.Children.Add(Check(look, "Fill wedges when zoomed in", Settings.ZoomedWedges,
            "Off: zoomed in so far that a bearing wedge is longer than the screen, only its bearing line is drawn (faster). On: always filled."));
        panel.Children.Add(Slider(look, "Dimmed line opacity", "Lines of the units that are not selected (with Units shown = All).",
            Settings.DimOpacity, 0f, 1f, 0.05f, v => Math.Round(v * 100) + "%"));
        panel.Children.Add(Slider(look, "Line weight", "Multiplier on the game's own map line weight.",
            Settings.LineWidth, 0.5f, 4f, 0.25f, v => v.ToString("0.##") + "x"));
        panel.Children.Add(Slider(look, "Bearing wedge length", "Length of the line and wedge for bearing-only contacts (ESM, passive sonar).",
            Settings.WedgeNm, 5f, 300f, 5f, v => Math.Round(v) + " nm"));

        panel.Children.Add(Header(look, "Coverage of the selected unit"));
        panel.Children.Add(Note(look, "Switch the layer in the map's Layers menu or with Ctrl+Shift+PageDown."));
        panel.Children.Add(Check(look, "Radar", Settings.CoverRadar, "Search radar coverage against the target altitude (the scope's slider)."));
        panel.Children.Add(Check(look, "Terrain shadows", Settings.TerrainShadows, "Cut the radar coverage where terrain blocks the line of sight."));
        panel.Children.Add(Check(look, "ESM", Settings.CoverEsm, "ESM listening range against an emitter like the unit's own search radar."));
        panel.Children.Add(Check(look, "Visual", Settings.CoverVisual, "Visual range, limited by the horizon."));
        panel.Children.Add(Check(look, "Passive sonar", Settings.CoverSonar, "Passive sonar range with the dent of the baffle sector."));

        panel.Children.Add(Header(look, "Windows"));
        panel.Children.Add(Check(look, "Follow the selected unit", Settings.FollowSelection,
            "The scope, the sonar display and the board's sensor panel show the unit selected in the game. Off: the unit clicked on the board."));
        panel.Children.Add(Slider(look, "Lost contact memory", "How long a lost contact stays on the board and scope, dimmed.",
            Settings.MemorySeconds, 0f, 600f, 10f, v => Math.Round(v) + " s"));
        panel.Children.Add(Check(look, "Bottom bar buttons", Settings.BarButtons,
            "The Board, Scope and Sonar buttons at the right end of the bottom bar. Takes effect at the next mission."));
    }

    // ---- look and controls (the page's own styles where it has them) ----------------------------------------------------

    private sealed class Look
    {
        public readonly Noesis.Style Header, Label;
        public readonly Noesis.Brush Highlight, Background;
        public readonly float NoteSize;

        public Look(Noesis.FrameworkElement view)
        {
            Header = view.TryFindResource("OptionsSectionHeader") as Noesis.Style;
            Label = view.TryFindResource("TextBlockLabel") as Noesis.Style;
            Highlight = view.TryFindResource("Brush.Foreground.Highlight") as Noesis.Brush;
            Background = view.TryFindResource("Brush.Background.Normal") as Noesis.Brush;
            object size = view.TryFindResource("Font.Size.ToolTip");
            NoteSize = size is float f ? f : size is double d ? (float)d : 0f;
        }
    }

    private static Noesis.TextBlock Header(Look look, string text)
    {
        var header = new Noesis.TextBlock { Text = text, Margin = new Noesis.Thickness(0, 14, 0, 8), FontSize = 18f };
        if (look.Header != null) header.Style = look.Header;
        else if (look.Highlight != null) header.Foreground = look.Highlight;
        return header;
    }

    private static Noesis.TextBlock Note(Look look, string text)
    {
        var note = new Noesis.TextBlock { Text = text, TextWrapping = Noesis.TextWrapping.Wrap, Margin = new Noesis.Thickness(0, 0, 0, 12), Opacity = 0.8f };
        if (look.Highlight != null) note.Foreground = look.Highlight;
        if (look.NoteSize > 0f) note.FontSize = look.NoteSize;
        return note;
    }

    private static Noesis.CheckBox Check(Look look, string label, ConfigEntry<bool> entry, string tip)
    {
        var box = new Noesis.CheckBox { Content = label, Margin = new Noesis.Thickness(0, 0, 0, 12), IsChecked = entry.Value, ToolTip = tip };
        if (look.Highlight != null) box.Foreground = look.Highlight;
        box.Click += (s, e) => entry.Value = box.IsChecked == true;
        return box;
    }

    private static Noesis.Grid Row(Look look, string label, string tip, out int next)
    {
        var row = new Noesis.Grid { Margin = new Noesis.Thickness(0, 0, 0, 12), ToolTip = tip };
        row.ColumnDefinitions.Add(new Noesis.ColumnDefinition { Width = new Noesis.GridLength(200f) });
        row.ColumnDefinitions.Add(new Noesis.ColumnDefinition { Width = new Noesis.GridLength(1, Noesis.GridUnitType.Star) });
        row.ColumnDefinitions.Add(new Noesis.ColumnDefinition { Width = new Noesis.GridLength(1, Noesis.GridUnitType.Auto) });
        var text = new Noesis.TextBlock { Text = label, VerticalAlignment = Noesis.VerticalAlignment.Center };
        if (look.Label != null) text.Style = look.Label;
        else if (look.Highlight != null) text.Foreground = look.Highlight;
        Noesis.Grid.SetColumn(text, 0);
        row.Children.Add(text);
        next = 1;
        return row;
    }

    private static Noesis.Grid Choice(Look look, string label, ConfigEntry<string> entry, IList<string> values, string tip)
    {
        Noesis.Grid row = Row(look, label, tip, out int col);
        var combo = new Noesis.ComboBox { MinWidth = 240, HorizontalAlignment = Noesis.HorizontalAlignment.Left };
        foreach (string v in values) combo.Items.Add(v);
        combo.SelectedIndex = Math.Max(0, values.IndexOf(entry.Value));
        combo.SelectionChanged += (s, e) =>
        {
            int i = combo.SelectedIndex;
            if (i >= 0 && entry.Value != values[i]) entry.Value = values[i];
        };
        Noesis.Grid.SetColumn(combo, col);
        row.Children.Add(combo);
        return row;
    }

    private static Noesis.Grid Slider(Look look, string label, string tip, ConfigEntry<float> entry, float min, float max, float step,
        Func<float, string> format)
    {
        Noesis.Grid row = Row(look, label, tip, out int col);
        var slider = new Noesis.Slider
        {
            Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true,
            Value = Math.Max(min, Math.Min(max, entry.Value)), MinWidth = 200,
        };
        Noesis.Grid.SetColumn(slider, col);
        var value = new Noesis.TextBlock
        {
            Text = format(slider.Value), MinWidth = 64, Margin = new Noesis.Thickness(12, 0, 0, 0),
            VerticalAlignment = Noesis.VerticalAlignment.Center,
        };
        if (look.Highlight != null) value.Foreground = look.Highlight;
        Noesis.Grid.SetColumn(value, col + 1);
        slider.ValueChanged += (s, e) =>
        {
            float v = (float)(Math.Round(slider.Value / step) * step);
            value.Text = format(v);
            if (Math.Abs(entry.Value - v) > step * 0.01f) entry.Value = v;
        };
        row.Children.Add(slider);
        row.Children.Add(value);
        return row;
    }

    // ---- finding the page's parts ------------------------------------------------------------------------------------------

    private static T Find<T>(Noesis.DependencyObject root, int maxDepth) where T : Noesis.DependencyObject
    {
        var queue = new Queue<(Noesis.DependencyObject node, int depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (node is T hit) return hit;
            if (depth >= maxDepth) continue;
            int n = Noesis.VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
                queue.Enqueue((Noesis.VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return null;
    }

    private static Noesis.StackPanel FindAudioPanel(Noesis.FrameworkElement view)
    {
        foreach (string name in new[] { "cb_MuteSoundInPause", "cb_SimultaneousVoiceovers", "sliderVoiceVolume", "sliderMasterVolume" })
        {
            Noesis.DependencyObject at = view.FindName(name) as Noesis.DependencyObject;
            for (int depth = 0; at != null && depth < 6; depth++)
            {
                at = at is Noesis.FrameworkElement fe && fe.Parent != null ? fe.Parent : Noesis.VisualTreeHelper.GetParent(at);
                if (at is Noesis.StackPanel sp && sp.Orientation == Noesis.Orientation.Vertical) return sp;
            }
        }
        return null;
    }
}
