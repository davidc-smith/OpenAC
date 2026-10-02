using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.UI;

internal readonly record struct UiClipRect(float Left, float Top, float Right, float Bottom)
{
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public static UiClipRect Intersect(UiClipRect a, UiClipRect b)
        => new(
            MathF.Max(a.Left, b.Left),
            MathF.Max(a.Top, b.Top),
            MathF.Min(a.Right, b.Right),
            MathF.Min(a.Bottom, b.Bottom));

    public static bool TryClipSprite(
        UiClipRect clip,
        ref float x, ref float y, ref float w, ref float h,
        ref float u0, ref float v0, ref float u1, ref float v1)
        => QuadClipper.TryClip(
            clip.Left, clip.Top, clip.Right, clip.Bottom,
            ref x, ref y, ref w, ref h,
            ref u0, ref v0, ref u1, ref v1);
}

public sealed class UiRenderContext
{
    public TextRenderer TextRenderer { get; }
    public BitmapFont? DefaultFont { get; set; }
    public Vector2 ScreenSize { get; private set; }

    private readonly System.Collections.Generic.List<Vector2> _stack = new();
    private Vector2 _current;
    private readonly System.Collections.Generic.List<UiClipRect?> _clipStack = new();
    private UiClipRect? _clip;

    private readonly System.Collections.Generic.List<float> _alphaStack = new();
    private float _alpha = 1f;
    private readonly System.Collections.Generic.List<UiColorVertex> _triangles = new(256);
    private readonly System.Collections.Generic.List<UiColorVertex> _shape = new(256);

    public float AlphaMod => _alpha;

    public void PushAlpha(float a) { _alphaStack.Add(_alpha); _alpha *= a; }

    public void PushAlphaAbsolute(float a) { _alphaStack.Add(_alpha); _alpha = a; }

    public void PopAlpha()
    {
        if (_alphaStack.Count == 0) return;
        _alpha = _alphaStack[^1];
        _alphaStack.RemoveAt(_alphaStack.Count - 1);
    }

    public UiRenderContext(TextRenderer tr, Vector2 screenSize, BitmapFont? defaultFont = null)
    {
        TextRenderer = tr;
        Begin(screenSize, defaultFont);
    }

    /// <summary>
    /// Device pixels per interface point this frame: 1 on a standard display,
    /// 2 on a typical high-density one. Shapes use it for the width of their
    /// anti-aliased edge, and fonts that carry a sharp companion use it to
    /// pick their sharper glyphs. Layout never reads it.
    /// </summary>
    public float PixelScale { get; private set; } = 1f;

    /// <summary>
    /// Points the context at a new frame and clears what the last one left.
    /// One context serves every frame instead of one being built per frame:
    /// its three stacks are built empty and grown as the interface nests, so
    /// a context per frame grew them again from nothing every frame -- fifteen
    /// megabytes per thirty seconds of clip-stack arrays alone at an uncapped
    /// frame rate. A newly constructed context goes through this same reset,
    /// which is what makes a reused one start where a fresh one starts.
    /// </summary>
    public void Begin(Vector2 screenSize, BitmapFont? defaultFont, float pixelScale = 1f)
    {
        ScreenSize = screenSize;
        DefaultFont = defaultFont;
        PixelScale = float.IsFinite(pixelScale) && pixelScale >= 1f ? pixelScale : 1f;
        _stack.Clear();
        _current = default;
        _clipStack.Clear();
        _clip = null;
        _alphaStack.Clear();
        _alpha = 1f;
    }

    public void PushTransform(float dx, float dy)
    {
        _stack.Add(_current);
        _current += new Vector2(dx, dy);
    }

    public void PopTransform()
    {
        if (_stack.Count == 0) return;
        _current = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
    }

    public Vector2 CurrentOrigin => _current;

    /// <summary>Intersect descendant drawing with a local-space viewport.</summary>
    public void PushClip(float x, float y, float w, float h)
    {
        _clipStack.Add(_clip);
        var next = new UiClipRect(
            _current.X + x,
            _current.Y + y,
            _current.X + x + MathF.Max(0f, w),
            _current.Y + y + MathF.Max(0f, h));
        _clip = _clip is { } current
            ? UiClipRect.Intersect(current, next)
            : next;
    }

    public void PopClip()
    {
        if (_clipStack.Count == 0) return;
        _clip = _clipStack[^1];
        _clipStack.RemoveAt(_clipStack.Count - 1);
    }

    internal int ClipStackDepth => _clipStack.Count;

    internal int TransformStackDepth => _stack.Count;

    internal int AlphaStackDepth => _alphaStack.Count;

    public bool CurrentClipIsEmpty => _clip is { } c && c.IsEmpty;

    public void PushClipUnbounded()
    {
        _clipStack.Add(_clip);
        _clip = new UiClipRect(0f, 0f, ScreenSize.X, ScreenSize.Y);
    }

    /// <summary>
    /// Deepens the clip stack by one without changing what is clipped, the way
    /// <c>PushTransform(0, 0)</c> and <c>PushAlpha(1)</c> deepen theirs. Only
    /// use is putting the depth back after a drawing callback popped more than
    /// it pushed and ate a level belonging to its caller. What it deepens with
    /// is whatever the over-popping left current -- a wider rectangle, or none
    /// at all -- so this restores the count, not the cropping.
    /// </summary>
    internal void PushClipUnchanged() => _clipStack.Add(_clip);

    public void BeginOverlayLayer() => TextRenderer.OverlayMode = true;
    public void EndOverlayLayer() => TextRenderer.OverlayMode = false;

    /// <summary>
    /// Sends what is drawn next to the upper layer, which the frame draws
    /// after everything before it, text included. The root calls it once,
    /// at the plugin canvases drawn over windows.
    /// </summary>
    internal void BeginUpperLayer() => TextRenderer.Layer = UiDrawLayer.Upper;

    public void DrawRect(float x, float y, float w, float h, Vector4 color) => DrawFill(x, y, w, h, color);

    public void DrawFill(float x, float y, float w, float h, Vector4 color)
    {
        x += _current.X;
        y += _current.Y;
        if (!ClipRect(ref x, ref y, ref w, ref h)) return;
        TextRenderer.DrawFill(x, y, w, h, ApplyAlpha(color));
    }

    public void DrawRectOutline(float x, float y, float w, float h, Vector4 color, float thickness = 1f)
    {
        if (thickness <= 0f || w <= 0f || h <= 0f) return;
        float t = MathF.Min(thickness, MathF.Min(w, h) * 0.5f);
        DrawRect(x, y, w, t, color);
        DrawRect(x, y + h - t, w, t, color);
        DrawRect(x, y + t, t, h - 2f * t, color);
        DrawRect(x + w - t, y + t, t, h - 2f * t, color);
    }

    public void DrawSprite(uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        x += _current.X;
        y += _current.Y;
        DrawSpriteAbsolute(texture, x, y, w, h, u0, v0, u1, v1, tint, applyAlpha: true);
    }

    /// <summary>
    /// Draws a texture whose colour is already multiplied by its alpha, such
    /// as a plugin canvas's target. Translation, clipping and the alpha stack
    /// apply as they do for <see cref="DrawSprite"/>.
    /// </summary>
    internal void DrawSpritePremultiplied(uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint)
    {
        x += _current.X;
        y += _current.Y;
        if (_clip is { } clip
            && !UiClipRect.TryClipSprite(
                clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1))
            return;
        TextRenderer.DrawPremultipliedSprite(texture, x, y, w, h, u0, v0, u1, v1, ApplyAlpha(tint));
    }

    /// <summary>
    /// Draws a region of a single-channel texture as the alpha of
    /// <paramref name="color"/> (see <see cref="TextRenderer.DrawCoverageSprite"/>).
    /// Translation, clipping and the alpha stack apply as they do for
    /// <see cref="DrawSprite"/>.
    /// </summary>
    internal void DrawCoverageSprite(uint coverageTexture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 color) =>
        DrawCoverageSpriteAbsolute(coverageTexture, x + _current.X, y + _current.Y, w, h, u0, v0, u1, v1, color);

    private void DrawCoverageSpriteAbsolute(uint coverageTexture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 color)
    {
        if (_clip is { } clip
            && !UiClipRect.TryClipSprite(
                clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1))
            return;
        TextRenderer.DrawCoverageSprite(coverageTexture, x, y, w, h, u0, v0, u1, v1, ApplyAlpha(color));
    }

    /// <summary>
    /// A straight segment from (x0, y0) to (x1, y1), <paramref name="thickness"/>
    /// pixels wide, centred on the segment -- half the width falls on each
    /// side of the line through the two points. The ends are square and flush
    /// with the endpoints; there are no caps and no joins, so a chain of
    /// segments leaves a notch on the outside of a sharp corner.
    /// </summary>
    public void DrawLine(
        float x0, float y0, float x1, float y1, Vector4 color, float thickness = 1f)
    {
        if (thickness <= 0f) return;
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 0f)) return;

        // Step sideways off the segment by half the width. Turning a direction
        // a quarter turn gives (-dy, dx), so that is the sideways direction.
        float half = thickness * 0.5f;
        float nx = -dy / length * half;
        float ny = dx / length * half;

        float ax = _current.X + x0;
        float ay = _current.Y + y0;
        float bx = _current.X + x1;
        float by = _current.Y + y1;

        Span<UiQuadVertex> quad =
        [
            new(ax + nx, ay + ny, 0f, 0f),
            new(bx + nx, by + ny, 0f, 0f),
            new(bx - nx, by - ny, 0f, 0f),
            new(ax - nx, ay - ny, 0f, 0f),
        ];
        EmitConvexQuad(UiTextureTableHandle.None, quad, ApplyAlpha(color));
    }

    /// <summary>
    /// Blits a sub-image into the rectangle (x, y, w, h) after scaling and
    /// rotating that rectangle about a pivot.
    ///
    /// <para><paramref name="pivot"/> is measured in pixels from the
    /// rectangle's own top-left corner, so (0, 0) turns the blit about its
    /// top-left and (w / 2, h / 2) about its middle. Each corner is first
    /// pushed away from (or pulled towards) the pivot by
    /// <paramref name="scale"/>, then swung around the pivot by
    /// <paramref name="rotationRadians"/>, measured clockwise on screen
    /// because the y axis points down.</para>
    ///
    /// <para>A rotation of zero and a scale of one produce exactly the
    /// rectangle <see cref="DrawSprite"/> would have drawn.</para>
    /// </summary>
    public void DrawSpriteTransformed(
        uint texture,
        float x, float y, float w, float h,
        float u0, float v0, float u1, float v1,
        Vector4 tint,
        float rotationRadians,
        Vector2 scale,
        Vector2 pivot)
    {
        if (w == 0f || h == 0f || scale.X == 0f || scale.Y == 0f) return;

        float cos = MathF.Cos(rotationRadians);
        float sin = MathF.Sin(rotationRadians);
        float pivotX = _current.X + x + pivot.X;
        float pivotY = _current.Y + y + pivot.Y;
        float left = _current.X + x;
        float top = _current.Y + y;

        Span<UiQuadVertex> quad =
        [
            Corner(left, top, u0, v0),
            Corner(left + w, top, u1, v0),
            Corner(left + w, top + h, u1, v1),
            Corner(left, top + h, u0, v1),
        ];
        EmitConvexQuad(texture, quad, ApplyAlpha(tint));

        UiQuadVertex Corner(float cx, float cy, float u, float v)
        {
            float ox = (cx - pivotX) * scale.X;
            float oy = (cy - pivotY) * scale.Y;
            return new UiQuadVertex(
                pivotX + ox * cos - oy * sin,
                pivotY + ox * sin + oy * cos,
                u,
                v);
        }
    }

    /// <summary>
    /// Trims a four-cornered shape to the clip in force and hands what is left
    /// to the batcher. The upright path has its own clipper that just moves
    /// the rectangle's edges; once a shape is turned, cutting it can add
    /// corners, so it goes through the polygon clipper instead.
    /// </summary>
    private void EmitConvexQuad(uint texture, ReadOnlySpan<UiQuadVertex> quad, Vector4 color)
    {
        if (_clip is not { } clip)
        {
            TextRenderer.DrawConvexPolygon(texture, quad, color);
            return;
        }
        if (clip.IsEmpty) return;

        Span<UiQuadVertex> clipped =
            stackalloc UiQuadVertex[TransformedQuadClipper.MaxClippedVertices];
        int count = TransformedQuadClipper.Clip(
            clip.Left, clip.Top, clip.Right, clip.Bottom, quad, clipped);
        if (count < 3) return;
        TextRenderer.DrawConvexPolygon(texture, clipped[..count], color);
    }

    /// <summary>
    /// Untextured triangles in local coordinates, three vertices each, with a
    /// colour at every corner. They are moved by the current origin, faded
    /// by the alpha stack and cut to the clip in force, with the colour
    /// blended wherever the clip cuts, then handed to the batcher in one go.
    /// </summary>
    internal void DrawTriangles(ReadOnlySpan<UiColorVertex> triangles)
    {
        if (triangles.Length < 3 || _clip is { IsEmpty: true }) return;

        _triangles.Clear();
        Span<UiColorVertex> clipped = stackalloc UiColorVertex[ColoredTriangleClipper.MaxClippedVertices];
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            UiColorVertex a = Place(triangles[i]);
            UiColorVertex b = Place(triangles[i + 1]);
            UiColorVertex c = Place(triangles[i + 2]);
            if (_clip is not { } clip)
            {
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
                continue;
            }

            int count = ColoredTriangleClipper.Clip(clip.Left, clip.Top, clip.Right, clip.Bottom, a, b, c, clipped);
            for (int k = 1; k + 1 < count; k++)
            {
                _triangles.Add(clipped[0]);
                _triangles.Add(clipped[k]);
                _triangles.Add(clipped[k + 1]);
            }
        }

        if (_triangles.Count > 0)
            TextRenderer.DrawTriangles(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_triangles));
    }

    /// <summary>One device pixel, in interface points: the width of a shape's soft edge.</summary>
    private float DevicePixel => 1f / PixelScale;

    private void DrawShape()
    {
        if (_shape.Count > 0)
            DrawTriangles(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_shape));
    }

    /// <summary>A rectangle with rounded corners and a smooth edge, in local coordinates.</summary>
    internal void FillRoundedRect(float x, float y, float w, float h, CanvasCornerRadii radii, Vector4 color)
    {
        _shape.Clear();
        CanvasGeometry.FillRoundedRect(x, y, w, h, radii, color, DevicePixel, _shape);
        DrawShape();
    }

    internal void FillRoundedRect(float x, float y, float w, float h, float radius, Vector4 color) =>
        FillRoundedRect(x, y, w, h, new CanvasCornerRadii(radius, radius, radius, radius), color);

    /// <summary>A rounded rectangle's outline, centred on the outline.</summary>
    internal void StrokeRoundedRect(float x, float y, float w, float h, float radius, Vector4 color, float thickness)
    {
        _shape.Clear();
        CanvasGeometry.StrokeRoundedRect(
            x, y, w, h, new CanvasCornerRadii(radius, radius, radius, radius), color, thickness, DevicePixel, _shape);
        DrawShape();
    }

    internal void FillEllipse(float x, float y, float w, float h, Vector4 color)
    {
        _shape.Clear();
        CanvasGeometry.FillEllipse(x, y, w, h, color, DevicePixel, _shape);
        DrawShape();
    }

    internal void StrokeEllipse(float x, float y, float w, float h, Vector4 color, float thickness)
    {
        _shape.Clear();
        CanvasGeometry.StrokeEllipse(x, y, w, h, color, thickness, DevicePixel, _shape);
        DrawShape();
    }

    /// <summary>A straight segment with smooth sides and square, flush ends.</summary>
    internal void DrawSmoothLine(float x0, float y0, float x1, float y1, Vector4 color, float thickness)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (!(length > 0f) || !(thickness > 0f)) return;
        float nx = -dy / length * thickness * 0.5f;
        float ny = dx / length * thickness * 0.5f;
        Span<Vector2> corners =
        [
            new(x0 + nx, y0 + ny), new(x1 + nx, y1 + ny), new(x1 - nx, y1 - ny), new(x0 - nx, y0 - ny),
        ];
        Span<Vector4> colors = [color, color, color, color];
        _shape.Clear();
        CanvasGeometry.FillConvexPolygon(corners, colors, DevicePixel, _shape);
        DrawShape();
    }

    /// <summary>
    /// A rounded rectangle whose colour runs from <paramref name="top"/> to
    /// <paramref name="bottom"/>. Each corner of the shape takes the colour
    /// at its height; the soft edge keeps its own transparency.
    /// </summary>
    internal void FillVerticalGradient(
        float x, float y, float w, float h, CanvasCornerRadii radii, Vector4 top, Vector4 bottom)
    {
        if (!(h > 0f)) return;
        _shape.Clear();
        CanvasGeometry.FillRoundedRect(x, y, w, h, radii, Vector4.One, DevicePixel, _shape);
        for (int i = 0; i < _shape.Count; i++)
        {
            UiColorVertex v = _shape[i];
            float t = Math.Clamp((v.Position.Y - y) / h, 0f, 1f);
            Vector4 c = Vector4.Lerp(top, bottom, t);
            _shape[i] = new UiColorVertex(v.Position, c with { W = c.W * v.Color.W });
        }
        DrawShape();
    }

    /// <summary>
    /// A soft shadow under a rounded rectangle: six stacked rounded fills,
    /// each grown by a sixth of <paramref name="spread"/>, so the darkness
    /// fades outwards. Drawn with the clip lifted to the whole screen,
    /// because a shadow lies outside the element that casts it.
    /// </summary>
    internal void DrawSoftShadow(float x, float y, float w, float h, float radius, float spread, Vector4 color)
    {
        if (!(spread > 0f) || !(color.W > 0f) || !(w > 0f) || !(h > 0f)) return;
        const int Layers = 6;
        Vector4 layer = color with { W = color.W / Layers };
        PushClipUnbounded();
        try
        {
            for (int i = Layers; i >= 1; i--)
            {
                float grow = spread * i / Layers;
                FillRoundedRect(x - grow, y - grow, w + 2f * grow, h + 2f * grow, radius + grow, layer);
            }
        }
        finally
        {
            PopClip();
        }
    }

    private UiColorVertex Place(in UiColorVertex vertex) =>
        new(vertex.Position + _current, ApplyAlpha(vertex.Color));

    private void DrawSpriteAbsolute(
        uint texture, float x, float y, float w, float h,
        float u0, float v0, float u1, float v1, Vector4 tint, bool applyAlpha)
    {
        if (_clip is { } clip
            && !UiClipRect.TryClipSprite(
                clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1))
            return;
        TextRenderer.DrawSprite(
            texture, x, y, w, h, u0, v0, u1, v1,
            applyAlpha ? ApplyAlpha(tint) : tint);
    }

    private bool ClipRect(ref float x, ref float y, ref float w, ref float h)
    {
        if (_clip is not { } clip)
            return w > 0f && h > 0f;
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
        return UiClipRect.TryClipSprite(
            clip, ref x, ref y, ref w, ref h, ref u0, ref v0, ref u1, ref v1);
    }

    private Vector4 ApplyAlpha(Vector4 c) => _alpha >= 1f ? c : new Vector4(c.X, c.Y, c.Z, c.W * _alpha);

    public void DrawString(string text, float x, float y, Vector4 color, BitmapFont? font = null)
    {
        var f = font ?? DefaultFont;
        if (f is null) return;
        float screenX = _current.X + x;
        float screenY = _current.Y + y;
        Vector4 alphaColor = ApplyAlpha(color);
        if (_clip is { } clip)
        {
            TextRenderer.DrawStringClipped(
                f, text, screenX, screenY, alphaColor,
                clip.Left, clip.Top, clip.Right, clip.Bottom);
            return;
        }
        TextRenderer.DrawString(f, text, screenX, screenY, alphaColor);
    }

    public static readonly Vector4 DefaultOutlineColor = new(0f, 0f, 0f, 1f);

    public static readonly Vector4 StoreOnlyCaptionColor = new(0.5f, 0.5f, 0.5f, 1f);

    public void DrawStringDat(
        UiDatFont font, string text, float x, float y, Vector4 color,
        bool outline = false, Vector4? outlineColor = null)
    {
        if (font is null || string.IsNullOrEmpty(text)) return;

        if (outline)
        {
            DrawStringDatPass(font, text, x, y, outlineColor ?? DefaultOutlineColor, isOutlinePass: true);
        }

        // PASS 1 (or the only pass, when outline is off) — fill, whole string.
        DrawStringDatPass(font, text, x, y, color, isOutlinePass: false);
    }

    public void DrawStringDatPass(
        UiDatFont font, string text, float x, float y, Vector4 tint, bool isOutlinePass)
    {
        if (font is null || string.IsNullOrEmpty(text)) return;

        float originX = _current.X + x;
        float originY = _current.Y + y;

        float baseY = System.MathF.Floor(originY + 0.5f);

        // On a high-density display a bundled font's sharper twin supplies
        // the glyph images; the pen still advances by this font's own metrics.
        UiDatFontSharp? sharp = !isOutlinePass && PixelScale >= 1.5f ? font.Sharp : null;
        float grid = sharp is null ? 1f : MathF.Min(PixelScale, sharp.Scale);

        float pen = originX;
        for (int i = 0; i < text.Length; i++)
        {
            if (!font.TryGetGlyph(text[i], out var g))
                continue;

            if (sharp is not null && sharp.Font.TryGetGlyph(text[i], out var fine)
                && fine.Width > 0 && fine.Height > 0)
            {
                float texel = 1f / sharp.Scale;
                float fx = Snap(pen + fine.HorizontalOffsetBefore * texel, grid);
                float fy = baseY + MathF.Round(fine.VerticalOffsetBefore * texel * grid) / grid;
                DrawFillGlyph(sharp.Font, fine, fx, fy, fine.Width * texel, fine.Height * texel, tint);
                pen += UiDatFont.GlyphAdvance(g);
                continue;
            }

            // Horizontal: snap each glyph's dest X to a whole pixel (the pen keeps its
            // true fractional advance). Vertical: integer baseline + integer per-glyph
            // offset — never an independent per-glyph round (see baseY's note above).
            // Half-up for the same anti-vibration reason as baseY.
            float gx = System.MathF.Floor(pen + g.HorizontalOffsetBefore + 0.5f);
            float gy = baseY + g.VerticalOffsetBefore;
            float gw = g.Width;
            float gh = g.Height;

            if (gw > 0f && gh > 0f)
            {
                if (isOutlinePass)
                    DrawOutlineGlyph(font, g, gx, gy, gw, gh, tint);
                else
                    DrawFillGlyph(font, g, gx, gy, gw, gh, tint);
            }

            pen += UiDatFont.GlyphAdvance(g);
        }
    }

    /// <summary>
    /// Draws one line of text in a baked canvas font, its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>). Glyphs are coverage
    /// sprites, so the text keeps its place among fills and images. The
    /// baseline and each glyph's left edge snap to whole pixels while the pen
    /// keeps its fractional advance, as the interface font does. The outline
    /// is eight copies one pixel out, drawn first.
    ///
    /// <para>On a canvas painted at a <paramref name="pixelScale"/> above 1,
    /// glyphs come from the font's sharper bake when it has one, and snap
    /// to that bake's pixels instead: whole device pixels when the bake
    /// matches the canvas. The pen, and so every position the text is
    /// measured at, still comes from the font's own bake.</para>
    /// </summary>
    internal void DrawStringCanvasFont(
        CanvasFont font, string text, float x, float y, Vector4 color,
        bool outline = false, Vector4? outlineColor = null, float pixelScale = 1f)
    {
        if (font is null || string.IsNullOrEmpty(text)) return;
        if (outline)
        {
            Vector4 shadow = outlineColor ?? DefaultOutlineColor;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dy != 0)
                        DrawCanvasFontPass(font, text, x + dx, y + dy, shadow, pixelScale);
                }
            }
        }
        DrawCanvasFontPass(font, text, x, y, color, pixelScale);
    }

    private void DrawCanvasFontPass(CanvasFont font, string text, float x, float y, Vector4 color, float pixelScale)
    {
        CanvasSharpGlyphs? sharp = pixelScale > 1f ? font.Sharp : null;
        // Pixels per canvas pixel of the grid glyphs snap to: the sharper
        // bake's, but no finer than the canvas's own device pixels.
        float grid = sharp is null ? 1f : MathF.Min(pixelScale, sharp.Scale);
        float pen = _current.X + x;
        float baseline = Snap(_current.Y + y + font.Ascent, grid);
        int previous = -1;
        foreach (System.Text.Rune rune in text.EnumerateRunes())
        {
            if (!font.TryGetGlyph(rune.Value, out CanvasGlyph glyph))
            {
                previous = -1;
                continue;
            }
            if (previous >= 0)
                pen += font.Kerning(previous, glyph.GlyphIndex);
            // Canvas pixels per atlas pixel of the glyph drawn.
            float texel = 1f;
            CanvasGlyph drawn = glyph;
            uint atlas = font.AtlasTexture;
            if (sharp is not null && sharp.TryGetGlyph(rune.Value, out CanvasGlyph fine))
            {
                texel = 1f / sharp.Scale;
                drawn = fine;
                atlas = sharp.AtlasTexture;
            }
            if (drawn.Width > 0f && drawn.Height > 0f)
            {
                float gx = Snap(pen + drawn.OffsetX * texel, grid);
                float gy = baseline + MathF.Round(drawn.OffsetY * texel * grid) / grid;
                DrawCoverageSpriteAbsolute(
                    atlas, gx, gy, drawn.Width * texel, drawn.Height * texel,
                    drawn.U0, drawn.V0, drawn.U1, drawn.V1, color);
            }
            pen += glyph.Advance;
            previous = glyph.GlyphIndex;
        }
    }

    /// <summary>Rounds half up to the nearest multiple of 1 / <paramref name="pixelsPerUnit"/>.</summary>
    private static float Snap(float value, float pixelsPerUnit) =>
        MathF.Floor(value * pixelsPerUnit + 0.5f) / pixelsPerUnit;

    private void DrawFillGlyph(
        UiDatFont font, DatReaderWriter.Types.FontCharDesc g,
        float gx, float gy, float gw, float gh, Vector4 tint)
    {
        var (fu0, fv0, fu1, fv1) = AtlasUv(
            g.OffsetX, g.OffsetY, g.Width, g.Height,
            font.ForegroundWidth, font.ForegroundHeight);
        DrawSpriteAbsolute(font.ForegroundTexture, gx, gy, gw, gh, fu0, fv0, fu1, fv1, tint, applyAlpha: true);
    }

    private void DrawOutlineGlyph(
        UiDatFont font, DatReaderWriter.Types.FontCharDesc g,
        float gx, float gy, float gw, float gh, Vector4 tint)
    {
        if (font.BackgroundTexture != 0)
        {
            int bx = font.BorderX, by = font.BorderY;
            float ix = gx - bx;
            float iy = gy - by;
            float iw = gw + 2f * bx;
            float ih = gh + 2f * by;

            float srcX = g.OffsetX - bx, srcY = g.OffsetY - by;
            float srcW = iw, srcH = ih;
            float destX = ix, destY = iy, destW = iw, destH = ih;
            if (srcX < 0f) { destX -= srcX; destW += srcX; srcW += srcX; srcX = 0f; }
            if (srcY < 0f) { destY -= srcY; destH += srcY; srcH += srcY; srcY = 0f; }
            if (srcX + srcW > font.BackgroundWidth)
            {
                float over = srcX + srcW - font.BackgroundWidth;
                srcW -= over; destW -= over;
            }
            if (srcY + srcH > font.BackgroundHeight)
            {
                float over = srcY + srcH - font.BackgroundHeight;
                srcH -= over; destH -= over;
            }
            if (srcW <= 0f || srcH <= 0f) return;

            var (bu0, bv0, bu1, bv1) = AtlasUv(
                (int)srcX, (int)srcY, (int)srcW, (int)srcH,
                font.BackgroundWidth, font.BackgroundHeight);
            DrawSpriteAbsolute(font.BackgroundTexture, destX, destY, destW, destH, bu0, bv0, bu1, bv1, tint, applyAlpha: true);
        }
        else
        {
            var (fu0, fv0, fu1, fv1) = AtlasUv(
                g.OffsetX, g.OffsetY, g.Width, g.Height,
                font.ForegroundWidth, font.ForegroundHeight);
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    DrawSpriteAbsolute(
                        font.ForegroundTexture, gx + dx, gy + dy, gw, gh,
                        fu0, fv0, fu1, fv1, tint, applyAlpha: true);
                }
            }
        }
    }

    private static (float u0, float v0, float u1, float v1) AtlasUv(
        int offsetX, int offsetY, int width, int height, int atlasW, int atlasH)
    {
        if (atlasW <= 0 || atlasH <= 0) return (0f, 0f, 0f, 0f);
        float u0 = offsetX / (float)atlasW;
        float v0 = offsetY / (float)atlasH;
        float u1 = (offsetX + width) / (float)atlasW;
        float v1 = (offsetY + height) / (float)atlasH;
        return (u0, v0, u1, v1);
    }
}
