using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>What went wrong with a shape, for the canvas to report once per kind.</summary>
internal enum CanvasShapeProblem
{
    /// <summary>A shape was given input it cannot draw: not finite, negative, a wrong count, not convex.</summary>
    InvalidInput,

    /// <summary>A paint drew more shape vertices than one paint may.</summary>
    VertexBudget,
}

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
/// clip in force. The pixel scale the surface binds is how many target
/// pixels each canvas pixel covers; only what is measured in device pixels
/// -- a shape's fringe, a curve's chords -- reads it.</para>
///
/// <para>Shapes are tessellated here into anti-aliased triangles
/// (<see cref="CanvasGeometry"/>). Input they cannot draw draws nothing
/// and is handed to the problem sink the surface binds, never thrown: a
/// throw would drop the whole canvas.</para>
/// </summary>
internal sealed class PluginPainter : IPluginPainter
{
    // Image regions are pulled in at the pixel scale of the current paint.
    private double DevicePixelsPerPixel => PixelScale;

    private UiRenderContext? _context;
    private UiDatFont? _font;
    private PluginImages? _images;
    private PluginFonts? _fonts;
    private int _width;
    private int _height;

    /// <summary>
    /// The most shape vertices one paint may ask for, counted per paint
    /// before clipping. Shape triangles go into the frame's vertex ring,
    /// 16 MiB per frame slot and shared by everything drawn in that frame
    /// (<c>GpuMemoryProfile.RingCapacityBytesPerSlot</c>); at 32 bytes a
    /// vertex this is 1 MiB of it -- about 75 large circles or 130 rounded
    /// panels. Clipping can add vertices, so in the worst case the ring's
    /// share is a little more than 1 MiB. Tessellating and clipping this
    /// many takes about 0.6 ms, well inside the paint budget.
    /// </summary>
    internal const int MaximumShapeVerticesPerPaint = 32_768;

    /// <summary>One pixel of the target, in canvas pixels: the reciprocal of the pixel scale.</summary>
    private float _devicePixel = 1f;

    private float _pixelScale = 1f;

    private readonly List<UiColorVertex> _shape = new(1024);
    private Action<CanvasShapeProblem, string>? _shapeProblems;
    private int _shapeVertices;

    /// <summary>Points the painter at one repaint. Only the surface calls this.</summary>
    internal void Bind(
        UiRenderContext context, UiDatFont? font, PluginImages? images, int width, int height,
        PluginFonts? fonts = null, Action<CanvasShapeProblem, string>? shapeProblems = null,
        float pixelScale = 1f)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (!(float.IsFinite(pixelScale) && pixelScale >= 1f))
            throw new ArgumentOutOfRangeException(nameof(pixelScale), pixelScale, "A pixel scale is at least 1.");
        _pixelScale = pixelScale;
        _devicePixel = 1f / pixelScale;
        _font = font;
        _images = images;
        _fonts = fonts;
        _fonts?.BeginPaint();
        _width = width;
        _height = height;
        _shapeProblems = shapeProblems;
        _shapeVertices = 0;
        ShapeBudgetExceeded = false;
    }

    /// <summary>Forgets the repaint; every call after this throws.</summary>
    internal void Unbind()
    {
        _fonts?.EndPaint();
        _context = null;
        _font = null;
        _images = null;
        _fonts = null;
        _shapeProblems = null;
    }

    /// <summary>
    /// True once a shape in this repaint would have drawn more shape vertices
    /// than one paint may; it and later shapes in it are skipped. Kept after
    /// <see cref="Unbind"/> for the guard to read, cleared by the next
    /// <see cref="Bind"/>.
    /// </summary>
    internal bool ShapeBudgetExceeded { get; private set; }

    internal bool IsBound => _context is not null;

    public int Width => _width;

    public int Height => _height;

    public double PixelScale
    {
        get
        {
            _ = Context;
            return _pixelScale;
        }
    }

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

    public void DrawText(string text, PluginPoint position, PluginColor color, PluginFont font, bool outline = false)
    {
        UiRenderContext context = Context;
        if (string.IsNullOrEmpty(text) || !TryResolve(font, out CanvasFont? resolved)) return;
        context.DrawStringCanvasFont(
            resolved, text, (float)position.X, (float)position.Y, ToVector(color), outline, pixelScale: _pixelScale);
    }

    public PluginSize MeasureText(string text, PluginFont font)
    {
        _ = Context;
        if (string.IsNullOrEmpty(text) || !TryResolve(font, out CanvasFont? resolved)) return default;
        return new PluginSize(resolved.MeasureWidth(text), resolved.LineHeight);
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

    public void FillPolygon(ReadOnlySpan<PluginPoint> points, PluginColor color)
    {
        UiRenderContext context = Context;
        Span<Vector4> colors = stackalloc Vector4[Math.Min(points.Length, CanvasGeometry.MaximumPolygonPoints)];
        colors.Fill(ToVector(color));
        FillPolygonCore(context, points, colors);
    }

    public void FillPolygon(ReadOnlySpan<PluginPoint> points, ReadOnlySpan<PluginColor> colors)
    {
        UiRenderContext context = Context;
        if (colors.Length != points.Length)
        {
            Reject(nameof(FillPolygon), string.Create(
                CultureInfo.InvariantCulture, $"{colors.Length} colours for {points.Length} points"));
            return;
        }
        Span<Vector4> converted = stackalloc Vector4[Math.Min(colors.Length, CanvasGeometry.MaximumPolygonPoints)];
        for (int i = 0; i < converted.Length; i++)
            converted[i] = ToVector(colors[i]);
        FillPolygonCore(context, points, converted);
    }

    public void FillRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color)
    {
        UiRenderContext context = Context;
        if (!TryConvert(rect, nameof(FillRoundedRect), out float x, out float y, out float w, out float h)
            || !TryConvert(radii, nameof(FillRoundedRect), out CanvasCornerRadii corners)
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.FillRoundedRect(x, y, w, h, corners, ToVector(color), _devicePixel, _shape);
        Emit(context);
    }

    public void StrokeRoundedRect(PluginRect rect, PluginCornerRadii radii, PluginColor color, float thickness = 1f)
    {
        UiRenderContext context = Context;
        if (!TryConvert(rect, nameof(StrokeRoundedRect), out float x, out float y, out float w, out float h)
            || !TryConvert(radii, nameof(StrokeRoundedRect), out CanvasCornerRadii corners)
            || !CheckThickness(thickness, nameof(StrokeRoundedRect))
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.StrokeRoundedRect(x, y, w, h, corners, ToVector(color), thickness, _devicePixel, _shape);
        Emit(context);
    }

    public void FillEllipse(PluginRect bounds, PluginColor color)
    {
        UiRenderContext context = Context;
        if (!TryConvert(bounds, nameof(FillEllipse), out float x, out float y, out float w, out float h)
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.FillEllipse(x, y, w, h, ToVector(color), _devicePixel, _shape);
        Emit(context);
    }

    public void StrokeEllipse(PluginRect bounds, PluginColor color, float thickness = 1f)
    {
        UiRenderContext context = Context;
        if (!TryConvert(bounds, nameof(StrokeEllipse), out float x, out float y, out float w, out float h)
            || !CheckThickness(thickness, nameof(StrokeEllipse))
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.StrokeEllipse(x, y, w, h, ToVector(color), thickness, _devicePixel, _shape);
        Emit(context);
    }

    public void FillCircle(PluginPoint center, double radius, PluginColor color)
    {
        UiRenderContext context = Context;
        if (!double.IsFinite(radius) || radius < 0)
        {
            Reject(nameof(FillCircle), "a radius that is negative or not a finite number");
            return;
        }
        var bounds = new PluginRect(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        if (!TryConvert(bounds, nameof(FillCircle), out float x, out float y, out float w, out float h)
            || ShapeBudgetExceeded)
            return;
        _shape.Clear();
        CanvasGeometry.FillEllipse(x, y, w, h, ToVector(color), _devicePixel, _shape);
        Emit(context);
    }

    public void FillRectGradient(PluginRect rect, PluginColor from, PluginColor to, PluginGradientDirection direction)
    {
        UiRenderContext context = Context;
        if (!TryConvert(rect, nameof(FillRectGradient), out float x, out float y, out float w, out float h))
            return;
        if (direction is not (PluginGradientDirection.Horizontal or PluginGradientDirection.Vertical))
        {
            Reject(nameof(FillRectGradient), "a direction that is neither horizontal nor vertical");
            return;
        }
        if (ShapeBudgetExceeded) return;

        Vector4 start = ToVector(from);
        Vector4 end = ToVector(to);
        Span<Vector2> corners = [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];
        Span<Vector4> colors = stackalloc Vector4[4];
        if (direction == PluginGradientDirection.Horizontal)
        {
            colors[0] = start; colors[1] = end; colors[2] = end; colors[3] = start;
        }
        else
        {
            colors[0] = start; colors[1] = start; colors[2] = end; colors[3] = end;
        }
        _shape.Clear();
        CanvasGeometry.FillConvexPolygon(corners, colors, _devicePixel, _shape);
        Emit(context);
    }

    private UiRenderContext Context =>
        _context ?? throw new InvalidOperationException(
            "The painter is valid only for the duration of the paint callback it was handed to.");

    private bool TryResolve(PluginFont font, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CanvasFont? resolved)
    {
        resolved = null;
        return _fonts is not null && _fonts.TryResolve(font, out resolved);
    }

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

    private void FillPolygonCore(UiRenderContext context, ReadOnlySpan<PluginPoint> points, ReadOnlySpan<Vector4> colors)
    {
        const string member = nameof(FillPolygon);
        if (points.Length < 3)
        {
            Reject(member, "fewer than three points");
            return;
        }
        if (points.Length > CanvasGeometry.MaximumPolygonPoints)
        {
            Reject(member, string.Create(
                CultureInfo.InvariantCulture, $"more than {CanvasGeometry.MaximumPolygonPoints} points"));
            return;
        }
        Span<Vector2> corners = stackalloc Vector2[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            corners[i] = new Vector2((float)points[i].X, (float)points[i].Y);
            if (!float.IsFinite(corners[i].X) || !float.IsFinite(corners[i].Y))
            {
                Reject(member, "a point that is not a finite number");
                return;
            }
        }
        if (ShapeBudgetExceeded) return;

        _shape.Clear();
        if (CanvasGeometry.FillConvexPolygon(corners, colors, _devicePixel, _shape) == CanvasShapeOutcome.NotConvex)
        {
            Reject(member, "points that do not make a convex polygon");
            return;
        }
        Emit(context);
    }

    /// <summary>
    /// Hands the shape just tessellated to the context, unless it would take
    /// the paint past its vertex budget; then it and every later shape in
    /// this paint are skipped, and the surface tells the guard.
    /// </summary>
    private void Emit(UiRenderContext context)
    {
        int count = _shape.Count;
        if (count == 0) return;
        if (_shapeVertices + count > MaximumShapeVerticesPerPaint)
        {
            ShapeBudgetExceeded = true;
            _shapeProblems?.Invoke(CanvasShapeProblem.VertexBudget, string.Create(
                CultureInfo.InvariantCulture,
                $"one paint asked for more than {MaximumShapeVerticesPerPaint} shape vertices; the shapes past that were not drawn"));
            return;
        }
        _shapeVertices += count;
        context.DrawTriangles(CollectionsMarshal.AsSpan(_shape));
    }

    private bool TryConvert(PluginRect rect, string member, out float x, out float y, out float width, out float height)
    {
        x = (float)rect.X;
        y = (float)rect.Y;
        width = (float)rect.Width;
        height = (float)rect.Height;
        if (!(float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height)))
            return Reject(member, "a rectangle that is not a finite number");
        if (width < 0f || height < 0f)
            return Reject(member, "a rectangle of negative size");
        return true;
    }

    private bool TryConvert(PluginCornerRadii radii, string member, out CanvasCornerRadii corners)
    {
        corners = new CanvasCornerRadii(
            (float)radii.TopLeft, (float)radii.TopRight, (float)radii.BottomRight, (float)radii.BottomLeft);
        if (!(float.IsFinite(corners.TopLeft) && float.IsFinite(corners.TopRight)
              && float.IsFinite(corners.BottomRight) && float.IsFinite(corners.BottomLeft)))
            return Reject(member, "a corner radius that is not a finite number");
        if (corners.TopLeft < 0f || corners.TopRight < 0f || corners.BottomRight < 0f || corners.BottomLeft < 0f)
            return Reject(member, "a negative corner radius");
        return true;
    }

    private bool CheckThickness(float thickness, string member) =>
        (float.IsFinite(thickness) && thickness >= 0f)
        || Reject(member, "a thickness that is negative or not a finite number");

    private bool Reject(string member, string what)
    {
        _shapeProblems?.Invoke(CanvasShapeProblem.InvalidInput, $"{member} was given {what}");
        return false;
    }

    private static Vector4 ToVector(PluginColor color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
}
