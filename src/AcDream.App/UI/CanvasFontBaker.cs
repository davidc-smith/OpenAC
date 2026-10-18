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
/// holds them all. Sizes smaller than the glyphs' combined area are not
/// tried, so a bake that cannot fit fails before any glyph is drawn.
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

    /// <param name="fontBytes">The font file.</param>
    /// <param name="pixelSize">The size to bake at.</param>
    /// <param name="ranges">The characters to bake, where the font has them.</param>
    /// <param name="maximumGlyphs">The most glyphs the bake may hold.</param>
    /// <param name="bake">The bake, when it succeeds.</param>
    /// <param name="failure">Why it did not, when it does not.</param>
    /// <param name="maximumAtlasBytes">The largest atlas, in bytes, the bake may use.</param>
    internal static unsafe bool TryBake(
        byte[] fontBytes,
        float pixelSize,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        [NotNullWhen(true)] out CanvasFontBake? bake,
        [NotNullWhen(false)] out string? failure,
        long maximumAtlasBytes = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);
        ArgumentNullException.ThrowIfNull(ranges);
        bake = null;
        if (!float.IsFinite(pixelSize) || pixelSize <= 0f)
        {
            failure = "the size is not a positive number";
            return false;
        }

        if (!TryValidateSfnt(fontBytes, out failure))
            return false;

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
            long area = PackedArea(info, codepoints, scale);

            foreach ((int width, int height) in AtlasSizes)
            {
                long atlasBytes = (long)width * height;
                if (atlasBytes > maximumAtlasBytes)
                    break;
                if (atlasBytes < area)
                    continue;
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

            (int Width, int Height) largest = AtlasSizes.LastOrDefault(size => (long)size.Width * size.Height <= maximumAtlasBytes);
            failure = largest == default
                ? string.Create(CultureInfo.InvariantCulture, $"no atlas fits in {maximumAtlasBytes:N0} bytes")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{codepoints.Length} glyphs at {pixelSize} px do not fit a {largest.Width}x{largest.Height} atlas");
            return false;
        }
    }

    /// <summary>
    /// Bakes a font again for a canvas painted at <paramref name="scale"/>
    /// device pixels per canvas pixel: at <paramref name="pixelSize"/> times
    /// the scale, or, when that does not fit the atlas or the byte room, at
    /// the largest lower quarter step that does. Nothing is baked at a step
    /// of 1 or below -- the font's own bake is that.
    /// </summary>
    /// <param name="bakedScale">The step the bake was made at; 1 when none was.</param>
    /// <param name="shortfall">
    /// Why the bake is not at <paramref name="scale"/> itself: null when it
    /// is, and always set when nothing was baked.
    /// </param>
    internal static bool TryBakeForScale(
        byte[] fontBytes,
        float pixelSize,
        float scale,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        long maximumAtlasBytes,
        [NotNullWhen(true)] out CanvasFontBake? bake,
        out float bakedScale,
        out string? shortfall)
    {
        shortfall = null;
        for (float step = scale; step > 1f; step -= Layout.CanvasPixelScale.Step)
        {
            if (TryBake(fontBytes, pixelSize * step, ranges, maximumGlyphs, out bake, out string? failure, maximumAtlasBytes))
            {
                bakedScale = step;
                return true;
            }
            shortfall ??= failure;
        }
        shortfall ??= "the canvas is painted at one pixel per pixel";
        bake = null;
        bakedScale = 1f;
        return false;
    }

    /// <summary>
    /// The area the packer's rectangles take: each glyph's box at this scale
    /// plus the one pixel of padding the packer puts after it, as
    /// <c>stbtt_PackFontRangesGatherRects</c> measures them. No atlas
    /// smaller than this can hold them.
    /// </summary>
    private static unsafe long PackedArea(StbTrueType.stbtt_fontinfo info, int[] codepoints, float scale)
    {
        long area = 0;
        foreach (int codepoint in codepoints)
        {
            int x0, y0, x1, y1;
            StbTrueType.stbtt_GetGlyphBitmapBoxSubpixel(
                info, StbTrueType.stbtt_FindGlyphIndex(info, codepoint), scale, scale, 0f, 0f, &x0, &y0, &x1, &y1);
            area += (long)(x1 - x0 + 1) * (y1 - y0 + 1);
        }
        return area;
    }

    /// <summary>
    /// StbTrueTypeSharp does no bounds checking, so the sfnt header, table
    /// directory and every table's extent are checked here before the native
    /// parser sees the bytes. Residual risk: malformed outline data inside
    /// valid table bounds still reaches the native parser.
    /// </summary>
    private static bool TryValidateSfnt(byte[] bytes, [NotNullWhen(false)] out string? failure)
    {
        const string NotAFont = "the file is not a TrueType or OpenType font";
        failure = NotAFont;
        if (bytes.Length < 12)
            return false;
        uint version = ReadU32(bytes, 0);
        if (version == 0x74746366) // 'ttcf'
        {
            failure = "font collections are not supported";
            return false;
        }
        if (version != 0x00010000 && version != 0x4F54544F && version != 0x74727565)
            return false;
        int numTables = (bytes[4] << 8) | bytes[5];
        if (numTables < 1 || 12L + 16L * numTables > bytes.Length)
            return false;
        var tags = new HashSet<uint>();
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + 16 * i;
            long offset = ReadU32(bytes, record + 8);
            long length = ReadU32(bytes, record + 12);
            if (offset + length > bytes.Length)
                return false;
            tags.Add(ReadU32(bytes, record));
        }
        foreach (string required in new[] { "cmap", "head", "hhea", "hmtx" })
        {
            if (!tags.Contains(Tag(required)))
            {
                failure = $"the font is missing its '{required}' table";
                return false;
            }
        }
        if (!tags.Contains(Tag("CFF ")) && !(tags.Contains(Tag("glyf")) && tags.Contains(Tag("loca"))))
        {
            failure = "the font is missing its outlines ('glyf' and 'loca', or 'CFF ')";
            return false;
        }
        failure = null;
        return true;
    }

    private static uint ReadU32(byte[] b, int at) =>
        ((uint)b[at] << 24) | ((uint)b[at + 1] << 16) | ((uint)b[at + 2] << 8) | b[at + 3];

    private static uint Tag(string tag) => ReadU32(System.Text.Encoding.ASCII.GetBytes(tag), 0);

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
            failure = string.Create(
                CultureInfo.InvariantCulture,
                $"the font has {present.Count} of the requested characters; the most a font may prepare is {maximumGlyphs}");
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
