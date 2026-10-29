using AcDream.App.UI;
using AcDream.App.UI.Hud;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>
/// Deck fonts for tests: a face's line is its pixel height plus two, and every fitted face is recorded.
/// Like the real baker, a pixel height outside the bundled font's range throws.
/// </summary>
internal static class FakeDeckFonts
{
    public static float Line(float pixels) => pixels + 2f;

    public static DeckFonts Make(List<DeckFit>? fits = null, List<(float Px, BundledUiFontWeight Weight)>? baked = null)
    {
        var fonts = new DeckFonts((px, weight) =>
        {
            if (!float.IsFinite(px) || px < BundledUiFont.MinimumPixelHeight || px > BundledUiFont.MaximumPixelHeight)
                throw new ArgumentOutOfRangeException(nameof(px), $"Font size {px} is outside the bundled font's range.");
            baked?.Add((px, weight));
            return Font(px);
        });
        if (fits is not null) fonts.Fitted = fits.Add;
        return fonts;
    }

    public static UiDatFont Font(float pixels)
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        byte w = (byte)Math.Max(1, (int)MathF.Round(pixels * 0.6f)), h = (byte)Math.Max(1, (int)pixels);
        for (char c = ' '; c <= '~'; c++)
            glyphs[c] = new FontCharDesc { Unicode = c, Width = w, Height = h };
        return new UiDatFont(fgTex: 1u, fgW: 256, fgH: 256, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: Line(pixels), baselineOffset: pixels, glyphs);
    }

    /// <summary>
    /// Every fitted face's line is no taller than its box, or than the deck's own (factor 1) face where the box was already smaller.
    /// This guards callers that draw a face without going through <see cref="DeckFonts.FitFace"/>
    /// (FitFace itself guarantees the bound).
    /// </summary>
    public static void AssertAllFit(IEnumerable<DeckFit> fits)
    {
        foreach (DeckFit f in fits)
        {
            float own = Line(DeckFonts.PixelHeight(f.BasePixels, f.Scale));
            Assert.True(f.LineHeight <= MathF.Max(f.BoxHeight, own) + 0.01f, $"{f} is taller than its box");
        }
    }
}
