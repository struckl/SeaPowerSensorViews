using System;
using System.Collections.Generic;
using Noesis;

namespace SeaPowerSensorViews;

/// <summary>
/// The game's colours by their resource keys, for the windows and the map layers alike. A key is looked up from the element that
/// draws (the game's theme lives in the resources above it); when that fails the colour of the game's dark theme is used.
/// </summary>
internal static class GameColours
{
    public const string WindowBack = "Brush.Window.BackgroundDark", WindowHeader = "Brush.Window.Header", Border = "Brush.Border.Normal",
        Normal = "Brush.Foreground.Normal", Highlight = "Brush.Foreground.Highlight", Emphasis = "Brush.Foreground.Emphasis",
        Red = "Brush.Glyph.SeaPowerRed", Green = "Brush.Glyph.SeaPowerGreen", Blue = "Brush.Glyph.SeaPowerBlue",
        Button = "Brush.Button.Background",
        Hostile = "NTDS.HostileColour", Friend = "NTDS.FriendColour", Neutral = "NTDS.NeutralColour", Unknown = "NTDS.UnknownColour",
        MapRwr = "Map_RWR", MapUnitRange = "Map_UnitOnMapRange";

    private static readonly Dictionary<string, uint> Fallback = new Dictionary<string, uint>
    {
        [WindowBack] = 0xFF242629, [WindowHeader] = 0xFF1E1F22, [Border] = 0xFF3F4147,
        [Normal] = 0xFF949AA1, [Highlight] = 0xFFD7D7D7, [Emphasis] = 0xFFD5A876,
        [Red] = 0xFFC04A3C, [Green] = 0xFF63B970, [Blue] = 0xFF277DCD, [Button] = 0xFF393D40,
        [Hostile] = 0xFFFF0000, [Friend] = 0xFF277DCD, [Neutral] = 0xFF63B970, [Unknown] = 0xFFFFC000,
        [MapRwr] = 0xFFFF0000, [MapUnitRange] = 0xFF3B7FE0,
    };

    private static readonly Dictionary<uint, SolidColorBrush> Solid = new Dictionary<uint, SolidColorBrush>();

    public static Brush Find(FrameworkElement at, string key)
    {
        try
        {
            if (at.TryFindResource(key) is Brush b) return b;
        }
        catch (Exception) { /* the element is not in a tree with the game's resources yet: the fallback below */ }
        return Of(Fallback[key]);
    }

    public static Color Argb(uint v) => Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);

    /// <summary>One shared brush per colour (the windows and map paths ask for the same few faded colours again and again).</summary>
    public static SolidColorBrush Of(uint argb)
    {
        if (!Solid.TryGetValue(argb, out SolidColorBrush b))
            Solid[argb] = b = new SolidColorBrush(Argb(argb));
        return b;
    }

    /// <summary>The brush with its opacity multiplied by <paramref name="alpha"/> (0..1), as a brush of its own colour.</summary>
    public static Brush Faded(Brush brush, float alpha)
    {
        Color c = (brush as SolidColorBrush)?.Color ?? Color.FromArgb(255, 200, 200, 200);
        uint a = (uint)Math.Max(0, Math.Min(255, (int)(c.A * alpha)));
        return Of((a << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B);
    }
}
