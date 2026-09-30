using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>A baked canvas font measures exactly what it draws, kerning included.</summary>
public sealed class CanvasFontTests
{
    private sealed class NullFrames : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }

    private const uint Atlas = 42u;

    private static CanvasFont Bake(byte[] bytes, IReadOnlyList<(int, int)>? ranges = null)
    {
        Assert.True(CanvasFontBaker.TryBake(
            bytes, 16f, ranges ?? CanvasFontBaker.DefaultRanges, 2048, out CanvasFontBake? bake, out string? failure), failure);
        return new CanvasFont(bake, Atlas, bytes);
    }

    public static TheoryData<string> Fonts => new() { "ttf", "otf" };

    private static byte[] Bytes(string kind) =>
        kind == "ttf" ? BundledUiFont.ReadEmbeddedFontBytes() : CanvasFontBakerTests.CffFixture();

    [Theory]
    [MemberData(nameof(Fonts))]
    public void KerningPullsAVTogether(string kind)
    {
        using CanvasFont font = Bake(Bytes(kind));

        Assert.True(font.MeasureWidth("AV") < font.MeasureWidth("A") + font.MeasureWidth("V"));
    }

    [Fact]
    public void AMissingCharacterIsAQuestionMarkWhenOneWasBakedAndNothingOtherwise()
    {
        using CanvasFont full = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        using CanvasFont caps = Bake(BundledUiFont.ReadEmbeddedFontBytes(), [('A', 'Z')]);

        Assert.Equal(full.MeasureWidth("?"), full.MeasureWidth("漢"));
        Assert.Equal(caps.MeasureWidth("A") + caps.MeasureWidth("B"), caps.MeasureWidth("A漢B"));
        Assert.Equal(0f, full.MeasureWidth("\n\t"));
    }

    [Fact]
    public void ASurrogatePairIsOneCharacter()
    {
        using CanvasFont full = Bake(BundledUiFont.ReadEmbeddedFontBytes());

        // U+1F600 is not in the bake: one '?', not two.
        Assert.Equal(full.MeasureWidth("?"), full.MeasureWidth("\U0001F600"));
    }

    [Fact]
    public void DrawingLandsAtTheMeasuredPositionsInOneCoverageRun()
    {
        using CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 50f));
        var context = new UiRenderContext(renderer, new Vector2(200f, 50f));

        context.DrawStringCanvasFont(font, "AV", 10.3f, 5f, Vector4.One);

        Assert.Equal([Atlas], renderer.DebugSpriteSegmentCoverage);
        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        Assert.Equal(12, verts.Count / TextRenderer.FloatsPerVertex);
        Assert.True(font.TryGetGlyph('A', out CanvasGlyph a));
        Assert.True(font.TryGetGlyph('V', out CanvasGlyph v));
        float expectedLeft = MathF.Floor(10.3f + a.Advance + font.Kerning(a.GlyphIndex, v.GlyphIndex) + v.OffsetX + 0.5f);
        float secondLeft = Enumerable.Range(6, 6).Min(i => verts[i * TextRenderer.FloatsPerVertex]);
        Assert.Equal(expectedLeft, secondLeft);
    }

    [Fact]
    public void AnOutlineIsEightCopiesInTheOutlineColourUnderTheFill()
    {
        using CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());
        using var device = new RecordingGpuDevice();
        using var renderer = new TextRenderer(device, new NullFrames(), "unused");
        renderer.Begin(new Vector2(200f, 50f));
        var context = new UiRenderContext(renderer, new Vector2(200f, 50f));
        var red = new Vector4(1f, 0f, 0f, 1f);

        context.DrawStringCanvasFont(font, "A", 10f, 5f, Vector4.One, outline: true, outlineColor: red);

        (uint _, IReadOnlyList<float> verts) = Assert.Single(renderer.DebugSpriteSegmentVerts);
        int vertices = verts.Count / TextRenderer.FloatsPerVertex;
        Assert.Equal(9 * 6, vertices);
        Assert.Equal(1f, verts[4]);
        Assert.Equal(0f, verts[5]);
        int fillStart = 8 * 6 * TextRenderer.FloatsPerVertex;
        Assert.Equal(1f, verts[fillStart + 5]);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        CanvasFont font = Bake(BundledUiFont.ReadEmbeddedFontBytes());

        font.Dispose();
        font.Dispose();
    }
}
