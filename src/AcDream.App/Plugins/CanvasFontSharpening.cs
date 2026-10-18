using AcDream.App.UI;

namespace AcDream.App.Plugins;

/// <summary>
/// Prepares a canvas font for the scale canvases are painted at: bakes its
/// glyphs again at that multiple of its size, or the largest lower step that
/// fits, uploads them, and gives back the bake they replace. At a scale of
/// 1 the sharper bake is let go. The font's own bake, its metrics and every
/// handle to it stay as they were.
/// </summary>
internal static class CanvasFontSharpening
{
    /// <param name="font">The font to prepare.</param>
    /// <param name="scale">The canvas scale.</param>
    /// <param name="ranges">The characters the font was baked with.</param>
    /// <param name="maximumGlyphs">The glyph ceiling the font was baked under.</param>
    /// <param name="maximumAtlasBytes">The largest sharper atlas the font may have.</param>
    /// <param name="backend">Where atlases are uploaded and given back.</param>
    /// <param name="debugName">The font's atlas name; the step is appended.</param>
    /// <param name="shortfall">Why the font is not drawn at <paramref name="scale"/> itself, or null.</param>
    /// <returns>How many bytes of sharper atlas the font holds now less what it held before.</returns>
    internal static long Prepare(
        CanvasFont font,
        float scale,
        IReadOnlyList<(int First, int Last)> ranges,
        int maximumGlyphs,
        long maximumAtlasBytes,
        IPluginFontBackend backend,
        string debugName,
        out string? shortfall)
    {
        long before = font.Sharp?.AtlasBytes ?? 0L;
        CanvasSharpGlyphs? next = null;
        shortfall = null;
        if (scale > 1f)
        {
            if (CanvasFontBaker.TryBakeForScale(
                    font.Face.Bytes, font.PixelSize, scale, ranges, maximumGlyphs, maximumAtlasBytes,
                    out CanvasFontBake? bake, out float baked, out shortfall))
            {
                uint texture = backend.UploadCoverage(
                    bake.Coverage, bake.AtlasWidth, bake.AtlasHeight,
                    FormattableString.Invariant($"{debugName}-x{baked}"));
                next = new CanvasSharpGlyphs(texture, baked, bake.Glyphs, (long)bake.AtlasWidth * bake.AtlasHeight);
            }
        }
        CanvasSharpGlyphs? replaced = font.PrepareSharp(scale, next);
        if (replaced is not null)
            backend.ReleaseCoverage(replaced.AtlasTexture);
        return (next?.AtlasBytes ?? 0L) - before;
    }

    /// <summary>Gives back a font's sharper bake, when it has one; the font's own atlas is the caller's.</summary>
    /// <returns>The bytes of sharper atlas let go.</returns>
    internal static long Release(CanvasFont font, IPluginFontBackend backend)
    {
        CanvasSharpGlyphs? replaced = font.PrepareSharp(1f, null);
        if (replaced is null) return 0L;
        backend.ReleaseCoverage(replaced.AtlasTexture);
        return replaced.AtlasBytes;
    }
}
