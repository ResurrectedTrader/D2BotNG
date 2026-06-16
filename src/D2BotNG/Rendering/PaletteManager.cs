using D2BotNG.Utilities;

namespace D2BotNG.Rendering;

/// <summary>
/// Manages Diablo 2 palettes and color shifting for item rendering.
///
/// Item recoloring picks the shift table PER ITEM by the base item's InvTrans group
/// (the game's PALETTE_GetItemPalette): group -> palette file, and a tint is only
/// applied for groups {1,2,5,6,7,8} with a color index in [0,20]. Groups 0/3/4/&gt;=9
/// (and a missing/absent group) produce no shift.
/// </summary>
public class PaletteManager
{
    private readonly Color[] _basePalette = new Color[256];
    private readonly Dictionary<int, byte[]> _groupMaps = new();

    // InvTrans group -> shift-table resource (under wwwroot/assets/rendering/). Only these
    // groups are inventory-tintable, and the presence of a loaded table is what gates the
    // shift — so no separate "valid groups" set is needed. Groups 3 (gold) and 4 (brown)
    // aren't inventory-tintable and aren't loaded.
    private static readonly Dictionary<int, string> GroupFiles = new()
    {
        [1] = "grey",
        [2] = "grey2",
        [5] = "greybrown",
        [6] = "invgrey",
        [7] = "invgrey2",
        [8] = "invgreybrown"
    };

    public PaletteManager()
    {
        var palData = LoadEmbeddedResource("pal.dat");

        // Load base palette (768 bytes = 256 colors * 3 bytes RGB, stored as BGR)
        for (int i = 0; i < 256; i++)
        {
            byte b = palData[i * 3];
            byte g = palData[i * 3 + 1];
            byte r = palData[i * 3 + 2];
            _basePalette[i] = Color.FromArgb(255, r, g, b);
        }

        foreach (var (group, name) in GroupFiles)
        {
            _groupMaps[group] = LoadEmbeddedResource($"{name}.dat");
        }
    }

    /// <summary>
    /// Gets a color-shifted palette color
    /// </summary>
    /// <param name="index">Palette index (0-255)</param>
    /// <param name="shiftColor">Color shift value (itemColor; -1 for no shift)</param>
    /// <param name="invTrans">Base item's InvTrans group (selects the shift table)</param>
    public Color GetShiftedColor(int index, int shiftColor, int invTrans)
    {
        if (index < 0 || index >= 256) return Color.Transparent;

        // An absent group (0/3/4/>=9, or no invTrans) has no loaded table -> no shift, as
        // does a negative color. The bounds check below also enforces shiftColor in [0,20].
        if (shiftColor < 0 || !_groupMaps.TryGetValue(invTrans, out var colorMap))
        {
            return _basePalette[index];
        }

        int mapIndex = shiftColor * 256 + index;
        if (mapIndex < 0 || mapIndex >= colorMap.Length)
        {
            return _basePalette[index];
        }

        return _basePalette[colorMap[mapIndex]];
    }

    /// <summary>
    /// Creates a shifted palette array for a specific shift value + InvTrans group
    /// </summary>
    public Color[] CreateShiftedPalette(int shiftColor, int invTrans)
    {
        var palette = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            palette[i] = GetShiftedColor(i, shiftColor, invTrans);
        }
        return palette;
    }

    private static byte[] LoadEmbeddedResource(string name)
    {
        return EmbeddedResourceLoader.LoadBytes($"D2BotNG.wwwroot.assets.rendering.{name}");
    }
}
