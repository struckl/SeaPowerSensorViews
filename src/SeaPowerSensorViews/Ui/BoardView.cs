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
    public BoardView()
    {
        var grid = new Grid { Margin = new Thickness(6) };
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
        Grid.SetRow(legend, 0);
        var board = new BoardSurface { Margin = new Thickness(0, 0, 0, 4) };
        Grid.SetRow(board, 1);
        var panel = new PanelSurface();
        Grid.SetRow(panel, 2);
        grid.Children.Add(legend);
        grid.Children.Add(board);
        grid.Children.Add(panel);
        Content = grid;
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
