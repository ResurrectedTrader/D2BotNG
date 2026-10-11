namespace D2BotNG.Rendering;

/// <summary>Which game's text palette a piece of text is drawn in.</summary>
public enum TextPalette
{
    /// <summary>Diablo II 1.14d: the classic thirteen colours.</summary>
    Lod,

    /// <summary>Diablo II: Resurrected, as its HD renderer draws: its own blue, and 13..38.</summary>
    D2R
}

/// <summary>
/// D2 text color definitions matching in-game color codes
/// </summary>
public static class D2Colors
{
    public static readonly Color White = Color.FromArgb(255, 255, 255);
    public static readonly Color Red = Color.FromArgb(255, 77, 77);
    public static readonly Color Green = Color.FromArgb(0, 255, 0);
    public static readonly Color Blue = Color.FromArgb(105, 105, 255);
    public static readonly Color Gold = Color.FromArgb(199, 179, 119);
    public static readonly Color Gray = Color.FromArgb(105, 105, 105);
    public static readonly Color Black = Color.FromArgb(0, 0, 0);
    public static readonly Color Tan = Color.FromArgb(208, 194, 125);
    public static readonly Color Orange = Color.FromArgb(255, 168, 0);
    public static readonly Color Yellow = Color.FromArgb(255, 255, 100);
    public static readonly Color DarkGreen = Color.FromArgb(0, 128, 0);
    public static readonly Color Purple = Color.FromArgb(174, 0, 255);
    public static readonly Color BrightGreen = Color.FromArgb(0, 200, 0);

    /// <summary>The classic font's thirteen colours, by index.</summary>
    private static readonly Color[] Classic =
    [
        White,       // 0
        Red,         // 1
        Green,       // 2
        Blue,        // 3
        Gold,        // 4
        Gray,        // 5
        Black,       // 6
        Tan,         // 7
        Orange,      // 8
        Yellow,      // 9
        DarkGreen,   // 10 ':'
        Purple,      // 11 ';'
        BrightGreen  // 12 '<'
    ];

    /// <summary>
    /// D2R's colours past the classic thirteen, as its HD renderer draws them — read from a running
    /// d2r.exe (build 91923). The code character is <c>'0' + index</c> in both games. 13..29 and
    /// 30..38 come from two hash maps <c>D2RGFX_D2_Text</c> (0x140668260) resolves through; each
    /// entry's RGBA is filled from the HD profile at startup. The frontend's
    /// <c>lib/rendering/colors.ts</c> holds the same values and the addresses they were read from.
    /// </summary>
    private static readonly Color[] D2RExtended =
    [
        Color.FromArgb(240, 240, 240), // 13 '=' normal
        Color.FromArgb(199, 179, 119), // 14 '>'
        Color.FromArgb(255, 255, 100), // 15 '?' rare
        Color.FromArgb(255, 168, 0),   // 16 '@' crafted
        Color.FromArgb(0, 128, 0),     // 17 'A' tempered
        Color.FromArgb(110, 110, 255), // 18 'B' magic
        Color.FromArgb(0, 252, 0),     // 19 'C' set
        Color.FromArgb(199, 179, 119), // 20 'D' unique
        Color.FromArgb(240, 240, 240), // 21 'E'
        Color.FromArgb(240, 240, 240), // 22 'F' rejuvenation potion
        Color.FromArgb(240, 240, 240), // 23 'G' stamina potion
        Color.FromArgb(240, 240, 240), // 24 'H' gold
        Color.FromArgb(99, 99, 99),    // 25 'I' ethereal
        Color.FromArgb(255, 168, 0),   // 26 'J' rune name
        Color.FromArgb(99, 99, 99),    // 27 'K' socketed
        Color.FromArgb(255, 168, 0),   // 28 'L' event item name
        Color.FromArgb(154, 72, 252),  // 29 'M' desecrated
        Color.FromArgb(238, 214, 141), // 30 'N'
        Color.FromArgb(6, 165, 221),   // 31 'O'
        Color.FromArgb(255, 128, 255), // 32 'P'
        Color.FromArgb(170, 170, 255), // 33 'Q'
        Color.FromArgb(0, 255, 0),     // 34 'R'
        Color.FromArgb(255, 255, 118), // 35 'S'
        Color.FromArgb(208, 62, 62),   // 36 'T'
        Color.FromArgb(130, 197, 255), // 37 'U' roll ranges
        Color.FromArgb(255, 0, 0)      // 38 'V'
    ];

    /// <summary>
    /// 1.14d's palette. It has nothing past 12, but a stray higher code reads in D2R's colour rather
    /// than as nothing.
    /// </summary>
    public static readonly Color[] TextColors = [.. Classic, .. D2RExtended];

    /// <summary>
    /// D2R's palette: the classic thirteen except blue, which its HD renderer draws as 110,110,255
    /// (its table at PanelManager.GlobalData + 0x188, read live), then 13..38.
    /// </summary>
    public static readonly Color[] ResurrectedTextColors =
        [.. Classic[..3], Color.FromArgb(110, 110, 255), .. Classic[4..], .. D2RExtended];

    /// <summary>
    /// Gets color for a D2 color code character in a game's palette; one naming no colour reads as
    /// white.
    /// </summary>
    public static Color GetTextColor(char code, TextPalette palette = TextPalette.Lod)
    {
        var colors = palette == TextPalette.D2R ? ResurrectedTextColors : TextColors;
        var index = code - '0';
        return index >= 0 && index < colors.Length ? colors[index] : White;
    }

    /// <summary>Whether a code character names a colour in either game.</summary>
    public static bool IsColorCode(char code) => code - '0' is >= 0 and < 39;
}
