using System.Numerics;
using AcDream.App.UI;
using DatReaderWriter.Types;   // FontCharDesc

namespace AcDream.App.Tests.UI;

public sealed class UiTextLineMarkerTests
{
    private static UiDatFont Font()
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        foreach (char c in "ABCDEFGH") glyphs[c] = new FontCharDesc { Unicode = c, Width = 6, Height = 8 };
        return new UiDatFont(fgTex: 1u, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f, glyphs);
    }

    [Fact]
    public void Marker_IsABarLeftOfTheLine()
    {
        var marker = new Vector4(0.9f, 0.1f, 0.2f, 1f);
        var text = new UiText
        {
            Width = 200, Height = 60, DatFont = Font(), Padding = 2f, MarginLeft = 6f,
            LinesProvider = () => [new UiText.Line("ABC", Vector4.One), new UiText.Line("DEF", Vector4.One)],
            LineMarkerProvider = i => i == 1 ? marker : null,
        };

        var bounds = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(text, 200, 60), marker);

        var (x, _, w, h) = Assert.Single(bounds);
        Assert.Equal(2f + 6f - 4f, x, 2);
        Assert.Equal(2f, w, 2);
        Assert.Equal(16f, h, 2);
    }
}
