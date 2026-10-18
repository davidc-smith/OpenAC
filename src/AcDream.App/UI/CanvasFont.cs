using System.Text;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// One font file as a canvas font keeps it: the bytes, which a sharper
/// bake is made from, and the native parser's copy, which kerning is looked
/// up in. Every size baked from the same file can share one face.
/// </summary>
internal sealed class CanvasFontFace : IDisposable
{
    private StbTrueType.stbtt_fontinfo? _info;

    /// <param name="bytes">
    /// Font bytes that have already passed <see cref="CanvasFontBaker.TryBake"/>:
    /// the bake validates the sfnt structure, and the native parser is
    /// handed the same bytes here.
    /// </param>
    internal CanvasFontFace(byte[] bytes)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        _info = StbTrueType.CreateFont(bytes, 0);
    }

    internal byte[] Bytes { get; }

    /// <summary>The parser's view of the file; null once disposed, or when the parser refused it.</summary>
    internal StbTrueType.stbtt_fontinfo? Info => _info;

    public void Dispose()
    {
        _info?.Dispose();
        _info = null;
    }
}

/// <summary>
/// A canvas font's glyphs baked again at <see cref="Scale"/> times its size,
/// for a canvas painted at that many device pixels per canvas pixel. The
/// boxes are in device pixels at that scale; the font's own bake still sets
/// every advance, kerning and line metric, so text measures the same at
/// every scale.
/// </summary>
internal sealed record CanvasSharpGlyphs(
    uint AtlasTexture,
    float Scale,
    IReadOnlyDictionary<int, CanvasGlyph> Glyphs,
    long AtlasBytes)
{
    /// <summary>The glyph for a code point, falling back to <c>?</c> exactly as the font's own bake does.</summary>
    internal bool TryGetGlyph(int codepoint, out CanvasGlyph glyph) =>
        Glyphs.TryGetValue(codepoint, out glyph) || Glyphs.TryGetValue('?', out glyph);
}

/// <summary>
/// A baked font a canvas draws text with: the glyph metrics, the atlas
/// texture they index, and the font's kerning, looked up in its
/// <see cref="CanvasFontFace"/>. The atlas textures -- its own and a sharper
/// bake's -- belong to whoever uploaded them.
/// </summary>
internal sealed class CanvasFont : IDisposable
{
    private const int KerningCacheLimit = 4096;

    private readonly IReadOnlyDictionary<int, CanvasGlyph> _glyphs;
    private readonly float _scale;
    private readonly Dictionary<(int Left, int Right), float> _kerningCache = [];
    private readonly bool _ownsFace;
    private bool _disposed;

    /// <param name="bake">The bake of <paramref name="fontBytes"/>.</param>
    /// <param name="atlasTexture">The uploaded atlas texture the glyphs index.</param>
    /// <param name="fontBytes">
    /// Font bytes that have already passed <see cref="CanvasFontBaker.TryBake"/>.
    /// The font keeps them, in a face of its own, for kerning and for a
    /// sharper bake.
    /// </param>
    internal CanvasFont(CanvasFontBake bake, uint atlasTexture, byte[] fontBytes)
        : this(bake, atlasTexture, new CanvasFontFace(fontBytes ?? throw new ArgumentNullException(nameof(fontBytes))), ownsFace: true)
    {
    }

    /// <param name="bake">The bake of the face's bytes.</param>
    /// <param name="atlasTexture">The uploaded atlas texture the glyphs index.</param>
    /// <param name="face">The file the bake was made from.</param>
    /// <param name="ownsFace">Whether disposing the font disposes the face; false for a face shared between sizes.</param>
    internal CanvasFont(CanvasFontBake bake, uint atlasTexture, CanvasFontFace face, bool ownsFace)
    {
        ArgumentNullException.ThrowIfNull(bake);
        Face = face ?? throw new ArgumentNullException(nameof(face));
        _ownsFace = ownsFace;
        AtlasTexture = atlasTexture;
        PixelSize = bake.PixelSize;
        LineHeight = bake.LineHeight;
        Ascent = bake.Ascent;
        _glyphs = bake.Glyphs;
        _scale = bake.Scale;
    }

    internal CanvasFontFace Face { get; }

    internal uint AtlasTexture { get; }

    internal float PixelSize { get; }

    internal float LineHeight { get; }

    internal float Ascent { get; }

    /// <summary>The sharper bake text is drawn from on a scaled canvas, or null to draw from the font's own.</summary>
    internal CanvasSharpGlyphs? Sharp { get; private set; }

    /// <summary>
    /// The canvas scale <see cref="Sharp"/> was last prepared for. It is
    /// kept even when no sharper bake could be made, or only a lower step,
    /// so the same scale is not tried again on every paint.
    /// </summary>
    internal float PreparedScale { get; private set; } = 1f;

    /// <summary>Records what was prepared for a scale and hands back the bake it replaces, for its texture to be given back.</summary>
    internal CanvasSharpGlyphs? PrepareSharp(float scale, CanvasSharpGlyphs? sharp)
    {
        CanvasSharpGlyphs? previous = Sharp;
        Sharp = sharp;
        PreparedScale = scale;
        return previous;
    }

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
        if (_disposed || Face.Info is not { } info) return 0f;
        if (_kerningCache.TryGetValue((leftGlyphIndex, rightGlyphIndex), out float cached))
            return cached;
        if (_kerningCache.Count >= KerningCacheLimit)
            _kerningCache.Clear();
        float kerning = StbTrueType.stbtt_GetGlyphKernAdvance(info, leftGlyphIndex, rightGlyphIndex) * _scale;
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
        if (_disposed) return;
        _disposed = true;
        if (_ownsFace)
            Face.Dispose();
    }
}
