using System;
using System.Text;
using Noesis;
using SeaPowerSensorViews.Data;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>Sensor board window content: legend line, the unit x contact grid, and the selected unit's sensors.</summary>
public sealed class BoardView : UserControl
{
    private bool syncing;
    private readonly System.Collections.Generic.List<System.Action> syncers = new System.Collections.Generic.List<System.Action>();

    /// <summary>A check box bound to a bool getter/setter; <paramref name="read"/> re-reads it on Sync.</summary>
    private CheckBox Check(Panel into, string text, System.Func<bool> read, System.Action<bool> write, string tip)
    {
        var box = new CheckBox { Content = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), ToolTip = tip };
        box.Click += (s, e) => { if (!syncing) write(box.IsChecked == true); };
        syncers.Add(() => { bool v = read(); if (box.IsChecked != v) box.IsChecked = v; });
        into.Children.Add(box);
        return box;
    }

    private void Sync()
    {
        syncing = true;
        try { foreach (var s in syncers) s(); }
        finally { syncing = false; }
    }

    /// <summary>
    /// Map layer switches: what used to live in the map's Layers menu. One "Map layers" button with the three dots; it opens a
    /// list with the switches, like the kebab button in the title bar of Pop-Out and Topo, instead of a wide row of check boxes.
    /// </summary>
    private Grid BuildMapBar()
    {
        var list = new StackPanel { Orientation = Orientation.Vertical };
        var white = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        list.Children.Add(new TextBlock { Text = "Map layers", FontWeight = FontWeight.Bold, Foreground = white, Margin = new Thickness(0, 0, 0, 4) });
        Check(list, "Sensor lines", () => Map.MapSet.LinesOn, v => { if (v != Map.MapSet.LinesOn) Map.MapSet.ToggleLines(); }, "Lines from each own unit to the contacts it holds").Margin = new Thickness(0, 2, 10, 2);
        Check(list, "Coverage", () => Map.MapSet.CoverageOn, v => { if (v != Map.MapSet.CoverageOn) Map.MapSet.ToggleCoverage(); }, "Where the selected unit's sensors would hold an imagined target").Margin = new Thickness(0, 2, 10, 2);

        var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        who.Children.Add(new TextBlock { Text = "Lines for", Foreground = white, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var sel = new RadioButton { Content = "selected unit", GroupName = "who" + GetHashCode(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var all = new RadioButton { Content = "all units", GroupName = "who" + GetHashCode(), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0), ToolTip = "Lines of every own unit, the selected one bright (coverage stays selected-unit only)" };
        sel.Click += (s, e) => { if (syncing) return; Settings.LinesAllUnits.Value = false; Settings.LinesWho.Value = Settings.SelectedOnly; };
        all.Click += (s, e) => { if (syncing) return; Settings.LinesAllUnits.Value = false; Settings.LinesWho.Value = Settings.AllUnits; };
        syncers.Add(() =>
        {
            bool everyone = Settings.LinesAllUnits.Value || Settings.LinesWho.Value == Settings.AllUnits;
            if (all.IsChecked != everyone) all.IsChecked = everyone;
            if (sel.IsChecked != !everyone) sel.IsChecked = !everyone;
        });
        who.Children.Add(sel);
        who.Children.Add(all);
        list.Children.Add(who);

        list.Children.Add(new TextBlock { Text = "Lines of", Foreground = white, Margin = new Thickness(0, 6, 0, 2) });
        var kinds = new StackPanel { Orientation = Orientation.Horizontal };
        Check(kinds, "radar", () => Settings.LinesRadar.Value, v => Settings.LinesRadar.Value = v, "Search and fire-control radar");
        Check(kinds, "ESM", () => Settings.LinesEsm.Value, v => Settings.LinesEsm.Value = v, "ESM bearing lines and cones");
        Check(kinds, "visual", () => Settings.LinesVisual.Value, v => Settings.LinesVisual.Value = v, "Visual and infrared");
        Check(kinds, "sonar", () => Settings.LinesSonar.Value, v => Settings.LinesSonar.Value = v, "Passive and active sonar");
        list.Children.Add(kinds);
        Check(list, "hide ESM of tracked", () => Settings.HideTrackedEsm.Value, v => Settings.HideTrackedEsm.Value = v,
            "Leave out an ESM line for a contact a radar holds as a live track").Margin = new Thickness(0, 4, 10, 0);

        var host = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (Noesis.GUI.ParseXaml("<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Width=\"12\" Height=\"12\" " +
                "StrokeThickness=\"1.3\" StrokeStartLineCap=\"Round\" StrokeEndLineCap=\"Round\" VerticalAlignment=\"Center\" " +
                "Data=\"M1,11 L11,2 M1,11 L11,6.5 M1,11 L6,1.5\" " +
                "Stroke=\"{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}\"/>") is UIElement dots)
            content.Children.Add(dots);
        content.Children.Add(new TextBlock { Text = "Map layers", Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Focusable = false, Padding = new Thickness(6, 2, 8, 2), ToolTip = "Sensor lines and coverage on the map" };
        var popup = new Popup
        {
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(240, 32, 32, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10),
                Child = list,
            },
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
        };
        // A click on the button while the list is open: the popup's light dismiss closes it first, so that click must not reopen it.
        DateTime closedAt = DateTime.MinValue;
        popup.Closed += (s, e) => closedAt = DateTime.UtcNow;
        button.Click += (s, e) =>
        {
            e.Handled = true;
            if (popup.IsOpen) { popup.IsOpen = false; return; }
            if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 250)
                return;
            Sync();
            popup.IsOpen = true;
        };
        host.Children.Add(button);
        host.Children.Add(popup);
        return host;
    }

    public BoardView()
    {
        var grid = new Grid { Margin = new Thickness(6) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(175) });
        var legend = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            Text = "R search radar   F fire control   E ESM   S passive sonar   A active sonar   V visual / infrared   M MAD" +
                   "      lower case = bearing only      dim = lost, last fix       number = age of the freshest fix",
        };
        Grid.SetRow(legend, 1);
        Grid mapBar;
        try { mapBar = BuildMapBar(); ModLog.Debug("Board: map bar built."); }
        catch (Exception ex) { ModLog.Error("Board map bar failed, board opens without it: " + ex); mapBar = new Grid(); }
        Grid.SetRow(mapBar, 0);
        var board = new BoardSurface { Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(board, 2);
        var panel = new PanelSurface();
        Grid.SetRow(panel, 3);
        grid.Children.Add(mapBar);
        grid.Children.Add(legend);
        grid.Children.Add(board);
        grid.Children.Add(panel);
        Content = grid;
        ModLog.Debug("Board: view built.");
        Loaded += (s, e) => { try { Sync(); } catch (Exception ex) { ModLog.Error("Board sync: " + ex); } };
        DataContextChanged += (s, e) => Sync();
        board.MouseEnter += (s, e) => Sync();      // catches a change made in the Layers menu or by hot key
    }
}

internal static class Fmt
{
    public static string Age(double seconds)
    {
        int s = (int)Math.Max(0, seconds);
        return s < 60 ? s + "s" : (s / 60) + ":" + (s % 60).ToString("00");
    }

    public static string Compass(float deg) => ((int)Math.Round(SensorPicture.Wrap360(deg)) % 360).ToString("000");
}

public sealed class BoardSurface : Surface
{
    private const float LabelW = 170f, HeaderH = 40f, RowH = 20f;
    private float colW = 80f;
    private int firstRow;

    public BoardSurface()
    {
        MouseLeftButtonDown += OnClick;
        MouseWheel += OnWheel;
    }

    private BoardWindow Win => DataContext as BoardWindow;

    private static Brush KindBrush(Surface s, Kind k)
    {
        switch (k)
        {
            case Kind.Radar: return s.Emphasis;
            case Kind.FireControl: return s.Red;
            case Kind.Esm: return s.Blue;
            case Kind.Passive: return s.Green;
            case Kind.Active: return s.Highlight;
            case Kind.Visual: return s.Normal;
            default: return s.Neutral;
        }
    }

    private Brush RelationBrush(int relation)
    {
        switch (relation)
        {
            case 1: return Friendly;
            case 2: return Neutral;
            case 3: return Hostile;
            default: return Unknown;
        }
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));
        SensorPicture pic = SensorPicture.Current;
        BoardWindow win = Win;
        if (!pic.InMission)
        {
            Draw(dc, "No mission running.", 8, 8, 12, Normal);
            return;
        }
        if (pic.Units.Count == 0 || pic.Contacts.Count == 0)
        {
            Draw(dc, pic.Units.Count == 0 ? "No own units." : "No contact is held by any of your sensors.", 8, 8, 12, Normal);
            return;
        }
        int cols = pic.Units.Count;
        colW = Math.Max(46f, Math.Min(120f, (w - LabelW) / cols));
        int visibleRows = Math.Max(1, (int)((h - HeaderH) / RowH));
        firstRow = Math.Max(0, Math.Min(firstRow, pic.Contacts.Count - visibleRows));
        UnitRow subject = win?.Subject();

        // header: unit names
        dc.DrawRectangle(Header, null, new Rect(0, 0, w, HeaderH));
        for (int c = 0; c < cols; c++)
        {
            UnitRow u = pic.Units[c];
            float x = LabelW + c * colW;
            if (x + colW > w + 1f) break;
            if (ReferenceEquals(u, subject))
            {
                dc.DrawRectangle(Fade(Red, 0.14f), null, new Rect(x, 0, colW, h));
                dc.DrawRectangle(Red, null, new Rect(x, HeaderH - 2f, colW, 2f));
            }
            string name = u.Name ?? "?";
            int fit = Math.Max(3, (int)(colW / 6.2f));
            string l1 = name.Length > fit ? name.Substring(0, fit) : name;
            string l2 = name.Length > fit ? name.Substring(fit, Math.Min(fit, name.Length - fit)) : "";
            DrawCentered(dc, l1, x + colW / 2, 4, 11, ReferenceEquals(u, subject) ? Highlight : Normal);
            DrawCentered(dc, l2, x + colW / 2, 18, 11, Normal);
            if (u.Emcon) DrawCentered(dc, "EMCON", x + colW / 2, 29, 9, Emphasis);
        }

        // rows: contacts
        for (int r = 0; r < visibleRows; r++)
        {
            int index = firstRow + r;
            if (index >= pic.Contacts.Count) break;
            ContactRow cr = pic.Contacts[index];
            float y = HeaderH + r * RowH;
            bool selRow = win != null && ReferenceEquals(win.SelContact, cr);
            if (selRow) dc.DrawRectangle(Fade(Red, 0.35f), null, new Rect(0, y, w, RowH));
            else if ((index & 1) == 1) dc.DrawRectangle(Fade(Header, 0.5f), null, new Rect(0, y, w, RowH));
            dc.DrawEllipse(RelationBrush(cr.Relation), null, new Point(9, y + RowH / 2), 4, 4);
            Draw(dc, cr.Label, 20, y + 3, 12, selRow ? Highlight : Normal);
            for (int c = 0; c < cols; c++)
            {
                float x = LabelW + c * colW;
                if (x + colW > w + 1f) break;
                DrawCell(dc, pic, cr, pic.Units[c], x, y);
            }
        }
        // grid lines
        dc.DrawLine(PenOf(Lines, 1f), new Point(LabelW, 0), new Point(LabelW, h));
        dc.DrawLine(PenOf(Lines, 1f), new Point(0, HeaderH), new Point(w, HeaderH));
        if (pic.Contacts.Count > visibleRows)
            DrawRight(dc, (firstRow + 1) + "-" + Math.Min(pic.Contacts.Count, firstRow + visibleRows) + " of " + pic.Contacts.Count + " (wheel scrolls)", w - 4, h - 14, 10, Normal);
    }

    private void DrawCell(DrawingContext dc, SensorPicture pic, ContactRow cr, UnitRow unit, float x, float y)
    {
        double freshest = double.MaxValue;
        float cx = x + 6;
        bool any = false;
        for (int k = 0; k < KindInfo.Letters.Length; k++)
        {
            Hit hit = null;
            for (int i = 0; i < cr.Hits.Count; i++)
                if (cr.Hits[i].Unit == unit && (int)cr.Hits[i].Kind == k) { hit = cr.Hits[i]; break; }
            if (hit == null) continue;
            any = true;
            char letter = KindInfo.Letters[k];
            string s = (hit.BearingOnly ? char.ToLowerInvariant(letter) : letter).ToString();
            Brush b = KindBrush(this, hit.Kind);
            Draw(dc, s, cx, y + 3, 12, hit.Live ? b : Fade(b, 0.4f));
            cx += 9f;
            double age = pic.Time - hit.FixTime;
            if (age < freshest) freshest = age;
        }
        if (any)
            DrawRight(dc, Fmt.Age(freshest), x + colW - 3, y + 5, 10, Normal);
        BoardWindow win = Win;
        if (win != null && ReferenceEquals(win.SelContact, cr) && ReferenceEquals(win.SelUnit, unit))
            dc.DrawRectangle(null, PenOf(Highlight, 1f), new Rect(x + 1, y + 1, colW - 2, RowH - 2));
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        BoardWindow win = Win;
        SensorPicture pic = SensorPicture.Current;
        if (win == null) return;
        Point p = e.GetPosition(this);
        int col = (int)Math.Floor((p.X - LabelW) / colW);
        bool dbl = e.ClickCount >= 2;
        if (col >= 0 && col < pic.Units.Count)
        {
            win.SelUnit = pic.Units[col];
            win.Pin(pic.Units[col]);
        }
        ContactRow clicked = null;
        if (p.Y >= HeaderH)
        {
            int row = firstRow + (int)((p.Y - HeaderH) / RowH);
            if (row >= 0 && row < pic.Contacts.Count) { clicked = pic.Contacts[row]; win.SelContact = clicked; }
        }
        // double click: a contact (its row) -> to where it is estimated; a unit's header -> to that unit
        if (dbl)
        {
            if (clicked != null) Jump.ToContact(clicked, col >= 0 && col < pic.Units.Count ? pic.Units[col] : null);
            else if (p.Y < HeaderH && col >= 0 && col < pic.Units.Count) Jump.ToUnit(pic.Units[col]);
        }
        e.Handled = true;
        Registry.RefreshAll();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        firstRow = Math.Max(0, firstRow + (e.Delta > 0 ? -3 : 3));
        e.Handled = true;
        InvalidateVisual();
    }
}

/// <summary>Lower half of the board: the clicked cell in words, then the selected unit's sensors.</summary>
public sealed class PanelSurface : Surface
{
    private BoardWindow Win => DataContext as BoardWindow;

    /// <summary>Bearing of a bearing-only track over its last fixes (track estimates only): a small strip plus the drift in degrees per minute.</summary>
    private void DrawHistory(DrawingContext dc, Hit hit, float x, float y, float w, float h)
    {
        var hist = hit.History;
        if (hist.Count < 2 || w < 60f) return;
        float t1 = hist[hist.Count - 1].Key, t0 = Math.Max(hist[0].Key, t1 - 300f);
        float last = hist[hist.Count - 1].Value, lo = 0f, hi = 0f;
        for (int i = 0; i < hist.Count; i++)
        {
            if (hist[i].Key < t0) continue;
            float d = Ang.DeltaAngle(last, hist[i].Value);
            if (d < lo) lo = d;
            if (d > hi) hi = d;
        }
        float span = Math.Max(2f, hi - lo);
        dc.DrawRectangle(Fade(Header, 0.8f), null, new Rect(x, y, w - 46f, h));
        float px = 0f, py = 0f;
        bool first = true;
        for (int i = 0; i < hist.Count; i++)
        {
            if (hist[i].Key < t0) continue;
            float xx = x + (w - 46f) * (hist[i].Key - t0) / Math.Max(1f, t1 - t0);
            float yy = y + h - 2f - (h - 4f) * (Ang.DeltaAngle(last, hist[i].Value) - lo) / span;
            if (!first) dc.DrawLine(PenOf(hit.Live ? Highlight : Fade(Highlight, 0.4f), 1f), new Point(px, py), new Point(xx, yy));
            px = xx; py = yy; first = false;
        }
        float dt = t1 - t0;
        if (dt >= 20f)
        {
            float drift = Ang.DeltaAngle(hist[0].Key < t0 ? Interp(hist, t0) : hist[0].Value, last) / dt * 60f;
            DrawRight(dc, (drift >= 0 ? "+" : "") + drift.ToString("0.0") + "/min", x + w, y + 1f, 10, Normal);
        }
    }

    private static float Interp(System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, float>> h, float t)
    {
        for (int i = 1; i < h.Count; i++)
            if (h[i].Key >= t) return h[i].Value;
        return h[h.Count - 1].Value;
    }

    protected override void Paint(DrawingContext dc, float w, float h)
    {
        dc.DrawRectangle(Back, null, new Rect(0, 0, w, h));
        SensorPicture pic = SensorPicture.Current;
        BoardWindow win = Win;
        if (win == null || !pic.InMission) return;
        UnitRow unit = win.Subject();
        float y = 4;
        if (win.SelContact != null && win.SelUnit != null && pic.Contacts.Contains(win.SelContact) && pic.Units.Contains(win.SelUnit))
        {
            var sb = new StringBuilder();
            Draw(dc, win.SelUnit.Name + " holds " + win.SelContact.Label + ":", 6, y, 12, Highlight);
            y += 16;
            bool any = false;
            foreach (Hit hit in win.SelContact.Hits)
            {
                if (hit.Unit != win.SelUnit) continue;
                any = true;
                sb.Clear();
                sb.Append(KindInfo.Names[(int)hit.Kind]).Append("  ").Append(hit.SensorName)
                  .Append("  brg ").Append(Fmt.Compass(hit.Bearing));
                if (hit.BearingError > 0.05f) sb.Append(" +-").Append(hit.BearingError.ToString("0"));
                if (!hit.BearingOnly && !float.IsNaN(hit.RangeM))
                    sb.Append("  rng ").Append((hit.RangeM / 1852f).ToString("0.0")).Append(" nm");
                else sb.Append("  bearing only");
                sb.Append("  fix ").Append(Fmt.Age(pic.Time - hit.FixTime)).Append(hit.Live ? " ago" : " ago (lost)");
                Draw(dc, sb.ToString(), 14, y, 11, hit.Live ? Normal : Fade(Normal, 0.6f));
                if (hit.BearingOnly) DrawHistory(dc, hit, w - 150f, y - 1f, 140f, 13f);
                y += 14;
            }
            if (!any) { Draw(dc, "no sensor of this unit holds it", 14, y, 11, Normal); y += 14; }
            y += 4;
        }
        if (unit == null) return;
        dc.DrawLine(PenOf(Lines, 1f), new Point(0, y), new Point(w, y));
        y += 3;
        Draw(dc, "Sensors of " + unit.Name + (unit.Emcon ? "   EMCON: search radars, active sonar and jamming are off" : ""), 6, y, 12, Highlight);
        y += 17;
        foreach (SensorRow s in unit.Sensors)
        {
            if (y > h - 12) { Draw(dc, "...", 6, y - 2, 11, Normal); break; }
            Brush b = s.Inoperable ? Red : s.On ? (s.Active ? Emphasis : Green) : Normal;
            Draw(dc, s.Letter, 6, y, 11, b);
            string state = s.Inoperable ? "damaged" : s.On ? "ON" : "off";
            Draw(dc, s.Name, 24, y, 11, s.On ? Highlight : Normal);
            Draw(dc, state, 200, y, 11, b);
            Draw(dc, "arc " + s.Arc, 250, y, 11, Normal);
            Draw(dc, s.Detail, 340, y, 11, Normal);
            y += 14;
        }
    }
}
