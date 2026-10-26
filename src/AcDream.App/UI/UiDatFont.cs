using System;
using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.Content;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace AcDream.App.UI;

public sealed class UiDatFont
{
    public const uint DefaultFontId = 0x40000000u;

    /// <summary>Foreground (glyph pixels) GL texture handle + atlas pixel size.</summary>
    public uint ForegroundTexture => Atlases.Foreground.Texture;
    public int ForegroundWidth => Atlases.Foreground.Width;
    public int ForegroundHeight => Atlases.Foreground.Height;

    /// <summary>Background (outline/shadow) GL texture handle + atlas pixel size.
    /// 0 when the font has no background atlas (then the outline pass is skipped).</summary>
    public uint BackgroundTexture => Atlases.Background.Texture;
    public int BackgroundWidth => Atlases.Background.Width;
    public int BackgroundHeight => Atlases.Background.Height;

    /// <summary>One uploaded glyph atlas: its texture handle and pixel size.</summary>
    internal readonly record struct Atlas(uint Texture, int Width, int Height);

    private Func<(Atlas Foreground, Atlas Background)>? _uploadAtlases;
    private (Atlas Foreground, Atlas Background) _atlases;

    /// <summary>
    /// The glyph atlases, uploaded the first time text in this font is
    /// drawn: a font that is only measured, or never shown, costs no
    /// texture.
    /// </summary>
    private (Atlas Foreground, Atlas Background) Atlases
    {
        get
        {
            if (_uploadAtlases is { } upload)
            {
                _atlases = upload();
                _uploadAtlases = null;
            }
            return _atlases;
        }
    }

    public float LineHeight { get; }

    public float BaselineOffset { get; }

    public int BorderX { get; }
    public int BorderY { get; }

    /// <summary>
    /// Bakes this font again for a display with the given device pixels per
    /// point, or gives null when it would be no sharper there. Only bundled
    /// fonts have one; dat fonts never do.
    /// </summary>
    internal Func<float, UiDatFontSharp?>? SharpSource { get; set; }

    private readonly Dictionary<float, UiDatFontSharp?> _sharp = new();

    /// <summary>
    /// The same font baked at a multiple of its size, whose glyphs are drawn
    /// in its place on a display with <paramref name="pixelScale"/> device
    /// pixels per point. Baked the first time a scale asks for it. Every
    /// measurement, and so all layout, still comes from this font.
    /// </summary>
    internal UiDatFontSharp? SharpFor(float pixelScale)
    {
        if (SharpSource is null || !(pixelScale > 1f)) return null;
        if (!_sharp.TryGetValue(pixelScale, out UiDatFontSharp? sharp))
            _sharp[pixelScale] = sharp = SharpSource(pixelScale);
        return sharp;
    }

    private readonly Dictionary<char, FontCharDesc> _glyphs;
    private readonly char? _fallbackCharacter;

    internal UiDatFont(
        uint fgTex, int fgW, int fgH,
        uint bgTex, int bgW, int bgH,
        float lineHeight, float baselineOffset,
        Dictionary<char, FontCharDesc> glyphs,
        int borderX = 0, int borderY = 0, char? fallbackCharacter = null)
        : this(lineHeight, baselineOffset, glyphs, borderX, borderY, fallbackCharacter)
    {
        _atlases = (new Atlas(fgTex, fgW, fgH), new Atlas(bgTex, bgW, bgH));
    }

    /// <summary>A font whose atlases <paramref name="uploadAtlases"/> uploads on first draw.</summary>
    internal UiDatFont(
        Func<(Atlas Foreground, Atlas Background)> uploadAtlases,
        float lineHeight, float baselineOffset,
        Dictionary<char, FontCharDesc> glyphs,
        int borderX = 0, int borderY = 0)
        : this(lineHeight, baselineOffset, glyphs, borderX, borderY, fallbackCharacter: null)
    {
        _uploadAtlases = uploadAtlases ?? throw new ArgumentNullException(nameof(uploadAtlases));
    }

    private UiDatFont(
        float lineHeight, float baselineOffset,
        Dictionary<char, FontCharDesc> glyphs,
        int borderX, int borderY, char? fallbackCharacter)
    {
        LineHeight = lineHeight;
        BaselineOffset = baselineOffset;
        _glyphs = glyphs;
        _fallbackCharacter = fallbackCharacter;
        BorderX = borderX;
        BorderY = borderY;
    }

    public bool HasBackground => BackgroundTexture != 0;

    public bool TryGetGlyph(char c, out FontCharDesc glyph)
        => _glyphs.TryGetValue(c, out glyph!) || (!char.IsControl(c)
            && _fallbackCharacter is { } fallback && _glyphs.TryGetValue(fallback, out glyph!));

    /// <summary>
    /// Reads font <paramref name="fontId"/>'s metrics now; its atlases are
    /// read and uploaded on first draw, under <paramref name="datLock"/>
    /// when one is given (the caller holds it for this read).
    /// </summary>
    public static UiDatFont? Load(
        IDatReaderWriter dats,
        TextureCache cache,
        uint fontId = DefaultFontId,
        object? datLock = null)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(cache);

        if (!dats.TryGet<Font>(fontId, out var font) || font is null)
            return null;

        // Foreground atlas is required; without it there are no glyph pixels.
        if (font.ForegroundSurfaceDataId == 0)
            return null;

        uint foregroundId = font.ForegroundSurfaceDataId;
        uint backgroundId = font.BackgroundSurfaceDataId;
        (Atlas, Atlas) UploadAtlases()
        {
            if (datLock is null)
                return Upload();
            lock (datLock)
                return Upload();
        }
        (Atlas, Atlas) Upload()
        {
            uint fgTex = cache.GetOrUploadRenderSurface(foregroundId, out int fgW, out int fgH, nearest: true);
            uint bgTex = 0; int bgW = 0, bgH = 0;
            if (backgroundId != 0)
                bgTex = cache.GetOrUploadRenderSurface(backgroundId, out bgW, out bgH, nearest: true);
            return (new Atlas(fgTex, fgW, fgH), new Atlas(bgTex, bgW, bgH));
        }

        var glyphs = new Dictionary<char, FontCharDesc>(font.CharDescs.Count);
        foreach (var cd in font.CharDescs)
            glyphs[(char)cd.Unicode] = cd;

        return new UiDatFont(
            UploadAtlases,
            lineHeight: font.MaxCharHeight,
            baselineOffset: font.BaselineOffset,
            glyphs,
            borderX: (int)font.NumHorizontalBorderPixels,
            borderY: (int)font.NumVerticalBorderPixels);
    }

    public float MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;

        float width = 0f;
        for (int index = 0; index < text.Length; index++)
        {
            if (TryGetGlyph(text[index], out FontCharDesc glyph))
                width += GlyphAdvance(glyph);
        }

        return width;
    }

    public static float MeasureWidth(string? text, Func<char, FontCharDesc?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (string.IsNullOrEmpty(text)) return 0f;
        float w = 0f;
        for (int i = 0; i < text.Length; i++)
            if (lookup(text[i]) is { } g)
                w += GlyphAdvance(g);
        return w;
    }

    public static float GlyphAdvance(FontCharDesc g)
        => g.HorizontalOffsetBefore + g.Width + g.HorizontalOffsetAfter;
}

/// <summary>A sharper bake of a font and how many times its size it was baked at.</summary>
internal sealed record UiDatFontSharp(UiDatFont Font, float Scale);
