using System;
using System.Collections.Generic;
using Noesis;
using SeaPowerSensorViews.Data;

namespace SeaPowerSensorViews.Globe.Ui;     // Noesis finds XAML types by full name across all loaded DLLs: a leftover DLL of an older release must not share these names

/// <summary>
/// A control that draws itself with Noesis' DrawingContext, like the game's own map controls (MapEllipse, Ruler, ...).
/// Colours come from the game's theme (<see cref="GameColours"/>), looked up once the control is in the window.
/// </summary>
public abstract class Surface : UserControl
{
    internal Brush Back, Header, Lines, Normal, Highlight, Emphasis, Red, Green, Blue, Button;
    internal Brush Hostile, Friendly, Neutral, Unknown;     // NTDS.* brushes
    private FontFamily family;
    private readonly Dictionary<(Brush, int), Pen> pens = new Dictionary<(Brush, int), Pen>();
    private readonly Dictionary<(string, int, Brush), FormattedText> texts = new Dictionary<(string, int, Brush), FormattedText>();
    private bool themed;
    private Brush ownShip;

    /// <summary>Formatted texts kept for reuse; the cache starts again when it holds more (a few windows show far fewer).</summary>
    private const int TextCacheSize = 800;

    protected Surface()
    {
        Background = GameColours.Of(0x00000000);      // hit-testable, invisible
        Loaded += (s, e) => { Theme(); Registry.Add(this); InvalidateVisual(); };
        Unloaded += (s, e) => Registry.Remove(this);
    }

    internal virtual bool Animated => false;
    /// <summary>Called 25 times a second for an <see cref="Animated"/> surface; the default redraws it.</summary>
    internal virtual void Animate() => InvalidateVisual();

    /// <summary>A subclass that wants the phosphor-screen palette of the period radar and sonar displays instead of the game's theme.</summary>
    internal virtual bool Retro => false;

    private void Theme()
    {
        if (themed) return;
        themed = true;
        Back = GameColours.Find(this, GameColours.WindowBack);
        Header = GameColours.Find(this, GameColours.WindowHeader);
        Lines = GameColours.Find(this, GameColours.Border);
        Normal = GameColours.Find(this, GameColours.Normal);
        Highlight = GameColours.Find(this, GameColours.Highlight);
        Emphasis = GameColours.Find(this, GameColours.Emphasis);
        Red = GameColours.Find(this, GameColours.Red);
        Green = GameColours.Find(this, GameColours.Green);
        Blue = GameColours.Find(this, GameColours.Blue);
        Button = GameColours.Find(this, GameColours.Button);
        Hostile = GameColours.Find(this, GameColours.Hostile);
        Friendly = GameColours.Find(this, GameColours.Friend);
        Neutral = GameColours.Find(this, GameColours.Neutral);
        Unknown = GameColours.Find(this, GameColours.Unknown);
        try { family = TryFindResource("Font.Family.Default") as FontFamily; }
        catch (Exception) { family = null; }   // not in a tree with the game's resources: the control's own font
        if (family == null) family = FontFamily;
        if (Retro) Phosphor();
    }

    /// <summary>A green phosphor tube: near-black ground, dim rings, bright traces; hostile and locks stay the brightest.</summary>
    private void Phosphor()
    {
        Back = GameColours.Of(0xFF020E06); Header = GameColours.Of(0xFF03180B); Lines = GameColours.Of(0xFF14502A);
        Normal = GameColours.Of(0xFF3DA857); Highlight = GameColours.Of(0xFF8CFFA8); Emphasis = GameColours.Of(0xFFD6FF8C);
        Red = GameColours.Of(0xFFFFB347); Green = GameColours.Of(0xFF5DFF7D); Blue = GameColours.Of(0xFF4FD0C0);
        Button = GameColours.Of(0xFF0A2A14);
        Hostile = GameColours.Of(0xFFE8FF6A); Friendly = GameColours.Of(0xFF6AFFB0); Neutral = GameColours.Of(0xFF3DA857);
        Unknown = GameColours.Of(0xFFB8FF9A);
    }

    /// <summary>The brush with another opacity.</summary>
    internal static Brush Fade(Brush brush, float alpha) => GameColours.Faded(brush, alpha);

    internal Brush RelationBrush(Relation relation) => relation switch
    {
        Relation.Friendly => Friendly,
        Relation.Neutral => Neutral,
        Relation.Hostile => Hostile,
        _ => Unknown,
    };

    /// <summary>The faint pen of scale lines and grids.</summary>
    internal Pen GridPen => PenOf(Fade(Lines, 0.8f), 1f);

    /// <summary>An own ship on a scope: a diamond and its name. The game's friendly blue is too dark on the dark ground, so the same hue 60 % towards white.</summary>
    internal void DrawOwnShip(DrawingContext dc, Point p, string name, float alpha)
    {
        if (ownShip == null)
        {
            Color c = (Friendly as SolidColorBrush)?.Color ?? Color.FromArgb(255, 39, 125, 205);
            byte L(byte v) => (byte)(v + (255 - v) * 0.6f);
            ownShip = new SolidColorBrush(Color.FromArgb(255, L(c.R), L(c.G), L(c.B)));
        }
        Brush b = Fade(ownShip, alpha);
        var d = new Point[] { new Point(p.X, p.Y - 4.5f), new Point(p.X + 4.5f, p.Y), new Point(p.X, p.Y + 4.5f), new Point(p.X - 4.5f, p.Y), new Point(p.X, p.Y - 4.5f) };
        Pen pen = PenOf(b, 1.6f);
        for (int i = 0; i < 4; i++) dc.DrawLine(pen, d[i], d[i + 1]);
        Draw(dc, name, p.X + 7, p.Y - 6, 10, Fade(ownShip, Math.Max(0.3f, alpha)));
    }

    /// <summary>A radar contact on a scope: a square in its side's colour, a ring and "LOCK" when a fire-control radar holds it.</summary>
    internal void DrawContact(DrawingContext dc, Point p, ContactRow cr, bool locked, float alpha, float labelAlpha)
    {
        dc.DrawRectangle(Fade(RelationBrush(cr.Relation), alpha), null, new Rect(p.X - 3.5f, p.Y - 3.5f, 7, 7));
        if (locked) dc.DrawEllipse(null, PenOf(Hostile, 1.5f), p, 9, 9);
        Draw(dc, cr.Label + (locked ? " LOCK" : ""), p.X + 7, p.Y - 6, 10, Fade(locked ? Hostile : Highlight, labelAlpha));
    }

    /// <summary>A small text on a dark box, for the measuring cursors.</summary>
    internal void DrawTag(DrawingContext dc, string text, float x, float y, float width)
    {
        dc.DrawRectangle(Fade(Back, 0.85f), null, new Rect(x - 2, y - 1, width, 15));
        Draw(dc, text, x, y, 10, Highlight);
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
            if (texts.Count > TextCacheSize) texts.Clear();
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
        if (errors++ < 5) Rig.Log.Error("draw: " + ex);
    }

    protected abstract void Paint(DrawingContext dc, float w, float h);

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

/// <summary>A radar or sonar screen: the period look when the setting is on.</summary>
public abstract class TubeSurface : Surface
{
    internal override bool Retro => Settings.RetroLook.Value;
}

/// <summary>A measuring cursor drawn on its own layer over a screen, so a mouse move redraws a few lines, not the whole picture.</summary>
public abstract class CursorSurface : TubeSurface
{
    protected Point Pointer = new Point(-1, -1);

    protected CursorSurface() => Background = null;

    internal void MoveTo(Point p)
    {
        if (p.X < 0 && Pointer.X < 0) return;
        Pointer = p;
        InvalidateVisual();
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
