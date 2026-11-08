using System;
using System.Collections.Generic;
using Noesis;

#if FLATMAP
namespace SeaPowerSensorViews.Ui;
#else
namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: the two builds must not share these names
#endif

/// <summary>
/// A control that draws itself with Noesis' DrawingContext, like the game's own map controls (MapEllipse, Ruler, ...).
/// Colours come from the game's theme: the brushes are looked up by the game's resource keys (Brush.Window.Background,
/// Brush.Foreground.Normal, ...) once the control is in the window; the literal colours below are only the fallback with
/// the same values as the game's dark theme.
/// </summary>
public abstract class Surface : UserControl
{
    // ---- the game's theme colours (SeaPower resource keys) -------------------------------------------------------
    internal Brush Back, Header, Lines, Normal, Highlight, Emphasis, Red, Green, Blue, Button;
    internal Brush Hostile, Friendly, Neutral, Unknown;     // NTDS.* brushes
    private FontFamily family;
    private readonly Dictionary<uint, SolidColorBrush> alphaBrushes = new Dictionary<uint, SolidColorBrush>();
    private readonly Dictionary<(Brush, int), Pen> pens = new Dictionary<(Brush, int), Pen>();
    private readonly Dictionary<(string, int, Brush), FormattedText> texts = new Dictionary<(string, int, Brush), FormattedText>();
    private bool themed;

    protected Surface()
    {
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));      // hit-testable, invisible
        Loaded += (s, e) => { Theme(); Registry.Add(this); InvalidateVisual(); };
        Unloaded += (s, e) => Registry.Remove(this);
    }

    /// <summary>Called by the host after a new snapshot, or every few frames when <see cref="Animated"/>.</summary>
    internal virtual void Refresh() => InvalidateVisual();
    internal virtual bool Animated => false;
    /// <summary>Called 25 times a second for an <see cref="Animated"/> surface; the default redraws it.</summary>
    internal virtual void Animate() => InvalidateVisual();

    private Brush Find(string key, uint fallbackArgb)
    {
        try
        {
            if (TryFindResource(key) is Brush b) return b;
        }
        catch (Exception) { /* use the fallback */ }
        return new SolidColorBrush(Argb(fallbackArgb));
    }

    internal static Color Argb(uint v) => Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);

    private void Theme()
    {
        if (themed) return;
        themed = true;
        Back = Find("Brush.Window.BackgroundDark", 0xFF242629);
        Header = Find("Brush.Window.Header", 0xFF1E1F22);
        Lines = Find("Brush.Border.Normal", 0xFF3F4147);
        Normal = Find("Brush.Foreground.Normal", 0xFF949AA1);
        Highlight = Find("Brush.Foreground.Highlight", 0xFFD7D7D7);
        Emphasis = Find("Brush.Foreground.Emphasis", 0xFFD5A876);
        Red = Find("Brush.Glyph.SeaPowerRed", 0xFFC04A3C);
        Green = Find("Brush.Glyph.SeaPowerGreen", 0xFF63B970);
        Blue = Find("Brush.Glyph.SeaPowerBlue", 0xFF277DCD);
        Button = Find("Brush.Button.Background", 0xFF393D40);
        Hostile = Find("NTDS.HostileColour", 0xFFFF0000);
        Friendly = Find("NTDS.FriendColour", 0xFF277DCD);
        Neutral = Find("NTDS.NeutralColour", 0xFF63B970);
        Unknown = Find("NTDS.UnknownColour", 0xFFFFC000);
        try { family = TryFindResource("Font.Family.Default") as FontFamily; } catch (Exception) { family = null; }
        if (family == null) family = FontFamily;
    }

    /// <summary>The brush with another opacity (cached per colour and alpha step).</summary>
    internal Brush Fade(Brush brush, float alpha)
    {
        Color c = (brush as SolidColorBrush)?.Color ?? Color.FromArgb(255, 200, 200, 200);
        byte a = (byte)Math.Max(0, Math.Min(255, (int)(c.A * alpha)));
        uint key = ((uint)a << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
        if (!alphaBrushes.TryGetValue(key, out SolidColorBrush result))
            alphaBrushes[key] = result = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        return result;
    }

    internal Pen PenOf(Brush brush, float thickness)
    {
        var key = (brush, (int)(thickness * 4f));
        if (!pens.TryGetValue(key, out Pen pen))
            pens[key] = pen = new Pen { Brush = brush, Thickness = thickness };
        return pen;
    }

    internal FormattedText Text(string s, float size, Brush brush)
    {
        var key = (s, (int)(size * 2f), brush);
        if (!texts.TryGetValue(key, out FormattedText t))
        {
            if (texts.Count > 800) texts.Clear();
            texts[key] = t = new FormattedText(s, family ?? FontFamily, size, brush, FlowDirection.LeftToRight);
        }
        return t;
    }

    /// <summary>Draws text with its top-left at (x, y); returns its width.</summary>
    internal float Draw(DrawingContext dc, string s, float x, float y, float size, Brush brush)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        FormattedText t = Text(s, size, brush);
        dc.DrawText(t, new Point(x, y));
        return t.Bounds.Width;
    }

    internal float DrawRight(DrawingContext dc, string s, float right, float y, float size, Brush brush)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        FormattedText t = Text(s, size, brush);
        float w = t.Bounds.Width;
        dc.DrawText(t, new Point(right - w, y));
        return w;
    }

    internal float DrawCentered(DrawingContext dc, string s, float cx, float y, float size, Brush brush)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        FormattedText t = Text(s, size, brush);
        float w = t.Bounds.Width;
        dc.DrawText(t, new Point(cx - w / 2f, y));
        return w;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!themed) Theme();
        long t = Perf.Start();
        try { Paint(dc, ActualWidth, ActualHeight); }
        catch (Exception ex) { Report(ex); }
        Perf.Stop(Perf.Part.WindowDraw, t);
    }

    private static int errors;
    private static void Report(Exception ex)
    {
        if (errors++ < 5) ModLog.Error("draw: " + ex);
    }

    protected abstract void Paint(DrawingContext dc, float w, float h);

    // ---- geometry helpers ------------------------------------------------------------------------------------------
    internal static Point Polar(float cx, float cy, float radius, float compassDeg)
    {
        float a = compassDeg * (MathF.PI / 180f);
        return new Point(cx + radius * MathF.Sin(a), cy - radius * MathF.Cos(a));
    }

    /// <summary>A ring sector (or full disc) between two radii and two compass bearings, clockwise from a to b.</summary>
    internal static Geometry Sector(float cx, float cy, float r0, float r1, float fromDeg, float toDeg)
    {
        float span = toDeg - fromDeg;
        while (span < 0f) span += 360f;
        if (span >= 359.9f)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(new Point(cx, cy), r1, r1));
            if (r0 > 0.5f) ring.Children.Add(new EllipseGeometry(new Point(cx, cy), r0, r0));
            return ring;
        }
        var g = new StreamGeometry();
        using (StreamGeometryContext c = g.Open())
        {
            bool large = span > 180f;
            c.BeginFigure(Polar(cx, cy, r1, fromDeg), true, true);
            c.ArcTo(Polar(cx, cy, r1, toDeg), new Size(r1, r1), 0, large, SweepDirection.Clockwise, true, false);
            if (r0 > 0.5f)
            {
                c.LineTo(Polar(cx, cy, r0, toDeg), true, false);
                c.ArcTo(Polar(cx, cy, r0, fromDeg), new Size(r0, r0), 0, large, SweepDirection.Counterclockwise, true, false);
            }
            else c.LineTo(new Point(cx, cy), true, false);
        }
        return g;
    }
}

/// <summary>The surfaces that are on screen, so the host can refresh them.</summary>
internal static class Registry
{
    public static readonly List<Surface> Surfaces = new List<Surface>();
    public static void Add(Surface s) { if (!Surfaces.Contains(s)) Surfaces.Add(s); }
    public static void Remove(Surface s) => Surfaces.Remove(s);
    public static void RefreshAll() { for (int i = 0; i < Surfaces.Count; i++) Surfaces[i].InvalidateVisual(); }
}
