using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// One glyph as a canvas font draws it: its box relative to the pen on the
/// baseline and its advance, in canvas pixels, its place in the atlas as UVs,
/// and the font's own index for it, which kerning is looked up by.
/// </summary>
internal readonly record struct CanvasGlyph(
    float OffsetX,
    float OffsetY,
    float Width,
    float Height,
    float Advance,
    float U0,
    float V0,
    float U1,
    float V1,
    int GlyphIndex);

/// <summary>What one bake produced: a single-channel atlas and the metrics to draw from it.</summary>
internal sealed record CanvasFontBake(
    byte[] Coverage,
    int AtlasWidth,
    int AtlasHeight,
    float PixelSize,
    float Scale,
    float LineHeight,
    float Ascent,
    IReadOnlyDictionary<int, CanvasGlyph> Glyphs);

/// <summary>
/// Bakes a TrueType or CFF OpenType font at one size into a single-channel
/// coverage atlas. Only the requested characters the font actually has are
/// packed, from an explicit list: StbTrueTypeSharp's skip-missing switch
/// makes the packer report failure even when every glyph fitted. The atlas
/// is the smallest in a fixed ladder of sizes, up to 2048 on a side, that
/// holds them all.
/// </summary>
internal static class CanvasFontBaker
{
    internal const int MaximumAtlasSide = 2048;

    internal const int MaximumScannedCodepoints = 65_536;

    /// <summary>The characters baked when none are named: Latin, Greek and Cyrillic, general punctuation.</summary>
    internal static readonly IReadOnlyList<(int First, int Last)> DefaultRanges =
        [(0x20, 0x24F), (0x370, 0x52F), (0x2000, 0x206F)];

    private static readonly (int Width, int Height)[] AtlasSizes =
        [(256, 256), (512, 256), (512, 512), (1024, 512), (1024, 1024), (2048, 1024), (2048, 2048)];

    internal static unsafe bool TryBake(
        byte[] fontBytes,
        float pixelSize,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        [NotNullWhen(true)] out CanvasFontBake? bake,
        [NotNullWhen(false)] out string? failure)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(ranges);
        bake = null;
        if (!float.IsFinite(pixelSize) || pixelSize <= 0f)
        {
            failure = "the size is not a positive number";
            return false;
        }

        StbTrueType.stbtt_fontinfo? info;
        try
        {
            info = StbTrueType.CreateFont(fontBytes, 0);
        }
        catch (Exception)
        {
            info = null;
        }
        if (info is null)
        {
            failure = "the file is not a TrueType or OpenType font";
            return false;
        }

        using (info)
        {
            if (!TryCollectCodepoints(info, ranges, maximumGlyphs, out int[] codepoints, out failure))
                return false;

            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelSize);
            int ascent, descent, gap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &gap);
            float baseline = MathF.Round(ascent * scale);
            float lineHeight = MathF.Ceiling((ascent - descent + gap) * scale);

            foreach ((int width, int height) in AtlasSizes)
            {
                byte[] coverage = new byte[width * height];
                var packed = new StbTrueType.stbtt_packedchar[codepoints.Length];
                if (!TryPack(fontBytes, pixelSize, codepoints, coverage, width, height, packed))
                    continue;

                var glyphs = new Dictionary<int, CanvasGlyph>(codepoints.Length);
                for (int i = 0; i < codepoints.Length; i++)
                {
                    StbTrueType.stbtt_packedchar p = packed[i];
                    glyphs[codepoints[i]] = new CanvasGlyph(
                        p.xoff,
                        p.yoff,
                        p.x1 - p.x0,
                        p.y1 - p.y0,
                        p.xadvance,
                        p.x0 / (float)width,
                        p.y0 / (float)height,
                        p.x1 / (float)width,
                        p.y1 / (float)height,
                        StbTrueType.stbtt_FindGlyphIndex(info, codepoints[i]));
                }
                bake = new CanvasFontBake(coverage, width, height, pixelSize, scale, lineHeight, baseline, glyphs);
                failure = null;
                return true;
            }

            failure = string.Create(
                CultureInfo.InvariantCulture,
                $"{codepoints.Length} glyphs at {pixelSize} px do not fit a {MaximumAtlasSide}x{MaximumAtlasSide} atlas");
            return false;
        }
    }

    private static bool TryCollectCodepoints(
        StbTrueType.stbtt_fontinfo info,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        out int[] codepoints,
        [NotNullWhen(false)] out string? failure)
    {
        codepoints = [];
        long scanned = 0;
        var present = new SortedSet<int>();
        foreach ((int first, int last) in ranges)
        {
            if (first < 0 || last > 0x10FFFF || last < first)
            {
                failure = $"the range U+{first:X4}-U+{last:X4} is not a range of code points";
                return false;
            }
            scanned += last - first + 1L;
            if (scanned > MaximumScannedCodepoints)
            {
                // Invariant: this reason reaches the log and the tests, whatever the machine's culture.
                failure = string.Create(
                    CultureInfo.InvariantCulture,
                    $"the ranges name more than {MaximumScannedCodepoints:N0} code points");
                return false;
            }
            for (int codepoint = first; codepoint <= last; codepoint++)
            {
                if (StbTrueType.stbtt_FindGlyphIndex(info, codepoint) != 0)
                    present.Add(codepoint);
            }
        }
        if (present.Count == 0)
        {
            failure = "the font has none of the requested characters";
            return false;
        }
        if (present.Count > maximumGlyphs)
        {
            failure = $"the font has {present.Count} of the requested characters; "
                + $"the most a font may prepare is {maximumGlyphs}";
            return false;
        }
        codepoints = [.. present];
        failure = null;
        return true;
    }

    private static unsafe bool TryPack(
        byte[] fontBytes,
        float pixelSize,
        int[] codepoints,
        byte[] coverage,
        int width,
        int height,
        StbTrueType.stbtt_packedchar[] packed)
    {
        var context = new StbTrueType.stbtt_pack_context();
        fixed (byte* pixels = coverage)
        fixed (byte* font = fontBytes)
        fixed (int* points = codepoints)
        fixed (StbTrueType.stbtt_packedchar* chars = packed)
        {
            if (StbTrueType.stbtt_PackBegin(context, pixels, width, height, width, 1, null) == 0)
                return false;
            StbTrueType.stbtt_PackSetOversampling(context, 1, 1);
            var range = new StbTrueType.stbtt_pack_range
            {
                font_size = pixelSize,
                first_unicode_codepoint_in_range = 0,
                array_of_unicode_codepoints = points,
                num_chars = codepoints.Length,
                chardata_for_range = chars,
            };
            int packedAll = StbTrueType.stbtt_PackFontRanges(context, font, 0, &range, 1);
            StbTrueType.stbtt_PackEnd(context);
            return packedAll != 0;
        }
    }
}
