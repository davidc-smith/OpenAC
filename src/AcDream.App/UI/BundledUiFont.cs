using AcDream.App.Rendering;
using DatReaderWriter.Types;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>Bundled, platform-independent sans font for any retained client control.</summary>
public static class BundledUiFont
{
    public const float DefaultPixelHeight = 16f;
    private const int AtlasSize = 512;
    private static readonly (int First, int Count)[] Ranges = [(32, 560), (0x370, 448), (0x2000, 112)];

    public static UiDatFont Load(TextureCache textures, float pixelHeight = DefaultPixelHeight)
    {
        ArgumentNullException.ThrowIfNull(textures);
        var atlas = Bake(pixelHeight);
        uint texture = textures.UploadCoverage8(atlas.Coverage, atlas.Width, atlas.Height, nearest: true);
        return atlas.CreateFont(texture);
    }

    /// <summary>The baked glyphs: <see cref="Coverage"/> holds one byte of
    /// glyph coverage per pixel, drawn as white with that alpha.</summary>
    internal sealed record Atlas(byte[] Coverage, int Width, int Height, float LineHeight,
        float Ascent, Dictionary<char, FontCharDesc> Glyphs)
    {
        internal UiDatFont CreateFont(uint texture) => new(texture, Width, Height,
            0, 0, 0, LineHeight, Ascent, Glyphs, fallbackCharacter: '?');
    }

    internal static unsafe Atlas Bake(float pixelHeight = DefaultPixelHeight)
    {
        if (!float.IsFinite(pixelHeight) || pixelHeight < 8 || pixelHeight > 32)
            throw new ArgumentOutOfRangeException(nameof(pixelHeight), "Font size must be between 8 and 32 pixels.");
        using var stream = typeof(BundledUiFont).Assembly.GetManifestResourceStream(
            "AcDream.App.Fonts.NotoSans-Regular.ttf")
            ?? throw new InvalidOperationException("Bundled Noto Sans font is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        byte[] fontBytes = bytes.ToArray();
        using var info = StbTrueType.CreateFont(fontBytes, 0)
            ?? throw new InvalidOperationException("Bundled Noto Sans font could not be read.");
        float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelHeight);
        int ascent, descent, gap;
        StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &gap);
        float baseline = MathF.Round(ascent * scale);
        byte[] atlas = new byte[AtlasSize * AtlasSize * Ranges.Length];
        var coverage = new byte[AtlasSize * AtlasSize];
        var glyphs = new Dictionary<char, FontCharDesc>();
        for (int range = 0; range < Ranges.Length; range++)
        {
            var (first, count) = Ranges[range];
            var baked = new StbTrueType.stbtt_bakedchar[count];
            Array.Clear(coverage);
            if (!StbTrueType.stbtt_BakeFontBitmap(fontBytes, 0, pixelHeight,
                coverage, AtlasSize, AtlasSize, first, count, baked))
                throw new InvalidOperationException("Bundled font atlas does not fit the requested size.");
            coverage.CopyTo(atlas, range * coverage.Length);
            for (int i = 0; i < count; i++)
            {
                int codepoint = first + i;
                if (StbTrueType.stbtt_FindGlyphIndex(info, codepoint) == 0) continue;
                var g = baked[i];
                int width = g.x1 - g.x0;
                int before = (int)MathF.Round(g.xoff);
                glyphs[(char)codepoint] = new FontCharDesc
                {
                    Unicode = (ushort)codepoint,
                    OffsetX = g.x0, OffsetY = (ushort)(g.y0 + range * AtlasSize),
                    Width = checked((byte)width), Height = checked((byte)(g.y1 - g.y0)),
                    HorizontalOffsetBefore = checked((sbyte)before),
                    HorizontalOffsetAfter = checked((sbyte)((int)MathF.Round(g.xadvance) - width - before)),
                    VerticalOffsetBefore = checked((sbyte)(baseline + MathF.Round(g.yoff))),
                };
            }
        }
        return new Atlas(atlas, AtlasSize, AtlasSize * Ranges.Length,
            MathF.Ceiling((ascent - descent + gap) * scale), baseline, glyphs);
    }
}


