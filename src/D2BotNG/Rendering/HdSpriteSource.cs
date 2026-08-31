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
/// cell. The frontend is arranged the same way, for the same reason — an early version there
/// reimplemented the compositing and immediately disagreed with the original, stacking a runeword's
/// runes down the middle of the item instead of laying them out in columns.
///
/// The art is packed as nine archives of concatenated PNGs plus an index giving each sprite's
/// [archive, offset, length]. A slice of an archive IS a whole PNG file, so there is nothing to
/// unpack: the slice goes straight to the platform decoder.
/// </summary>
public sealed class HdSpriteSource
{
    private const string ResourcePrefix = "D2BotNG.wwwroot.assets.rendering.hd.";
    private const int ArchiveCount = 9;

    /// <summary>
    /// The marker drawn in a socket nothing is set into.
    ///
    /// D2R does ship a sprite under this name and it is deliberately NOT used: it is an opaque
    /// filled disc a whole inventory cell across, against socket positions spaced 14px apart for a
    /// small translucent dot, so a six-socket item ends up tiled edge to edge. It is this app's own
    /// affordance rather than artwork either game draws — D2 shows an empty socket as a hole in the
    /// item — so both styles use the classic one.
    /// </summary>
    public const string EmptySocketCode = "gemsocket";

    private readonly ILogger<HdSpriteSource> _logger;
    private readonly Lazy<Manifest?> _manifest;
    private readonly ConcurrentDictionary<int, byte[]> _archives = new();

    public HdSpriteSource(ILogger<HdSpriteSource> logger)
    {
        _logger = logger;
        _manifest = new Lazy<Manifest?>(LoadManifest);
    }

    /// <summary>
    /// One sprite's D2R pixels, tinted, or null when there is no D2R art for the code.
    ///
    /// Null is not an error — the archives cover the base game, and a modded code has none. The
    /// caller falls back to the classic sprite, so the style degrades per item rather than leaving
    /// a hole where an item plainly is.
    /// </summary>
    public Bitmap? Render(string code, string? colorName, int gfxIndex)
    {
        var manifest = _manifest.Value;
        if (manifest == null || string.Equals(code, EmptySocketCode, StringComparison.OrdinalIgnoreCase))
            return null;

        var key = code.ToLowerInvariant();

        // A code may already have its variant baked in. A v2 capture reports the item and the
        // graphic it rolled separately, but wire schema v1 and the mule files carry the resolved
        // sprite name the game handed them -- `amu2`, not `amu` plus a 2 -- so a trailing digit is
        // read back off the code when the whole thing names no item. Tried in that order because
        // real codes end in digits too: `ob5` is an item, not the fifth variant of `ob`.
        manifest.Items.TryGetValue(key, out var item);
        if (item == null && key.Length > 1 && char.IsAsciiDigit(key[^1]))
        {
            if (manifest.Items.TryGetValue(key[..^1], out var stem))
            {
                item = stem;
                gfxIndex = key[^1] - '0';
            }
        }

        var sprite = item != null
            ? SpriteName(item, gfxIndex, manifest)
            : manifest.Index.ContainsKey(key) ? key : null;
        if (sprite == null) return null;

        var entry = manifest.Index[sprite];
        var archive = LoadArchive(entry[0]);
        if (archive == null) return null;

        Bitmap decoded;
        try
        {
            // A copy, not a view: the stream must own its bytes for the decoder's lifetime, and
            // GDI+ keeps the stream alive behind the Bitmap.
            using var stream = new MemoryStream(archive, entry[1], entry[2], writable: false);
            using var raw = new Bitmap(stream);
            decoded = new Bitmap(raw);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to decode D2R sprite {Sprite}", sprite);
            return null;
        }

        // A sprite with no item behind it has no transform group and takes no tint.
        var value = item != null ? TintValue(item.InvTrans, colorName) : 0;
        if (value != 0) Tint(decoded, value, sprite, manifest);
        return decoded;
    }

    /// <summary>
    /// The colour name for an item that only knows its classic palette shift.
    ///
    /// D2R tints by the <c>invtransform</c> NAME on the item's unique or set row, and only a v2
    /// capture carries the row index to look one up. But the shift index every other source already
    /// sends indexes colors.txt, and colors.txt row N holds that same colour's name — checked
    /// against all 320 unique and set rows that define one, agreeing on every single one. So a mule
    /// line or a v1 character reaches the D2R art from what it already sends.
    /// </summary>
    public static string? ColorNameForShift(int colorShift) =>
        colorShift >= 0 && colorShift < HdTintTables.ColorNames.Length
            ? HdTintTables.ColorNames[colorShift]
            : null;

    /// <summary>
    /// The composite tint: the base item's transform row and the quality's colour, in one number.
    /// <c>% 10</c> because the row is the low digit of <c>invtrans</c>.
    /// </summary>
    private static int TintValue(int invTrans, string? colorName)
    {
        if (string.IsNullOrEmpty(colorName)) return 0;
        var colour = Array.IndexOf(HdTintTables.ColorNames, colorName);
        return colour < 0 ? 0 : (invTrans % 10) * HdTintTables.ColorNames.Length + colour;
    }

    /// <summary>
    /// Which sprite an item draws with. A type claiming variants does not guarantee the art exists
    /// — an item with its own sprite ignores the variant system — so a missing one falls back to
    /// the base rather than to nothing.
    /// </summary>
    private static string? SpriteName(HdItem item, int gfxIndex, Manifest manifest)
    {
        if (item.VarInvGfx > 0 && gfxIndex > 0)
        {
            var variant = $"{item.Hd}{gfxIndex}".ToLowerInvariant();
            if (manifest.Index.ContainsKey(variant)) return variant;
        }

        var basic = item.Hd.ToLowerInvariant();
        return manifest.Index.ContainsKey(basic) ? basic : null;
    }

    private byte[]? LoadArchive(int n)
    {
        if (n < 0 || n >= ArchiveCount) return null;
        return _archives.GetOrAdd(n, i =>
        {
            try
            {
                return EmbeddedResourceLoader.LoadBytes($"{ResourcePrefix}hditems{i}.pngx");
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

    private Manifest? LoadManifest()
    {
        try
        {
            var index = JsonSerializer.Deserialize<Dictionary<string, int[]>>(
                EmbeddedResourceLoader.LoadBytes($"{ResourcePrefix}hditemlib.json"));
            var items = JsonSerializer.Deserialize<HdManifestFile>(
                EmbeddedResourceLoader.LoadBytes($"{ResourcePrefix}hditems.json"));
            if (index == null || items?.Codes == null) return null;

            return new Manifest(index, items.Codes, items.RangeOverride ?? [], items.TransformOverride ?? []);
        }
        catch (Exception ex)
        {
            // Not fatal: the setting simply has nothing to switch to, and every item keeps its
            // classic sprite.
            _logger.LogWarning(ex, "D2R item artwork is unavailable; the classic sprites will be used");
            return null;
        }
    }

    private sealed record Manifest(
        Dictionary<string, int[]> Index,
        Dictionary<string, HdItem> Items,
        Dictionary<string, float[]> RangeOverride,
        Dictionary<string, float[]> TransformOverride);

    /// <summary>Populated by the deserializer, which is why the setters look unused.</summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class HdManifestFile
    {
        [JsonPropertyName("codes")] public Dictionary<string, HdItem>? Codes { get; init; }
        [JsonPropertyName("rangeOverride")] public Dictionary<string, float[]>? RangeOverride { get; init; }
        [JsonPropertyName("transformOverride")] public Dictionary<string, float[]>? TransformOverride { get; init; }
    }

    /// <summary>Populated by the deserializer, which is why the setters look unused.</summary>
    [UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
    private sealed class HdItem
    {
        [JsonPropertyName("hd")] public string Hd { get; init; } = "";
        [JsonPropertyName("invtrans")] public int InvTrans { get; init; }
        [JsonPropertyName("varinvgfx")] public int VarInvGfx { get; init; }
    }

    // ---- the tint ---------------------------------------------------------------------------

    /// <summary>sRGB to linear light, the piecewise curve the standard defines.</summary>
    private static float ToLinear(float c) =>
        c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float ToSrgb(float c) =>
        c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    /// <summary>
    /// The mix itself: NOT a move toward the target but a blend between the channel and the channel
    /// multiplied by it, done in linear light. A brown that carries almost no blue stays that way
    /// however blue the target is, which is why a tint can look like nothing happened.
    /// </summary>
    private static float MixToward(float channel, float targetLinear, float mix)
    {
        var lin = ToLinear(channel);
        return lin + mix * (lin * targetLinear - lin);
    }

    /// <summary>Back to sRGB and to a byte. Clamped only here, never before the mix.</summary>
    private static int Quantise(float linear) =>
        (int)MathF.Round(Math.Clamp(ToSrgb(linear), 0f, 1f) * 255f);

    /// <summary>
    /// Distance around the unit circle.
    ///
    /// Single precision throughout, which C#'s <c>float</c> gives natively — the frontend has to
    /// say <c>Math.fround</c> at every step to get the same thing. It matters because the range
    /// test is a strict comparison: a pure blue's saturation is exactly 1, whose distance from a
    /// 0.14 centre is exactly 0.14 in double precision and so EXCLUDED, but 0.13999998 in single
    /// and so included.
    /// </summary>
    private static float Wrapped(float a, float b)
    {
        var d = MathF.Abs(a - b);
        return MathF.Min(1f - d, d);
    }

    /// <summary>
    /// Recolour in place.
    ///
    /// Two paths, chosen by the tint value's magnitude. Below nine range rows it is a SELECTIVE
    /// transform: pixels whose hue, saturation and value all sit inside a named band are rotated
    /// and mixed toward a target colour and everything else is left alone, which is how a helm's
    /// gold trim recolours while its leather does not. At or above nine it is a flat multiply in
    /// linear light over every pixel.
    ///
    /// This reproduces the game's kernel including the parts that look like mistakes, and that is
    /// the whole discipline of the function. Two are marked below — the hue computed as a
    /// difference of quotients, and the sector left unreduced. Both look like arithmetic that could
    /// be simplified and both are visibly wrong when simplified: the first turned Tal Rasha's belt
    /// from purple to red, because D2's palettes put an entire item's pixels exactly on a band edge
    /// that a strict comparison then decides differently.
    /// </summary>
    private static void Tint(Bitmap bitmap, int value, string sprite, Manifest manifest)
    {
        var colours = HdTintTables.ColorNames.Length;
        if (value < colours || value == colours * 9) return;

        var row = value / colours;
        var colour = value % colours;

        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadWrite,
            PixelFormat.Format32bppArgb);
        try
        {
            var count = bitmap.Width * bitmap.Height;
            var pixels = new int[count];
            Marshal.Copy(data.Scan0, pixels, 0, count);

            if (row >= HdTintTables.Ranges.Length)
                FlatTint(pixels, colour);
            else
                BandTint(pixels, row, colour, sprite, manifest);

            Marshal.Copy(pixels, 0, data.Scan0, count);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void FlatTint(int[] pixels, int colour)
    {
        var tint = colour < HdTintTables.Flat.Length ? HdTintTables.Flat[colour] : [1f, 1f, 1f, 1f];
        float[] target = [ToLinear(tint[0]), ToLinear(tint[1]), ToLinear(tint[2])];
        var strength = tint[3];

        for (var i = 0; i < pixels.Length; i++)
        {
            var argb = pixels[i];
            var alpha = (argb >> 24) & 0xFF;
            var r = Mix(((argb >> 16) & 0xFF) / 255f, target[0], strength);
            var g = Mix(((argb >> 8) & 0xFF) / 255f, target[1], strength);
            var b = Mix((argb & 0xFF) / 255f, target[2], strength);
            pixels[i] = (alpha << 24) | (r << 16) | (g << 8) | b;
        }
        return;

        static int Mix(float channel, float targetLinear, float strength) =>
            Quantise(MixToward(channel, targetLinear, strength));
    }

    private static void BandTint(int[] pixels, int row, int colour, string sprite, Manifest manifest)
    {
        var range = manifest.RangeOverride.TryGetValue(sprite, out var rangeOverride)
            ? rangeOverride
            : HdTintTables.Ranges[row];
        var transform =
            manifest.TransformOverride.TryGetValue($"{sprite}:{HdTintTables.ColorNames[colour]}", out var transformOverride)
                ? transformOverride
                : HdTintTables.Transforms[colour];

        float hueAt = range[0], hueSpan = range[1];
        float satAt = range[2], satSpan = range[3];
        float valAt = range[4], valSpan = range[5];
        float mix = transform[3], hueShift = transform[4], satScale = transform[5], valScale = transform[6];
        float[] target = [ToLinear(transform[0]), ToLinear(transform[1]), ToLinear(transform[2])];

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

            // Hue as a DIFFERENCE OF TWO QUOTIENTS, which is how the game computes it, rather than
            // as the single division the algebra reduces to. The two agree to about one part in ten
            // million and disagree about which side of a band edge a value falls on.
            var hue = 0f;
            if (delta != 0f)
            {
                var half = delta * 0.5f;
                var fromR = ((max - r) * (1f / 6f) + half) / delta;
                var fromG = ((max - g) * (1f / 6f) + half) / delta;
                var fromB = ((max - b) * (1f / 6f) + half) / delta;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (max == r) hue = fromB - fromG;
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                else if (max == g) hue = fromR + 1f / 3f - fromB;
                else hue = fromG + 2f / 3f - fromR;

                if (hue < 0f) hue += 1f;
                else if (hue > 1f) hue -= 1f;
            }

            var sat = max == 0f ? 0f : delta / max;

            if (Wrapped(hue, hueAt) >= hueSpan ||
                Wrapped(sat, satAt) >= satSpan ||
                Wrapped(max, valAt) >= valSpan)
            {
                continue;
            }

            // Unclamped, as the kernel leaves them: a scale past 1 is meaningful to the arithmetic
            // below, and clamping here capped channels the game lets saturate.
            var shifted = hue + hueShift;
            var h = shifted - MathF.Floor(shifted);
            var s = sat + sat * satScale;
            var v = max + max * valScale;

            // Not reduced modulo 6. `h` can be exactly 1, which makes this 6, matches no case and
            // falls through to the last arm with a fractional part of zero.
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
                _ => new[] { v, p, q },
            };

            var outR = Quantise(MixToward(rgb[0], target[0], mix));
            var outG = Quantise(MixToward(rgb[1], target[1], mix));
            var outB = Quantise(MixToward(rgb[2], target[2], mix));
            pixels[i] = (alpha << 24) | (outR << 16) | (outG << 8) | outB;
        }
    }
}
