namespace D2BotNG.Rendering;

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

    /// <summary>
    /// Maps color code characters to colors (ÿc0-ÿc;)
    /// </summary>
    public static readonly Color[] TextColors =
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
        DarkGreen,   // :
        Purple,      // ;
        BrightGreen  // <
    ];

    /// <summary>
    /// Gets color for a D2 color code character
    /// </summary>
    // ReSharper disable once UnusedMember.Global — rendering utility for text color rendering
    public static Color GetTextColor(char code)
    {
        int index = code switch
        {
            ':' => 10,
            ';' => 11,
            '<' => 12,
            >= '0' and <= '9' => code - '0',
            _ => 0
        };

        return index < TextColors.Length ? TextColors[index] : White;
    }
}
