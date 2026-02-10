using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using D2BotNG.Core.Protos;
using D2BotNG.Utilities;

namespace D2BotNG.Rendering;

/// <summary>
/// Renders Diablo 2 item images from DC6 sprites
/// </summary>
public class ItemRenderer
{
    private const int GridCellSize = 30;
    private const int SingleWidthThreshold = 37;
    private const int TwoRowHeightThreshold = 65;
    private const int ThreeRowHeightThreshold = 95;
    private const int TextLineHeight = 16;
    private const int SocketSpacing = 14;
    private const int TooltipHeaderHeight = 14;
    private const int TooltipTextPadding = 14;
    private const int TooltipMinWidth = 100;
    private const int TooltipBottomPadding = 6;

    private readonly ILogger<ItemRenderer> _logger;
    private readonly PaletteManager _paletteManager;
    private readonly ConcurrentDictionary<string, byte[]> _dc6Cache = new();
    private readonly byte[] _fallbackDc6;
    private readonly PrivateFontCollection _fontCollection = new();
    private FontFamily? _exocetFontFamily;

    /// <summary>
    /// Maps ItemFont enum to font family names
    /// </summary>
    private static readonly Dictionary<ItemFont, string> FontFamilyMap = new()
    {
        { ItemFont.Exocet, "Exocet Blizzard OT Light" },
        { ItemFont.Consolas, "Consolas" },
        { ItemFont.System, "Segoe UI" }
    };

    public ItemRenderer(ILogger<ItemRenderer> logger, PaletteManager paletteManager)
    {
        _logger = logger;
        _paletteManager = paletteManager;
        _fallbackDc6 = LoadDc6Resource("box");
        LoadExocetFont();
    }


    /// <summary>
    /// Loads the Exocet font from embedded resources
    /// </summary>
    private void LoadExocetFont()
    {
        try
        {
            using var stream = EmbeddedResourceLoader.LoadStream("D2BotNG.Resources.exocet-blizzard-light.ttf");
            if (stream == null) return;

            var fontData = new byte[stream.Length];
            stream.ReadExactly(fontData, 0, fontData.Length);

            var handle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            try
            {
                _fontCollection.AddMemoryFont(handle.AddrOfPinnedObject(), fontData.Length);
                _exocetFontFamily = _fontCollection.Families.FirstOrDefault();
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load Exocet font, falling back to system fonts");
        }
    }

    /// <summary>
    /// Creates a font, using the embedded Exocet font if available
    /// </summary>
    private Font CreateFont(string fontFamily, float size, FontStyle style = FontStyle.Regular)
    {
        if (_exocetFontFamily != null && fontFamily.Contains("Exocet"))
        {
            return new Font(_exocetFontFamily, size, style);
        }

        return new Font(fontFamily, size, style);
    }

    /// <summary>
    /// Gets the font family name for the given ItemFont enum value
    /// </summary>
    private static string GetFontFamily(ItemFont font)
    {
        return FontFamilyMap.TryGetValue(font, out var family) ? family : "Consolas";
    }

    /// <summary>
    /// Renders an item to a PNG image
    /// </summary>
    public byte[] RenderItem(Item item)
    {
        using var bitmap = RenderItemBitmap(item);
        return BitmapToPng(bitmap);
    }

    /// <summary>
    /// Renders an item with its sockets to a PNG image
    /// </summary>
    public byte[] RenderItemWithSockets(Item item)
    {
        // First render the base item to get dimensions
        var frame = GetItemFrame(item.Code);
        var gridSize = CalculateGridSize(frame.Width, frame.Height);

        int width = gridSize.X * GridCellSize - 1;
        int height = gridSize.Y * GridCellSize - 1;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        // Clear to transparent
        graphics.Clear(Color.Transparent);

        // Render base item centered
        using var itemBitmap = RenderItemBitmap(item, Color.Transparent);
        int itemX = (width - itemBitmap.Width) / 2;
        int itemY = (height - itemBitmap.Height) / 2;
        graphics.DrawImage(itemBitmap, itemX, itemY);

        // Render sockets if present
        if (item.Sockets.Count > 0)
        {
            RenderSockets(graphics, item, gridSize, itemX);
        }

        return BitmapToPng(bitmap);
    }

    /// <summary>
    /// Renders a full item tooltip with background and text
    /// </summary>
    public byte[] RenderItemTooltip(Item item, ItemFont itemFont = ItemFont.Exocet, bool showHeader = false)
    {
        // Get item frame for dimensions
        var frame = GetItemFrame(item.Code);
        var gridSize = CalculateGridSize(frame.Width, frame.Height);

        // Parse description - remove $ suffix and normalize color codes
        string description = NormalizeColorCodes(item.Description.Split('$')[0]);
        string fontFamily = GetFontFamily(itemFont);
        string[] lines = StripColorCodes(description).Replace("\\n", "\n").Split('\n');

        // Calculate header dimensions
        int headerHeight = 0;
        bool hasHeader = showHeader && !string.IsNullOrEmpty(item.Header);
        if (hasHeader)
        {
            headerHeight = TooltipHeaderHeight;
        }

        // Calculate dimensions
        int textWidth = CalculateTextWidth(lines, fontFamily);
        int textHeight = lines.Length * TextLineHeight;
        int imageTop = GetImageTop(gridSize.Y);

        // Include header width in calculation if present
        int headerWidth = hasHeader ? MeasureHeaderWidth(item.Header) : 0;
        int totalWidth = Math.Max(Math.Max(textWidth + TooltipTextPadding, headerWidth + 8), TooltipMinWidth);
        int totalHeight = textHeight + imageTop + TooltipBottomPadding + headerHeight;

        using var bitmap = new Bitmap(totalWidth, totalHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        // Graphics settings (match reference implementation)
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Black background
        graphics.Clear(Color.Black);

        // Draw background image (offset by header height)
        using var bgnd = LoadBackgroundBitmap(gridSize.Y);
        if (bgnd != null)
        {
            int bgndX = totalWidth / 2 - GetImageLeft(gridSize.X);
            graphics.DrawImage(bgnd, bgndX, -10 + headerHeight);
        }

        // Render item image (offset by header height)
        using var itemBitmap = RenderItemBitmap(item);
        int itemX = (totalWidth - itemBitmap.Width) / 2;
        graphics.DrawImage(itemBitmap, itemX, 5 + headerHeight);

        // Render sockets (offset by header height)
        if (item.Sockets.Count > 0)
        {
            RenderSocketsOnTooltip(graphics, item, gridSize, itemX, headerHeight);
        }

        // Render text (offset by header height)
        RenderDescription(graphics, description, totalWidth, imageTop + headerHeight, fontFamily);

        // Render header LAST so it overlays on top of everything (matches reference implementation)
        if (hasHeader)
        {
            using var headerFont = new Font("Arial", 8, FontStyle.Regular, GraphicsUnit.Point);
            using var headerBrush = new SolidBrush(Color.AliceBlue);
            graphics.DrawString(item.Header, headerFont, headerBrush, 2, 1);
        }

        return BitmapToPng(bitmap);
    }

    /// <summary>
    /// Measures the width of the header text
    /// </summary>
    private static int MeasureHeaderWidth(string header)
    {
        using var bmp = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bmp);
        using var font = new Font("Arial", 8, FontStyle.Regular, GraphicsUnit.Point);
        return (int)graphics.MeasureString(header, font).Width;
    }

    private Bitmap RenderItemBitmap(Item item, Color? background = null)
    {
        var frame = GetItemFrame(item.Code);
        int shiftColor = (int)item.ItemColor;
        bool isEthereal = item.Description.Contains("Ethereal") || item.Description.Contains(":eth");
        bool isSocket = item.Code == "gemsocket";

        int alpha = isEthereal ? 127 : (isSocket ? 100 : 255);
        var bgColor = background ?? Color.Blue;
        bool transparent = bgColor == Color.Transparent;

        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var palette = _paletteManager.CreateShiftedPalette(shiftColor);

        var bitmapData = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            int[] pixels = new int[frame.Width * frame.Height];

            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    int paletteIndex = frame.Pixels[x, y];
                    int pixelIndex = x + y * frame.Width;

                    if (paletteIndex == 0)
                    {
                        if (transparent)
                        {
                            pixels[pixelIndex] = 0; // Fully transparent
                        }
                        else
                        {
                            // Semi-transparent background
                            int bgAlpha = item.Description == "soc" ? 0 : 20;
                            pixels[pixelIndex] = Color.FromArgb(bgAlpha, bgColor).ToArgb();
                        }
                    }
                    else
                    {
                        var color = palette[paletteIndex];
                        pixels[pixelIndex] = Color.FromArgb(alpha, color).ToArgb();
                    }
                }
            }

            Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    private Dc6Frame GetItemFrame(string code)
    {
        var dc6Data = GetDc6Data(code);
        return Dc6Decoder.DecodeFirstFrame(dc6Data);
    }

    private byte[] GetDc6Data(string code)
    {
        return _dc6Cache.GetOrAdd(code.ToLowerInvariant(), key =>
        {
            try
            {
                return LoadDc6Resource(key);
            }
            catch (Exception)
            {
                _logger.LogDebug("Failed to load DC6 for code {Code}", code);
                return _fallbackDc6;
            }
        });
    }

    private static byte[] LoadDc6Resource(string code)
    {
        return EmbeddedResourceLoader.LoadBytes($"D2BotNG.wwwroot.assets.rendering.dc6.{code}.dc6");
    }

    private static (int X, int Y) CalculateGridSize(int width, int height)
    {
        int x = width < SingleWidthThreshold ? 1 : 2;
        int y = height < GridCellSize ? 1 : (height < TwoRowHeightThreshold ? 2 : (height < ThreeRowHeightThreshold ? 3 : 4));
        return (x, y);
    }

    private static int GetImageTop(int gridY) => gridY switch
    {
        1 => 32,
        2 => 61,
        3 => 90,
        _ => 119
    };

    private static int GetImageLeft(int gridX) => gridX switch
    {
        1 => 212,
        _ => 226
    };

    private void RenderSockets(Graphics graphics, Item item, (int X, int Y) gridSize, int baseX)
    {
        const int offsetY = -1;

        int x1 = baseX;
        int y1 = 2;
        int y2 = y1 + SocketSpacing * 2 + 1;
        int y3 = y2 + SocketSpacing * 2 + 1;
        int y4 = y3 + SocketSpacing * 2 + 1;

        var positions = CalculateSocketPositions(item.Sockets.Count, gridSize, x1, SocketSpacing, offsetY, y1, y2, y3, y4);
        DrawSockets(graphics, item, positions);
    }

    private void RenderSocketsOnTooltip(Graphics graphics, Item item, (int X, int Y) gridSize, int itemX,
        int headerOffset = 0)
    {
        const int offsetY = -1;

        int x1 = itemX + 1;
        int y1 = 5 + headerOffset;
        int y2 = 34 + headerOffset;
        int y3 = 63 + headerOffset;
        int y4 = 92 + headerOffset;

        var positions = CalculateSocketPositions(item.Sockets.Count, gridSize, x1, SocketSpacing, offsetY, y1, y2, y3, y4);
        DrawSockets(graphics, item, positions);
    }

    private void DrawSockets(Graphics graphics, Item item, List<Point> positions)
    {
        for (int i = 0; i < item.Sockets.Count && i < positions.Count; i++)
        {
            var socketItem = item.Sockets[i];
            using var socketBitmap = RenderItemBitmap(new Item
            {
                Code = socketItem.Code,
                ItemColor = socketItem.ItemColor,
                Description = ""
            }, Color.Transparent);

            var pos = positions[i];
            int drawX = socketItem.Code == "gemsocket" ? pos.X - 1 : pos.X;
            int drawY = socketItem.Code == "gemsocket" ? pos.Y + 1 : pos.Y;
            graphics.DrawImage(socketBitmap, drawX, drawY);
        }
    }

    private static List<Point> CalculateSocketPositions(int count, (int X, int Y) gridSize,
        int baseX, int spacing, int offsetY, int y1, int y2, int y3, int y4)
    {
        var positions = new List<Point>();

        int x1 = baseX;
        int x2 = x1 + spacing;
        int x3 = x2 + spacing;

        switch (count)
        {
            case 1:
                positions.Add(gridSize.Y == 2
                    ? new Point(gridSize.X == 1 ? x1 : x2, y1 + spacing + offsetY)
                    : gridSize.Y == 3
                        ? new Point(gridSize.X == 1 ? x1 : x2, y2 + offsetY)
                        : new Point(gridSize.X == 1 ? x1 : x2, y2 + spacing + offsetY));
                break;
            case 2:
                if (gridSize.Y == 2)
                {
                    positions.Add(new Point(gridSize.X == 1 ? x1 : x2, y1 + offsetY));
                    positions.Add(new Point(gridSize.X == 1 ? x1 : x2, y2 + offsetY));
                }
                else if (gridSize.Y == 3)
                {
                    positions.Add(new Point(gridSize.X == 1 ? x1 : x2, y1 + spacing + offsetY));
                    positions.Add(new Point(gridSize.X == 1 ? x1 : x2, y2 + spacing + offsetY));
                }
                else
                {
                    positions.Add(new Point(gridSize.X == 1 ? x1 : x2, y1 + spacing + offsetY));
                    positions.Add(new Point(gridSize.X == 1 ? x1 : x2, y3 + spacing + offsetY));
                }

                break;
            case 3:
                if (gridSize.Y == 2)
                {
                    positions.Add(new Point(x1, y1 + offsetY));
                    positions.Add(new Point(x3, y1 + offsetY));
                    positions.Add(new Point(x2, y2 + offsetY));
                }
                else if (gridSize.Y == 3)
                {
                    int x = gridSize.X == 1 ? x1 : x2;
                    positions.Add(new Point(x, y1 + offsetY));
                    positions.Add(new Point(x, y2 + offsetY));
                    positions.Add(new Point(x, y3 + offsetY));
                }
                else
                {
                    int x = gridSize.X == 1 ? x1 : x2;
                    positions.Add(new Point(x, y1 + spacing + offsetY));
                    positions.Add(new Point(x, y2 + spacing + offsetY));
                    positions.Add(new Point(x, y3 + spacing + offsetY));
                }

                break;
            case 4:
                if (gridSize.Y == 3)
                {
                    positions.Add(new Point(x1, y1 + spacing + offsetY));
                    positions.Add(new Point(x3, y1 + spacing + offsetY));
                    positions.Add(new Point(x1, y2 + spacing + offsetY));
                    positions.Add(new Point(x3, y2 + spacing + offsetY));
                }
                else if (gridSize.Y == 2)
                {
                    positions.Add(new Point(x1, y1 + offsetY));
                    positions.Add(new Point(x3, y1 + offsetY));
                    positions.Add(new Point(x1, y2 + offsetY));
                    positions.Add(new Point(x3, y2 + offsetY));
                }
                else
                {
                    int x = gridSize.X == 1 ? x1 : x2;
                    positions.Add(new Point(x, y1 + offsetY));
                    positions.Add(new Point(x, y2 + offsetY));
                    positions.Add(new Point(x, y3 + offsetY));
                    positions.Add(new Point(x, y4 + offsetY));
                }

                break;
            case 5:
                if (gridSize.Y == 3)
                {
                    positions.Add(new Point(x1, y1 + offsetY));
                    positions.Add(new Point(x3, y1 + offsetY));
                    positions.Add(new Point(x2, y2 + offsetY));
                    positions.Add(new Point(x1, y3 + offsetY));
                    positions.Add(new Point(x3, y3 + offsetY));
                }
                else
                {
                    positions.Add(new Point(x1, y1 + spacing + offsetY));
                    positions.Add(new Point(x3, y1 + spacing + offsetY));
                    positions.Add(new Point(x2, y2 + spacing + offsetY));
                    positions.Add(new Point(x1, y3 + spacing + offsetY));
                    positions.Add(new Point(x3, y3 + spacing + offsetY));
                }

                break;
            case 6:
                if (gridSize.Y == 3)
                {
                    positions.Add(new Point(x1, y1 + offsetY));
                    positions.Add(new Point(x3, y1 + offsetY));
                    positions.Add(new Point(x1, y2 + offsetY));
                    positions.Add(new Point(x3, y2 + offsetY));
                    positions.Add(new Point(x1, y3 + offsetY));
                    positions.Add(new Point(x3, y3 + offsetY));
                }
                else
                {
                    positions.Add(new Point(x1, y1 + spacing + offsetY));
                    positions.Add(new Point(x3, y1 + spacing + offsetY));
                    positions.Add(new Point(x1, y2 + spacing + offsetY));
                    positions.Add(new Point(x3, y2 + spacing + offsetY));
                    positions.Add(new Point(x1, y3 + spacing + offsetY));
                    positions.Add(new Point(x3, y3 + spacing + offsetY));
                }

                break;
        }

        return positions;
    }

    private void RenderDescription(Graphics graphics, string description, int width, int top, string fontFamily)
    {
        // Convert escape sequences to actual characters
        string normalized = NormalizeColorCodes(description);
        string[] lines = normalized.Replace("\\n", "\n").Split('\n');
        int y = top - 1;
        var currentColor = D2Colors.White;

        using var font = CreateFont(fontFamily, 9);

        foreach (var line in lines)
        {
            // Calculate line width without color codes for centering
            string stripped = StripColorCodes(line);
            float lineWidth = graphics.MeasureString(stripped, font).Width;
            float x = (width - lineWidth) / 2;

            // Parse and render colored segments
            RenderColoredLine(graphics, line, font, x, y, ref currentColor);
            y += TextLineHeight;
        }
    }

    /// <summary>
    /// Converts \xff escape sequences to actual ÿ character (0xFF)
    /// </summary>
    private static string NormalizeColorCodes(string text)
    {
        // Replace \xff (literal backslash-x-f-f) with actual ÿ character
        return text.Replace("\\xff", "ÿ").Replace("\\xfF", "ÿ").Replace("\\xFf", "ÿ").Replace("\\xFF", "ÿ");
    }

    private static void RenderColoredLine(Graphics graphics, string line, Font font, float startX, float y, ref Color currentColor)
    {
        // Split by ÿ (0xFF) - the D2 color code delimiter
        string[] segments = line.Split('ÿ');
        float x = startX;

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];

            // First segment has no color prefix, subsequent ones do
            if (i > 0 && segment.Length > 0)
            {
                // Parse color code after ÿ
                if (segment[0] == 'c' && segment.Length > 1)
                {
                    // ÿc[code] format
                    char code = segment[1];
                    if (code is (>= '0' and <= '9') or ':' or ';' or '<')
                        currentColor = D2Colors.GetTextColor(code);
                    segment = segment.Length > 2 ? segment[2..] : "";
                }
                else if (segment[0] == '#' && segment.Length >= 7)
                {
                    // ÿ#RRGGBB hex color - skip the hex code
                    segment = segment.Length > 7 ? segment[7..] : "";
                }
            }

            if (!string.IsNullOrEmpty(segment))
            {
                using var brush = new SolidBrush(currentColor);
                graphics.DrawString(segment, font, brush, x, y);
                x += graphics.MeasureString(segment, font).Width;
            }
        }
    }

    private static string StripColorCodes(string text)
    {
        // First normalize escape sequences
        string result = NormalizeColorCodes(text);

        // Remove ÿc[0-9:;<] color codes (3 chars: ÿ + c + code)
        int idx;
        while ((idx = result.IndexOf("ÿc", StringComparison.Ordinal)) >= 0)
        {
            if (idx + 2 < result.Length)
                result = result.Remove(idx, 3);
            else
                break;
        }

        // Remove ÿ#RRGGBB hex color codes (8 chars: ÿ + # + 6 hex)
        while ((idx = result.IndexOf("ÿ#", StringComparison.Ordinal)) >= 0)
        {
            if (idx + 8 <= result.Length)
                result = result.Remove(idx, 8);
            else
                break;
        }

        return result;
    }

    private int CalculateTextWidth(string[] lines, string fontFamily)
    {
        using var bmp = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bmp);
        using var font = CreateFont(fontFamily, 9);

        float maxWidth = 0;
        foreach (var line in lines)
        {
            // Strip color codes before measuring
            string stripped = StripColorCodes(line);
            var size = graphics.MeasureString(stripped, font);
            if (size.Width > maxWidth)
                maxWidth = size.Width;
        }

        return (int)maxWidth;
    }

    private static Bitmap? LoadBackgroundBitmap(int gridY)
    {
        try
        {
            using var stream = EmbeddedResourceLoader.LoadStream($"D2BotNG.wwwroot.assets.rendering.bgnd{gridY}.png");
            if (stream == null) return null;
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] BitmapToPng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
