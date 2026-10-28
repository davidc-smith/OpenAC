using System.Numerics;
using AcDream.App.UI;
using DatReaderWriter.Types;   // FontCharDesc

namespace AcDream.App.Tests.UI;

public sealed class UiTextSpacingTests
{
    private static readonly Vector4 First = new(1f, 0f, 0f, 1f);
    private static readonly Vector4 Second = new(0f, 1f, 0f, 1f);

    private static UiDatFont Font()
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        foreach (char c in "ABCDEFGH") glyphs[c] = new FontCharDesc { Unicode = c, Width = 6, Height = 8 };
        return new UiDatFont(fgTex: 1u, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f, glyphs);
    }

    /// <summary>Glyph quads are textured, which MarkupDrawProbe.QuadBounds skips.</summary>
    private static List<(float X, float Y)> GlyphBounds(AcDream.App.Rendering.TextRenderer renderer, Vector4 color)
    {
        var found = new List<(float, float)>();
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int q = 0; q + 48 <= s.Verts.Count; q += 48)
            {
                if (MathF.Abs(s.Verts[q + 4] - color.X) >= 0.01f || MathF.Abs(s.Verts[q + 5] - color.Y) >= 0.01f
                    || MathF.Abs(s.Verts[q + 6] - color.Z) >= 0.01f || MathF.Abs(s.Verts[q + 7] - color.W) >= 0.01f)
                    continue;
                float minX = float.MaxValue, minY = float.MaxValue;
                for (int v = 0; v < 6; v++)
                {
                    minX = MathF.Min(minX, s.Verts[q + v * 8]);
                    minY = MathF.Min(minY, s.Verts[q + v * 8 + 1]);
                }
                found.Add((minX, minY));
            }
        return found;
    }

    private static UiText Text(float spacing, float indent) => new()
    {
        Width = 200, Height = 100, DatFont = Font(), Padding = 0f, LineSpacing = spacing,
        LinesProvider = () => [new UiText.Line("ABC", First), new UiText.Line("DEF", Second, indent)],
    };

    [Fact]
    public void LineSpacing_SpreadsLines_AndCentresGlyphs()
    {
        var renderer = MarkupDrawProbe.Draw(Text(1.5f, 0f), 200, 100);
        float y1 = GlyphBounds(renderer, First).Min(b => b.Y);
        float y2 = GlyphBounds(renderer, Second).Min(b => b.Y);
        Assert.Equal(24f, y2 - y1, 2);   // 16 px font × 1.5
    }

    [Fact]
    public void Indent_MovesOnlyThatLine()
    {
        var plain = MarkupDrawProbe.Draw(Text(1f, 0f), 200, 100);
        var indented = MarkupDrawProbe.Draw(Text(1f, 16f), 200, 100);
        Assert.Equal(GlyphBounds(plain, First).Min(b => b.X),
            GlyphBounds(indented, First).Min(b => b.X), 2);
        Assert.Equal(GlyphBounds(plain, Second).Min(b => b.X) + 16f,
            GlyphBounds(indented, Second).Min(b => b.X), 2);
    }

    [Fact]
    public void DefaultSpacing_IsTheFontsLineHeight()
    {
        var renderer = MarkupDrawProbe.Draw(Text(1f, 0f), 200, 100);
        float y1 = GlyphBounds(renderer, First).Min(b => b.Y);
        float y2 = GlyphBounds(renderer, Second).Min(b => b.Y);
        Assert.Equal(16f, y2 - y1, 2);
    }
}
