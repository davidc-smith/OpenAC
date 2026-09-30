using System.Text;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// A baked font a canvas draws text with: the glyph metrics, the atlas
/// texture they index, and the font's kerning. It keeps its own copy of the
/// font data for kerning lookups; disposing it lets that go. The atlas
/// texture belongs to whoever uploaded it.
/// </summary>
internal sealed class CanvasFont : IDisposable
{
    private const int KerningCacheLimit = 4096;

    private readonly IReadOnlyDictionary<int, CanvasGlyph> _glyphs;
    private readonly float _scale;
    private readonly Dictionary<(int Left, int Right), float> _kerningCache = [];
    private StbTrueType.stbtt_fontinfo? _kerning;

    /// <param name="bake">The bake of <paramref name="fontBytes"/>.</param>
    /// <param name="atlasTexture">The uploaded atlas texture the glyphs index.</param>
    /// <param name="fontBytes">
    /// Font bytes that have already passed <see cref="CanvasFontBaker.TryBake"/>:
    /// the bake validates the sfnt structure, and this constructor hands the
    /// same bytes to the native parser for kerning.
    /// </param>
    internal CanvasFont(CanvasFontBake bake, uint atlasTexture, byte[] fontBytes)
    {
        ArgumentNullException.ThrowIfNull(bake);
        ArgumentNullException.ThrowIfNull(fontBytes);
        AtlasTexture = atlasTexture;
        PixelSize = bake.PixelSize;
        LineHeight = bake.LineHeight;
        Ascent = bake.Ascent;
        _glyphs = bake.Glyphs;
        _scale = bake.Scale;
        _kerning = StbTrueType.CreateFont(fontBytes, 0);
    }

    internal uint AtlasTexture { get; }

    internal float PixelSize { get; }

    internal float LineHeight { get; }

    internal float Ascent { get; }

    /// <summary>
    /// The glyph for a code point, or the font's <c>?</c> when it lacks one
    /// and <c>?</c> was baked. Control characters have no glyph.
    /// </summary>
    internal bool TryGetGlyph(int codepoint, out CanvasGlyph glyph)
    {
        if (codepoint < 0x20)
        {
            glyph = default;
            return false;
        }
        return _glyphs.TryGetValue(codepoint, out glyph) || _glyphs.TryGetValue('?', out glyph);
    }

    /// <summary>How far the pen moves between two glyphs beyond their advances, in canvas pixels.</summary>
    internal float Kerning(int leftGlyphIndex, int rightGlyphIndex)
    {
        if (_kerning is null) return 0f;
        if (_kerningCache.TryGetValue((leftGlyphIndex, rightGlyphIndex), out float cached))
            return cached;
        if (_kerningCache.Count >= KerningCacheLimit)
            _kerningCache.Clear();
        float kerning = StbTrueType.stbtt_GetGlyphKernAdvance(_kerning, leftGlyphIndex, rightGlyphIndex) * _scale;
        _kerningCache[(leftGlyphIndex, rightGlyphIndex)] = kerning;
        return kerning;
    }

    /// <summary>The pen's travel over a line of text: advances plus kerning, exactly as it is drawn.</summary>
    internal float MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        float pen = 0f;
        int previous = -1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!TryGetGlyph(rune.Value, out CanvasGlyph glyph))
            {
                previous = -1;
                continue;
            }
            if (previous >= 0)
                pen += Kerning(previous, glyph.GlyphIndex);
            pen += glyph.Advance;
            previous = glyph.GlyphIndex;
        }
        return pen;
    }

    public void Dispose()
    {
        _kerning?.Dispose();
        _kerning = null;
    }
}
