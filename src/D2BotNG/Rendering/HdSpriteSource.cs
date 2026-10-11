using System.Buffers.Binary;
using System.IO.Compression;
using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using D2BotNG.Utilities;
using JetBrains.Annotations;

namespace D2BotNG.Rendering;

/// <summary>
/// D2R's item artwork, as an alternative source of sprite pixels to the classic DC6s.
///
/// This produces ONE SPRITE and nothing else. Sizing the canvas, centring the artwork, laying out
/// socket markers and dimming an ethereal item all stay in <see cref="ItemRenderer" />, because
/// they are the same job whichever artwork is used: both are drawn at 30 pixels to an inventory
/// cell. The frontend's <c>hdRenderer.ts</c> is the same code; keep the two in step.
///
/// Everything here comes from the game: <c>scripts/generate-hd-assets.mjs</c> builds the archives
/// and the index from a D2R data dump (the art, which art each item draws, and the colouring tables
/// the game's <c>SpriteColoringHelper</c> loads), and <see cref="Tint" /> is a port of the shader
/// that applies them. A slice of an archive IS a whole PNG file, so it goes straight to the
/// platform decoder.
/// </summary>
public sealed class HdSpriteSource
{
    private const string ResourcePrefix = "D2BotNG.wwwroot.assets.rendering.hd.";

    /// <summary>
    /// The code a socket nothing is set into is reported under. <see cref="Render" /> declines it:
    /// D2R's marker is a cell-sized disc that only works on D2R's own socket layout, so it is drawn
    /// through <see cref="SocketLayout" /> and <see cref="RenderSocketMarker" />, and the classic
    /// layout keeps the classic marker.
    /// </summary>
    public const string EmptySocketCode = "gemsocket";

    private readonly ILogger<HdSpriteSource> _logger;
    private readonly Lazy<HdIndex?> _index;
    private readonly ConcurrentDictionary<int, byte[]> _archives = new();

    public HdSpriteSource(ILogger<HdSpriteSource> logger)
    {
        _logger = logger;
        _index = new Lazy<HdIndex?>(LoadIndex);
    }

    /// <summary>
    /// One sprite's D2R pixels, tinted, or null when there is no D2R art for the code.
    ///
    /// <paramref name="code" /> is the classic sprite name a mule line or a v1 item carries — the
    /// item code, with the rolled variant baked on for the types that have them (<c>amu2</c>).
    /// <paramref name="colorShift" /> is its palette shift, a colors.txt index, which is exactly
    /// the colour half of the palette slot the game's D2R draw is tinted by; -1 for none.
    ///
    /// Null is not an error: the caller falls back to the classic sprite, so the style degrades per
    /// item rather than leaving a hole where an item plainly is.
    /// </summary>
    public Bitmap? Render(string code, int colorShift)
    {
        var index = _index.Value;
        if (index == null || string.Equals(code, EmptySocketCode, StringComparison.OrdinalIgnoreCase))
            return null;

        var key = code.ToLowerInvariant();

        // A trailing digit is the variant when the whole code names no item. Tried in that order
        // because real codes end in digits too: `ob5` is an item, not the fifth variant of `ob`.
        var variant = 0;
        index.Items.TryGetValue(key, out var item);
        if (item == null && key.Length > 1 && char.IsAsciiDigit(key[^1]) &&
            index.Items.TryGetValue(key[..^1], out var stem))
        {
            item = stem;
            variant = key[^1] - '0';
        }

        if (item == null) return null;

        var sprite = item.Variants > 0 && variant >= 1 && variant <= item.Variants
            ? $"{item.Sprite}{variant}"
            : item.Sprite;
        if (!index.Sprites.TryGetValue(sprite, out var entry) || Decode(sprite, entry) is not { } decoded)
            return null;

        if (TintFor(item, sprite, colorShift, index.Coloring) is var (range, transform))
            Tint(decoded, range, transform);
        return decoded;
    }

    /// <summary>
    /// Where D2R draws an item's sockets, as pixel offsets from the item art's top-left at 30 pixels
    /// to a cell, with the alpha the marker is drawn at; null for a size and count the game has no
    /// layout for, or when the art is unavailable.
    ///
    /// The game's own placement (d2r.exe <c>sub_1401B1710</c>): a half-cell grid from a table keyed
    /// by the item's inventory size and socket count, the marker one pixel left of its cell. Every
    /// socket gets the marker, filled or not, and the filler is drawn over it at the same spot.
    /// </summary>
    public (List<Point> Positions, float Alpha)? SocketLayout(int count, int cellsWide, int cellsHigh)
    {
        var index = _index.Value;
        if (index?.SocketLayout.GetValueOrDefault($"{cellsWide}x{cellsHigh}") is not { } bySize ||
            !bySize.TryGetValue(count.ToString(System.Globalization.CultureInfo.InvariantCulture), out var cells))
            return null;

        const int half = 30 / 2;
        return ([.. cells.Select(c => new Point((c[0] - 1) * half - 1, (c[1] - 1) * half))], index.SocketAlpha);
    }

    /// <summary>The marker D2R draws under every socket, untinted; null when unavailable.</summary>
    public Bitmap? RenderSocketMarker()
    {
        var index = _index.Value;
        return index != null && index.Sprites.TryGetValue(index.EmptySocket, out var entry) ? Decode(index.EmptySocket, entry) : null;
    }

    private Bitmap? Decode(string sprite, int[] entry)
    {
        var archive = LoadArchive(entry[0]);
        if (archive == null) return null;
        try
        {
            return DecodePng(archive.AsSpan(entry[1], entry[2]));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to decode D2R sprite {Sprite}", sprite);
            return null;
        }
    }

    /// <summary>
    /// One of the generator's PNGs (8-bit RGBA, not interlaced) to a bitmap, decoded here rather
    /// than by GDI+.
    ///
    /// Copying a GDI+-decoded image into a fresh bitmap draws it through the premultiplied
    /// pipeline, which rounds every semi-transparent pixel's colour — at alpha 2/255 it is simply
    /// lost — before the tint's strict range test sees it. Decoding the file directly hands the
    /// kernel exactly the bytes <c>hdRenderer.ts</c> decodes, so the two produce the same pixels.
    /// </summary>
    private static Bitmap DecodePng(ReadOnlySpan<byte> png)
    {
        var width = BinaryPrimitives.ReadInt32BigEndian(png[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(png[20..]);
        if (png[24] != 8 || png[25] != 6 || png[28] != 0)
            throw new InvalidDataException("D2R sprite is not an 8-bit RGBA, non-interlaced PNG");

        using var idat = new MemoryStream();
        for (var at = 8; at < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[at..]);
            if (png.Slice(at + 4, 4).SequenceEqual("IDAT"u8)) idat.Write(png.Slice(at + 8, length));
            at += 12 + length;
        }

        idat.Position = 0;
        var stride = width * 4;
        var inflated = new byte[(stride + 1) * height];
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
            zlib.ReadExactly(inflated);

        // Undo the per-row filters (PNG spec, section 9), then store as GDI+'s B, G, R, A.
        var rgba = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = inflated[y * (stride + 1)];
            var src = y * (stride + 1) + 1;
            var row = y * stride;
            for (var x = 0; x < stride; x++)
            {
                var a = x >= 4 ? rgba[row + x - 4] : 0;
                var b = y > 0 ? rgba[row - stride + x] : 0;
                var c = x >= 4 && y > 0 ? rgba[row - stride + x - 4] : 0;
                var predicted = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) >> 1,
                    4 => Paeth(a, b, c),
                    _ => 0
                };
                rgba[row + x] = (byte)(inflated[src + x] + predicted);
            }
        }

        var bgra = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < height; y++)
                Marshal.Copy(bgra, y * stride, data.Scan0 + y * data.Stride, stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;

        static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    /// <summary>
    /// The tint the game would draw an item with, or null for none.
    ///
    /// <c>ITEMS_GetItemPalette</c> hands the draw a palette slot <c>class * 21 + colour</c>, and
    /// only when the class (items.txt InvTrans) is 1..8 but not 3 or 4 — gold and brown have no
    /// slot — and the colour is a real one. No slot, no tint: the overrides are not consulted
    /// either. <c>sub_1400921A0</c> then reads the slot's halves separately: the class picks a
    /// range (unless the sprite overrides it), the colour a transform (unless the sprite overrides
    /// it for that colour).
    /// </summary>
    private static (float[] Range, float[] Transform)? TintFor(HdItem item, string sprite, int colour,
        Coloring coloring)
    {
        var cls = item.InvTrans;
        if (cls is < 1 or > 8 or 3 or 4) return null;
        if (colour < 0 || colour >= coloring.Transforms.Length) return null;
        var range = coloring.RangeOverrides.GetValueOrDefault(sprite) ?? coloring.Ranges[cls];
        var transform = coloring.TransformOverrides.GetValueOrDefault($"{sprite}:{colour}") ??
                        coloring.Transforms[colour];
        return (range, transform);
    }

    private byte[]? LoadArchive(int n)
    {
        if (n < 0 || n >= _index.Value?.Archives) return null;
        return _archives.GetOrAdd(n, i =>
        {
            try
            {
                return EmbeddedResourceLoader.LoadBytes($"{ResourcePrefix}hdart{i}.pngx");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "D2R sprite archive {Index} is missing", i);
                return [];
            }
        }) is { Length: > 0 } bytes
            ? bytes
            : null;
    }

    private HdIndex? LoadIndex()
    {
        try
        {
            var index = JsonSerializer.Deserialize<HdIndex>(
                EmbeddedResourceLoader.LoadBytes($"{ResourcePrefix}hdart.json"));
            return index is { Items.Count: > 0 } ? index : null;
        }
        catch (Exception ex)
        {
            // Not fatal: the setting simply has nothing to switch to, and every item keeps its
            // classic sprite.
            _logger.LogWarning(ex, "D2R item artwork is unavailable; the classic sprites will be used");
            return null;
        }
    }

    /// <summary>The generated <c>hdart.json</c>. Populated by the deserializer.</summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class HdIndex
    {
        [JsonPropertyName("archives")] public int Archives { get; init; }
        [JsonPropertyName("emptySocket")] public string EmptySocket { get; init; } = "";
        [JsonPropertyName("socketAlpha")] public float SocketAlpha { get; init; }

        /// <summary><c>"WxH"</c> -> socket count -> <c>[cx, cy]</c> per socket, half cells, 1-based.</summary>
        [JsonPropertyName("socketLayout")]
        public Dictionary<string, Dictionary<string, int[][]>> SocketLayout { get; init; } = [];

        [JsonPropertyName("coloring")] public Coloring Coloring { get; init; } = new();
        [JsonPropertyName("items")] public Dictionary<string, HdItem> Items { get; init; } = [];
        [JsonPropertyName("sprites")] public Dictionary<string, int[]> Sprites { get; init; } = [];
    }

    /// <summary>Populated by the deserializer.</summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class HdItem
    {
        /// <summary>The sprite drawn, as a path under the game's <c>hd/global/ui/items</c>.</summary>
        [JsonPropertyName("sprite")] public string Sprite { get; init; } = "";

        /// <summary>items.txt InvTrans: which colour range class the item belongs to.</summary>
        [JsonPropertyName("invTrans")] public int InvTrans { get; init; }

        /// <summary>How many interchangeable graphics (<c>sprite1</c>..<c>spriteN</c>) it rolls between.</summary>
        [JsonPropertyName("variants")] public int Variants { get; init; }
    }

    /// <summary>
    /// The game's <c>SpriteColoringHelper</c>. A range is <c>[hueCentre, hueSpan, satCentre,
    /// satSpan, valCentre, valSpan]</c>; a transform <c>[hueShift, satScale, valScale, tintR, tintG,
    /// tintB, tintAmount]</c>. Populated by the deserializer.
    /// </summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class Coloring
    {
        /// <summary>Per InvTrans class.</summary>
        [JsonPropertyName("ranges")] public float[][] Ranges { get; init; } = [];

        /// <summary>Per colour, in colors.txt order.</summary>
        [JsonPropertyName("transforms")] public float[][] Transforms { get; init; } = [];

        [JsonPropertyName("rangeOverrides")] public Dictionary<string, float[]> RangeOverrides { get; init; } = [];

        /// <summary>Keyed <c>sprite:colour</c>.</summary>
        [JsonPropertyName("transformOverrides")]
        public Dictionary<string, float[]> TransformOverrides { get; init; } = [];
    }

    // ---- the tint ---------------------------------------------------------------------------

    // Every operation in the tint is a float operation, rounded, which is what hdRenderer.ts spells
    // out with Math.fround at each step; the two must produce the same pixels. Powers are the one
    // exception both sides share: computed in double and then rounded, because neither runtime's
    // single-precision pow is specified to the last bit.

    /// <summary>sRGB to linear light, as the shader writes it: the piecewise curve over |c|.</summary>
    private static float ToLinear(float c) =>
        c < 0.04045f ? c / 12.92f : (float)Math.Pow(MathF.Abs(c + 0.055f) / 1.055f, 2.4f);

    /// <summary>Linear light to sRGB, as the shader's output stage writes it at the default gamma.</summary>
    private static float ToSrgb(float c) =>
        c < 0.003131f ? c * 12.92f : 1.055f * (float)Math.Pow(MathF.Abs(c), 1f / 2.4f) - 0.055f;

    /// <summary>
    /// Distance around the unit circle, <c>min(|d|, 1 - |d|)</c>. The shader measures all three of
    /// hue, saturation and value this way, not only hue. Single precision, which C#'s <c>float</c>
    /// gives natively and the range test depends on: it is a strict comparison, and whole items sit
    /// exactly on a range boundary that double precision decides the other way.
    /// </summary>
    private static float Wrapped(float a, float b)
    {
        var d = MathF.Abs(a - b);
        return MathF.Min(1f - d, d);
    }

    /// <summary>
    /// Recolour in place: a port of D2R's UI pixel shader in its item-tint mode
    /// (<c>hd/shaders/ps_ui_p.bin</c>, UI_DRAW_MODE 4).
    ///
    /// A pixel whose hue, saturation and value all sit strictly inside the range is rotated in hue,
    /// scaled in saturation and value, converted to linear light and mixed toward the tint colour by
    /// the tint amount; every other pixel keeps its own colour. Two things that look like mistakes
    /// are the shader's own and are kept: hue is a DIFFERENCE OF TWO QUOTIENTS rather than the
    /// single division it reduces to (they disagree in the last bit, which decides a boundary), and
    /// the sector is not reduced modulo 6. Nothing is clamped before the final quantisation.
    /// </summary>
    private static void Tint(Bitmap bitmap, float[] range, float[] transform)
    {
        float hueAt = range[0], hueSpan = range[1];
        float satAt = range[2], satSpan = range[3];
        float valAt = range[4], valSpan = range[5];
        float hueShift = transform[0], satScale = transform[1], valScale = transform[2];
        float[] target = [ToLinear(transform[3]), ToLinear(transform[4]), ToLinear(transform[5])];
        var amount = transform[6];

        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        try
        {
            var count = bitmap.Width * bitmap.Height;
            var pixels = new int[count];
            Marshal.Copy(data.Scan0, pixels, 0, count);

            for (var i = 0; i < pixels.Length; i++)
            {
                var argb = pixels[i];
                var alpha = (argb >> 24) & 0xFF;
                if (alpha == 0) continue;

                var r = ((argb >> 16) & 0xFF) / 255f;
                var g = ((argb >> 8) & 0xFF) / 255f;
                var b = (argb & 0xFF) / 255f;
                var max = MathF.Max(r, MathF.Max(g, b));
                var delta = max - MathF.Min(r, MathF.Min(g, b));

                float hue = 0f, sat = 0f;
                if (delta != 0f)
                {
                    sat = delta / max;
                    var half = delta * 0.5f;
                    var fromR = ((max - r) * (1f / 6f) + half) / delta;
                    var fromG = ((max - g) * (1f / 6f) + half) / delta;
                    var fromB = ((max - b) * (1f / 6f) + half) / delta;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    if (r == max) hue = fromB - fromG;
                    // ReSharper disable once CompareOfFloatsByEqualityOperator
                    else if (g == max) hue = fromR + 1f / 3f - fromB;
                    else hue = fromG + 2f / 3f - fromR;

                    if (hue < 0f) hue += 1f;
                    if (hue > 1f) hue -= 1f;
                }

                if (!(Wrapped(hue, hueAt) < hueSpan) ||
                    !(Wrapped(sat, satAt) < satSpan) ||
                    !(Wrapped(max, valAt) < valSpan))
                {
                    continue;
                }

                var shifted = hue + hueShift;
                var h = shifted - MathF.Floor(shifted);
                var s = sat * satScale + sat;
                var v = max * valScale + max;

                var sector = (int)MathF.Floor(h * 6f);
                var f = h * 6f - sector;
                var p = v * (1f - s);
                var q = v * (1f - s * f);
                var t = v * (1f - s * (1f - f));

                var rgb = sector switch
                {
                    _ when s == 0f => new[] { v, v, v },
                    0 => [v, t, p],
                    1 => [q, v, p],
                    2 => [p, v, t],
                    3 => [p, q, v],
                    4 => [t, p, v],
                    _ => new[] { v, p, q }
                };

                var outR = Quantise(rgb[0], target[0], amount);
                var outG = Quantise(rgb[1], target[1], amount);
                var outB = Quantise(rgb[2], target[2], amount);
                pixels[i] = (alpha << 24) | (outR << 16) | (outG << 8) | outB;
            }

            Marshal.Copy(pixels, 0, data.Scan0, count);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>
    /// The mix and the output stage: NOT a move toward the tint but a blend between the channel and
    /// the channel multiplied by it, in linear light, then back to sRGB — clamped only here.
    /// </summary>
    private static int Quantise(float channel, float targetLinear, float amount)
    {
        var lin = ToLinear(channel);
        var mixed = lin + amount * (lin * targetLinear - lin);
        // Halves round up, as JavaScript's Math.round does; MathF.Round's default rounds them to even.
        return (int)MathF.Round(Math.Clamp(ToSrgb(mixed), 0f, 1f) * 255f, MidpointRounding.AwayFromZero);
    }
}
