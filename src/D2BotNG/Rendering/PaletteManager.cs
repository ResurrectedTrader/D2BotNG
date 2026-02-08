using System.Reflection;

namespace D2BotNG.Rendering;

/// <summary>
/// Manages Diablo 2 palettes and color shifting for item rendering
/// </summary>
public class PaletteManager
{
    private readonly Color[] _basePalette = new Color[256];
    private readonly byte[] _colorMap;

    public PaletteManager()
    {
        var palData = LoadEmbeddedResource("pal.dat");
        _colorMap = LoadEmbeddedResource("invgreybrown.dat");

        // Load base palette (768 bytes = 256 colors * 3 bytes RGB, stored as BGR)
        for (int i = 0; i < 256; i++)
        {
            byte b = palData[i * 3];
            byte g = palData[i * 3 + 1];
            byte r = palData[i * 3 + 2];
            _basePalette[i] = Color.FromArgb(255, r, g, b);
        }
    }

    /// <summary>
    /// Gets a color-shifted palette color
    /// </summary>
    /// <param name="index">Palette index (0-255)</param>
    /// <param name="shiftColor">Color shift value (-1 for no shift, 0+ for shift index)</param>
    public Color GetShiftedColor(int index, int shiftColor)
    {
        if (index < 0 || index >= 256) return Color.Transparent;

        if (shiftColor < 0)
        {
            return _basePalette[index];
        }

        // Apply color map shift
        int mapIndex = shiftColor * 256 + index;
        if (mapIndex < 0 || mapIndex >= _colorMap.Length)
        {
            return _basePalette[index];
        }

        int shiftedIndex = _colorMap[mapIndex];
        return _basePalette[shiftedIndex];
    }

    /// <summary>
    /// Creates a shifted palette array for a specific shift value
    /// </summary>
    public Color[] CreateShiftedPalette(int shiftColor)
    {
        var palette = new Color[256];
        for (int i = 0; i < 256; i++)
        {
            palette[i] = GetShiftedColor(i, shiftColor);
        }
        return palette;
    }

    private static byte[] LoadEmbeddedResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"D2BotNG.wwwroot.assets.rendering.{name}";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
