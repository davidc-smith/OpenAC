using System.Numerics;
using AcDream.App.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// The contract painter over one canvas repaint. Bound to the canvas
/// surface's drawing context for the duration of the plugin's paint
/// callback and unbound the moment it returns, so a painter the plugin
/// kept throws on the next call instead of drawing into whatever the
/// context is doing then.
///
/// <para>Coordinates come in as canvas pixels and go out unchanged: the
/// surface's context is begun at the canvas's own size, with its origin at
/// the canvas's top-left corner, and the canvas rectangle is already the
/// clip in force.</para>
/// </summary>
internal sealed class PluginPainter : IPluginPainter
{
    // The canvas surface paints at one screen pixel per canvas pixel, so
    // that is the scale image regions are pulled in at. HiDPI work passes
    // its pixel scale here.
    private const double DevicePixelsPerPixel = 1.0;

    private UiRenderContext? _context;
    private UiDatFont? _font;
    private PluginImages? _images;
    private int _width;
    private int _height;

    /// <summary>Points the painter at one repaint. Only the surface calls this.</summary>
    internal void Bind(UiRenderContext context, UiDatFont? font, PluginImages? images, int width, int height)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _font = font;
        _images = images;
        _width = width;
        _height = height;
    }

    /// <summary>Forgets the repaint; every call after this throws.</summary>
    internal void Unbind()
    {
        _context = null;
        _font = null;
        _images = null;
    }

    internal bool IsBound => _context is not null;

    public int Width => _width;

    public int Height => _height;

    public void Clear(PluginColor color) =>
        Context.DrawFill(0f, 0f, _width, _height, ToVector(color));

    public void FillRect(PluginRect rect, PluginColor color) =>
        Context.DrawFill((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, ToVector(color));

    public void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f) =>
        Context.DrawRectOutline(
            (float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, ToVector(color), thickness);

    public void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f) =>
        Context.DrawLine((float)from.X, (float)from.Y, (float)to.X, (float)to.Y, ToVector(color), thickness);

    public void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false)
    {
        UiRenderContext context = Context;
        if (_font is null || string.IsNullOrEmpty(text)) return;
        context.DrawStringDat(_font, text, (float)position.X, (float)position.Y, ToVector(color), outline);
    }

    public PluginSize MeasureText(string text)
    {
        _ = Context;
        if (_font is null || string.IsNullOrEmpty(text)) return default;
        return new PluginSize(_font.MeasureWidth(text), _font.LineHeight);
    }

    public void DrawImage(PluginImage image, PluginRect destination, PluginColor tint)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture)) return;
        context.DrawSprite(
            texture,
            (float)destination.X, (float)destination.Y, (float)destination.Width, (float)destination.Height,
            0f, 0f, 1f, 1f,
            ToVector(tint));
    }

    public void DrawImageTransformed(
        PluginImage image,
        PluginRect destination,
        PluginColor tint,
        double rotationRadians,
        PluginPoint pivot,
        double scaleX = 1.0,
        double scaleY = 1.0)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture)) return;
        context.DrawSpriteTransformed(
            texture,
            (float)destination.X, (float)destination.Y, (float)destination.Width, (float)destination.Height,
            0f, 0f, 1f, 1f,
            ToVector(tint),
            (float)rotationRadians,
            new Vector2((float)scaleX, (float)scaleY),
            new Vector2((float)pivot.X, (float)pivot.Y));
    }

    public void DrawImageRegion(PluginImage image, PluginRect source, PluginRect destination, PluginColor tint)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture, out int width, out int height, out bool linear)) return;
        if (!CanvasImageRegions.TryMapRegion(
                width, height, linear, source, destination, DevicePixelsPerPixel, exactPull: false, out CanvasImagePiece piece))
            return;
        context.DrawSprite(
            texture,
            piece.X, piece.Y, piece.Width, piece.Height,
            piece.U0, piece.V0, piece.U1, piece.V1,
            ToVector(tint));
    }

    public void DrawImageRegionTransformed(
        PluginImage image,
        PluginRect source,
        PluginRect destination,
        PluginColor tint,
        double rotationRadians,
        PluginPoint pivot,
        double scaleX = 1.0,
        double scaleY = 1.0)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture, out int width, out int height, out bool linear)) return;
        // Once turned or scaled no edge is on a whole pixel, so the region
        // is pulled in by the full half pixel.
        if (!CanvasImageRegions.TryMapRegion(
                width, height, linear, source, destination, DevicePixelsPerPixel, exactPull: true, out CanvasImagePiece piece))
            return;
        // The pivot is measured from the rectangle's corner; a cut source
        // moves that corner, so the pivot is measured from the new one to
        // stay where the plugin put it.
        context.DrawSpriteTransformed(
            texture,
            piece.X, piece.Y, piece.Width, piece.Height,
            piece.U0, piece.V0, piece.U1, piece.V1,
            ToVector(tint),
            (float)rotationRadians,
            new Vector2((float)scaleX, (float)scaleY),
            new Vector2((float)(destination.X + pivot.X) - piece.X, (float)(destination.Y + pivot.Y) - piece.Y));
    }

    public void DrawImageNineSlice(
        PluginImage image,
        PluginRect destination,
        PluginInsets insets,
        PluginColor tint,
        PluginRect? source = null,
        bool drawCenter = true)
    {
        UiRenderContext context = Context;
        if (!TryResolve(image, out uint texture, out int width, out int height, out bool linear)) return;
        Span<CanvasImagePiece> pieces = stackalloc CanvasImagePiece[CanvasImageRegions.MaximumNineSlicePieces];
        int count = CanvasImageRegions.NineSlice(
            width, height, linear, destination, insets, source, drawCenter, DevicePixelsPerPixel, pieces);
        Vector4 color = ToVector(tint);
        foreach (CanvasImagePiece piece in pieces[..count])
        {
            context.DrawSprite(
                texture,
                piece.X, piece.Y, piece.Width, piece.Height,
                piece.U0, piece.V0, piece.U1, piece.V1,
                color);
        }
    }

    public void PushClip(PluginRect rect) =>
        Context.PushClip((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

    public void PopClip() => Context.PopClip();

    private UiRenderContext Context =>
        _context ?? throw new InvalidOperationException(
            "The painter is valid only for the duration of the paint callback it was handed to.");

    private bool TryResolve(PluginImage image, out uint texture) =>
        TryResolve(image, out texture, out _, out _, out _);

    private bool TryResolve(PluginImage image, out uint texture, out int width, out int height, out bool linear)
    {
        texture = 0u;
        width = 0;
        height = 0;
        linear = false;
        return _images is not null
            && _images.TryResolve(image, out texture, out width, out height, out linear)
            && texture != 0u;
    }

    private static Vector4 ToVector(PluginColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}
