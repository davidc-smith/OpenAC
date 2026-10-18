using System.Linq;
using System.Text;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A canvas font is baked once, when asked for, into a single-channel atlas
/// holding only the glyphs the font has among the characters asked for.
/// </summary>
public sealed class CanvasFontBakerTests
{
    private static readonly byte[] Noto = BundledUiFont.ReadEmbeddedFontBytes();

    internal static byte[] CffFixture()
    {
        using Stream stream = typeof(CanvasFontBakerTests).Assembly.GetManifestResourceStream(
            "AcDream.Tests.Fixtures.Fonts.NotoSansCffFixture.otf")
            ?? throw new InvalidOperationException("The CFF font fixture is not embedded.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    [Theory]
    [InlineData(6f)]
    [InlineData(16f)]
    [InlineData(64f)]
    public void NotoBakesTheDefaultCharactersIntoOneAtlas(float size)
    {
        Assert.True(CanvasFontBaker.TryBake(
            Noto, size, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out string? failure), failure);

        Assert.InRange(bake.AtlasWidth, 1, CanvasFontBaker.MaximumAtlasSide);
        Assert.InRange(bake.AtlasHeight, 1, CanvasFontBaker.MaximumAtlasSide);
        Assert.Equal(bake.AtlasWidth * bake.AtlasHeight, bake.Coverage.Length);
        Assert.Contains(bake.Coverage, value => value > 0 && value < 255);
        Assert.True(bake.LineHeight >= size);
        Assert.InRange(bake.Ascent, 1f, bake.LineHeight);
        foreach (Rune rune in "BuffProfile_Banes åäö Ω Ж —".EnumerateRunes())
        {
            CanvasGlyph glyph = bake.Glyphs[rune.Value];
            Assert.True(glyph.Advance > 0f);
            Assert.InRange(glyph.U1, 0f, 1f);
            Assert.InRange(glyph.V1, 0f, 1f);
        }
    }

    [Fact]
    public void SixteenPixelsFitsASmallAtlas()
    {
        Assert.True(CanvasFontBaker.TryBake(Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out _));

        Assert.Equal((512, 256), (bake.AtlasWidth, bake.AtlasHeight));
    }

    [Fact]
    public void ACustomRangeBakesOnlyThoseCharacters()
    {
        Assert.True(CanvasFontBaker.TryBake(Noto, 16f, [('A', 'Z')], 2048, out CanvasFontBake? bake, out _));

        Assert.Equal(26, bake.Glyphs.Count);
        Assert.All(bake.Glyphs.Keys, codepoint => Assert.InRange(codepoint, 'A', 'Z'));
    }

    [Fact]
    public void ARangeTheFontHasNothingInIsRefused()
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, [(0xE000, 0xE0FF)], 2048, out _, out string? failure));

        Assert.Contains("none of the requested characters", failure);
    }

    [Fact]
    public void MoreGlyphsThanTheCapIsRefused()
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, CanvasFontBaker.DefaultRanges, 10, out _, out string? failure));

        Assert.Contains("the most a font may prepare is 10", failure);
    }

    [Theory]
    [InlineData(0x5A, 0x41)]
    [InlineData(0x10FFFF, 0x110000)]
    [InlineData(-1, 0x41)]
    public void ARangeThatIsNotOneIsRefused(int first, int last)
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, [(first, last)], 2048, out _, out string? failure));

        Assert.Contains("is not a range of code points", failure);
    }

    [Fact]
    public void ScanningPastTheCodepointCeilingIsRefused()
    {
        Assert.False(CanvasFontBaker.TryBake(Noto, 16f, [(0, 0x10000)], 2048, out _, out string? failure));

        Assert.Contains("more than 65,536 code points", failure);
    }

    [Fact]
    public void BytesThatAreNotAFontAreRefusedWithoutThrowing()
    {
        Assert.False(CanvasFontBaker.TryBake(new byte[64], 16f, CanvasFontBaker.DefaultRanges, 2048, out _, out string? failure));

        Assert.Contains("not a TrueType or OpenType font", failure);
    }

    [Fact]
    public void AnOpenTypeCffFontBakes()
    {
        byte[] otf = CffFixture();
        Assert.Equal("OTTO"u8.ToArray(), otf[..4]);

        Assert.True(CanvasFontBaker.TryBake(otf, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out string? failure), failure);

        Assert.Equal(['?', 'A', 'T', 'V', 'o'], bake.Glyphs.Keys.Order().Select(codepoint => (char)codepoint).ToArray());
        Assert.All(bake.Glyphs.Values, glyph => Assert.True(glyph.Width > 0f && glyph.Height > 0f));
    }

    private static byte[] Header(string tag, int tables, int bytes = 12)
    {
        byte[] data = new byte[bytes];
        System.Text.Encoding.ASCII.GetBytes(tag).CopyTo(data, 0);
        data[4] = (byte)(tables >> 8);
        data[5] = (byte)tables;
        return data;
    }

    [Fact]
    public void StructurallyBrokenFontFilesAreRefusedWithoutThrowing()
    {
        byte[] pointsPastEnd = Header("OTTO", 1, 28);
        System.Text.Encoding.ASCII.GetBytes("cmap").CopyTo(pointsPastEnd, 12);
        pointsPastEnd[23] = 0xFF; // offset
        pointsPastEnd[27] = 0x10; // length

        byte[] noCmap = CffFixture();
        int numTables = (noCmap[4] << 8) | noCmap[5];
        int record = Enumerable.Range(0, numTables).Select(i => 12 + 16 * i)
            .First(at => Encoding.ASCII.GetString(noCmap, at, 4) == "cmap");
        Encoding.ASCII.GetBytes("xxxx").CopyTo(noCmap, record);

        foreach (byte[] bytes in new[] { [], new byte[4], Header("OTTO", 200), pointsPastEnd, noCmap })
        {
            Assert.False(CanvasFontBaker.TryBake(bytes, 16f, CanvasFontBaker.DefaultRanges, 2048, out _, out string? failure));
            Assert.False(string.IsNullOrEmpty(failure));
        }
    }

    [Fact]
    public void ABakeKeepsToTheAtlasBytesItIsGiven()
    {
        Assert.True(CanvasFontBaker.TryBake(
            Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out _, maximumAtlasBytes: 512 * 256));
        Assert.Equal((512, 256), (bake.AtlasWidth, bake.AtlasHeight));

        Assert.False(CanvasFontBaker.TryBake(
            Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out _, out string? failure, maximumAtlasBytes: 512 * 256 - 1));
        Assert.Equal("1063 glyphs at 16 px do not fit a 256x256 atlas", failure);

        Assert.False(CanvasFontBaker.TryBake(
            Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out _, out failure, maximumAtlasBytes: 1000));
        Assert.Equal("no atlas fits in 1,000 bytes", failure);
    }

    [Fact]
    public void ASharperBakeHasTheSameGlyphsAtTheScale()
    {
        Assert.True(CanvasFontBaker.TryBake(Noto, 16f, CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? own, out _));

        Assert.True(CanvasFontBaker.TryBakeForScale(
            Noto, 16f, 2f, CanvasFontBaker.DefaultRanges, 2048, long.MaxValue,
            out CanvasFontBake? sharp, out float baked, out string? shortfall));

        Assert.Equal(2f, baked);
        Assert.Null(shortfall);
        Assert.Equal(32f, sharp.PixelSize);
        Assert.Equal((1024, 512), (sharp.AtlasWidth, sharp.AtlasHeight));
        Assert.Equal(own.Glyphs.Keys.Order(), sharp.Glyphs.Keys.Order());
        // Advances scale exactly, so text laid out from the font's own bake lines up with the sharper glyphs.
        foreach (int codepoint in new[] { 'A', 'V', 'g', 0x416 })
            Assert.Equal(own.Glyphs[codepoint].Advance * 2f, sharp.Glyphs[codepoint].Advance, 3);
    }

    [Fact]
    public void ASharperBakeThatDoesNotFitStepsDownAQuarterAtATime()
    {
        Assert.True(CanvasFontBaker.TryBakeForScale(
            Noto, 64f, 2f, CanvasFontBaker.DefaultRanges, 2048, long.MaxValue,
            out CanvasFontBake? sharp, out float baked, out string? shortfall));

        Assert.Equal(1.75f, baked);
        Assert.Equal(112f, sharp.PixelSize);
        Assert.Equal("1063 glyphs at 128 px do not fit a 2048x2048 atlas", shortfall);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    public void NoSharperBakeIsMadeAtOneOrWhenNoStepFits(float scale)
    {
        long room = scale == 1f ? long.MaxValue : 1000;

        Assert.False(CanvasFontBaker.TryBakeForScale(
            Noto, 16f, scale, CanvasFontBaker.DefaultRanges, 2048, room,
            out _, out float baked, out string? shortfall));

        Assert.Equal(1f, baked);
        Assert.False(string.IsNullOrEmpty(shortfall));
    }
}
