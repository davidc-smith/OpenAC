using System.Collections.Generic;
using System.Numerics;
using AcDream.App.Rendering;

namespace AcDream.App.UI;

/// <summary>What tessellating one shape came to.</summary>
internal enum CanvasShapeOutcome
{
    /// <summary>Triangles were written.</summary>
    Drawn,

    /// <summary>The shape covers nothing (no area, no thickness); nothing was written and nothing is wrong.</summary>
    Nothing,

    /// <summary>A polygon of fewer than three points.</summary>
    TooFewPoints,

    /// <summary>A polygon of more than <see cref="CanvasGeometry.MaximumPolygonPoints"/> points.</summary>
    TooManyPoints,

    /// <summary>A polygon whose outline turns both ways, or winds round more than once.</summary>
    NotConvex,
}

/// <summary>The four corner radii of a rounded rectangle, in canvas pixels.</summary>
internal readonly record struct CanvasCornerRadii(float TopLeft, float TopRight, float BottomRight, float BottomLeft);

/// <summary>
/// Turns canvas shapes into untextured triangles with a colour at every
/// corner, anti-aliased on the CPU: each edge gets a band one device pixel
/// wide whose outer vertices are transparent, centred on the true edge, so
/// the rasteriser's linear interpolation ramps coverage across it. Half the
/// band lies inside the shape and half outside, which keeps the covered
/// area equal to the shape's own. No shader is involved.
///
/// <para>A shape is built from rings: its outline pushed out and pulled in
/// by the fringe (and, for a stroke, by half the stroke's width), each
/// ring with the same number of points so neighbouring rings join point
/// for point. Everything here is pure: sizes come in canvas pixels, along
/// with how many canvas pixels one device pixel is, and triangles go out
/// as a list, three vertices each.</para>
/// </summary>
internal static class CanvasGeometry
{
    /// <summary>The most points a polygon may have.</summary>
    internal const int MaximumPolygonPoints = 64;

    /// <summary>How far, in device pixels, a chord may stray inside the true curve.</summary>
    internal const float ArcTolerance = 0.1f;

    /// <summary>The most chords one rounded corner is cut into.</summary>
    internal const int MaximumSegmentsPerCorner = 32;

    /// <summary>The fewest chords an ellipse is cut into.</summary>
    internal const int MinimumEllipseSegments = 8;

    /// <summary>The most chords an ellipse is cut into.</summary>
    internal const int MaximumEllipseSegments = 256;

    /// <summary>
    /// How far out a polygon corner's fringe may reach, as a multiple of half
    /// the fringe. A very sharp corner's true mitre would reach much further.
    /// </summary>
    private const float MaximumMiter = 10f;

    /// <summary>
    /// How many chords an arc of <paramref name="radius"/> canvas pixels
    /// sweeping <paramref name="sweep"/> radians needs so that no chord
    /// strays more than <see cref="ArcTolerance"/> device pixels from it,
    /// with <paramref name="pixel"/> canvas pixels to a device pixel.
    /// </summary>
    internal static int ArcSegments(float radius, float sweep, float pixel)
    {
        float radiusInPixels = radius / pixel;
        if (!(radiusInPixels > 0f)) return 0;
        if (radiusInPixels <= ArcTolerance) return 1;
        float step = 2f * MathF.Acos(1f - ArcTolerance / radiusInPixels);
        return Math.Max(1, (int)MathF.Ceiling(sweep / step));
    }

    /// <summary>
    /// Scales every radius down by one factor when two neighbours would not
    /// fit along the side they share, the way CSS does, so a corner never
    /// overruns its side and the shape keeps its proportions.
    /// </summary>
    internal static CanvasCornerRadii ClampRadii(CanvasCornerRadii radii, float width, float height)
    {
        float factor = 1f;
        factor = Fit(factor, width, radii.TopLeft + radii.TopRight);
        factor = Fit(factor, width, radii.BottomLeft + radii.BottomRight);
        factor = Fit(factor, height, radii.TopLeft + radii.BottomLeft);
        factor = Fit(factor, height, radii.TopRight + radii.BottomRight);
        return factor < 1f
            ? new CanvasCornerRadii(
                radii.TopLeft * factor, radii.TopRight * factor,
                radii.BottomRight * factor, radii.BottomLeft * factor)
            : radii;

        static float Fit(float factor, float side, float sum) =>
            sum > side ? MathF.Min(factor, side / sum) : factor;
    }

    /// <summary>
    /// A convex polygon with a colour at each point, blended across it.
    /// Repeated neighbouring points are merged; a polygon with no area draws
    /// nothing. Either winding is accepted.
    /// </summary>
    internal static CanvasShapeOutcome FillConvexPolygon(
        ReadOnlySpan<Vector2> points, ReadOnlySpan<Vector4> colors, float pixel, List<UiColorVertex> output)
    {
        if (points.Length < 3) return CanvasShapeOutcome.TooFewPoints;
        if (points.Length > MaximumPolygonPoints) return CanvasShapeOutcome.TooManyPoints;
        if (colors.Length != points.Length)
            throw new ArgumentException("Each point needs its own colour.", nameof(colors));

        Span<Vector2> corner = stackalloc Vector2[MaximumPolygonPoints];
        Span<Vector4> color = stackalloc Vector4[MaximumPolygonPoints];
        int count = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (count > 0 && SamePoint(corner[count - 1], points[i])) continue;
            corner[count] = points[i];
            color[count] = colors[i];
            count++;
        }
        while (count > 1 && SamePoint(corner[count - 1], corner[0])) count--;
        if (count < 3) return CanvasShapeOutcome.Nothing;
        corner = corner[..count];
        color = color[..count];

        // Convex means every turn goes the same way and the turns add up to
        // one full circle; a star turns one way throughout but twice round.
        int turn = 0;
        double turning = 0;
        for (int i = 0; i < count; i++)
        {
            Vector2 into = corner[(i + 1) % count] - corner[i];
            Vector2 outOf = corner[(i + 2) % count] - corner[(i + 1) % count];
            float cross = Cross(into, outOf);
            if (MathF.Abs(cross) > 1e-6f * into.Length() * outOf.Length())
            {
                int sign = Math.Sign(cross);
                if (turn == 0) turn = sign;
                else if (sign != turn) return CanvasShapeOutcome.NotConvex;
            }
            turning += Math.Atan2(cross, Vector2.Dot(into, outOf));
        }
        if (turn == 0) return CanvasShapeOutcome.Nothing;
        if (Math.Abs(Math.Abs(turning) - 2 * Math.PI) > 1e-3) return CanvasShapeOutcome.NotConvex;

        // Outward normal of each edge; which side is out depends on the winding.
        Span<Vector2> normal = stackalloc Vector2[count];
        for (int i = 0; i < count; i++)
        {
            Vector2 edge = Vector2.Normalize(corner[(i + 1) % count] - corner[i]);
            normal[i] = turn > 0 ? new Vector2(edge.Y, -edge.X) : new Vector2(-edge.Y, edge.X);
        }

        float half = pixel * 0.5f;
        Span<Vector2> inner = stackalloc Vector2[count];
        Span<Vector2> outer = stackalloc Vector2[count];
        Span<Vector4> clear = stackalloc Vector4[count];
        for (int i = 0; i < count; i++)
        {
            // The mitre: the corner moves along the average of its two edge
            // normals, far enough that both edges move by half a pixel.
            Vector2 average = (normal[(i + count - 1) % count] + normal[i]) * 0.5f;
            Vector2 miter = average / MathF.Max(average.LengthSquared(), 1f / (MaximumMiter * MaximumMiter));
            inner[i] = corner[i] - miter * half;
            outer[i] = corner[i] + miter * half;
            clear[i] = Transparent(color[i]);
        }

        Fan(inner, color, output);
        Band(inner, color, outer, clear, output);
        return CanvasShapeOutcome.Drawn;
    }

    /// <summary>A rectangle with rounded corners; zero radii give square corners.</summary>
    internal static void FillRoundedRect(
        float x, float y, float width, float height, CanvasCornerRadii radii, Vector4 color, float pixel,
        List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f)) return;
        radii = ClampRadii(radii, width, height);
        float half = pixel * 0.5f;
        Span<int> segments = stackalloc int[4];
        CornerSegments(radii, half, pixel, segments);
        int points = RingLength(segments);
        Span<Vector2> inner = stackalloc Vector2[points];
        Span<Vector2> outer = stackalloc Vector2[points];
        RoundedRectRing(x, y, width, height, radii, -half, segments, inner);
        RoundedRectRing(x, y, width, height, radii, half, segments, outer);
        FillRing(inner, outer, color, output);
    }

    /// <summary>
    /// A rounded rectangle's outline, <paramref name="thickness"/> wide,
    /// centred on the outline. Outside a square corner the stroke stays square.
    /// </summary>
    internal static void StrokeRoundedRect(
        float x, float y, float width, float height, CanvasCornerRadii radii, Vector4 color, float thickness,
        float pixel, List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f && thickness > 0f)) return;
        radii = ClampRadii(radii, width, height);
        (float halfWidth, Vector4 ink) = StrokeProfile(thickness, color, pixel);
        float fringe = pixel * 0.5f;
        Span<int> segments = stackalloc int[4];
        CornerSegments(radii, halfWidth + fringe, pixel, segments);
        int points = RingLength(segments);
        Span<Vector2> rings = stackalloc Vector2[points * 4];
        RoundedRectRing(x, y, width, height, radii, halfWidth + fringe, segments, rings.Slice(0, points));
        RoundedRectRing(x, y, width, height, radii, halfWidth - fringe, segments, rings.Slice(points, points));
        RoundedRectRing(x, y, width, height, radii, fringe - halfWidth, segments, rings.Slice(points * 2, points));
        RoundedRectRing(x, y, width, height, radii, -halfWidth - fringe, segments, rings.Slice(points * 3, points));
        StrokeRings(rings, points, ink, halfWidth > fringe, output);
    }

    /// <summary>An ellipse filling a rectangle.</summary>
    internal static void FillEllipse(
        float x, float y, float width, float height, Vector4 color, float pixel, List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f)) return;
        var centre = new Vector2(x + width * 0.5f, y + height * 0.5f);
        var radii = new Vector2(width * 0.5f, height * 0.5f);
        float half = pixel * 0.5f;
        int segments = EllipseSegments(radii, half, pixel);
        Span<Vector2> inner = stackalloc Vector2[segments];
        Span<Vector2> outer = stackalloc Vector2[segments];
        EllipseRing(centre, radii, -half, inner);
        EllipseRing(centre, radii, half, outer);
        FillRing(inner, outer, color, output);
    }

    /// <summary>
    /// An ellipse's outline, <paramref name="thickness"/> wide, centred on
    /// the outline. Its rings grow both radii alike, which is exact for a
    /// circle and close for an ellipse.
    /// </summary>
    internal static void StrokeEllipse(
        float x, float y, float width, float height, Vector4 color, float thickness, float pixel,
        List<UiColorVertex> output)
    {
        if (!(width > 0f && height > 0f && thickness > 0f)) return;
        var centre = new Vector2(x + width * 0.5f, y + height * 0.5f);
        var radii = new Vector2(width * 0.5f, height * 0.5f);
        (float halfWidth, Vector4 ink) = StrokeProfile(thickness, color, pixel);
        float fringe = pixel * 0.5f;
        int segments = EllipseSegments(radii, halfWidth + fringe, pixel);
        Span<Vector2> rings = stackalloc Vector2[segments * 4];
        EllipseRing(centre, radii, halfWidth + fringe, rings.Slice(0, segments));
        EllipseRing(centre, radii, halfWidth - fringe, rings.Slice(segments, segments));
        EllipseRing(centre, radii, fringe - halfWidth, rings.Slice(segments * 2, segments));
        EllipseRing(centre, radii, -halfWidth - fringe, rings.Slice(segments * 3, segments));
        StrokeRings(rings, segments, ink, halfWidth > fringe, output);
    }

    /// <summary>
    /// How wide a stroke is drawn and in what colour: as asked, or, thinner
    /// than a device pixel, one device pixel wide with the alpha lowered in
    /// proportion, so it covers what the thin stroke would have.
    /// </summary>
    private static (float HalfWidth, Vector4 Ink) StrokeProfile(float thickness, Vector4 color, float pixel) =>
        thickness >= pixel
            ? (thickness * 0.5f, color)
            : (pixel * 0.5f, color with { W = color.W * (thickness / pixel) });

    /// <summary>
    /// Four rings from outside in -- outer fade, outer edge, inner edge,
    /// inner fade -- joined into the two fringes and, when the stroke is
    /// wider than its fringes, the solid core between them.
    /// </summary>
    private static void StrokeRings(
        ReadOnlySpan<Vector2> rings, int points, Vector4 ink, bool hasCore, List<UiColorVertex> output)
    {
        Span<Vector4> solid = stackalloc Vector4[points];
        Span<Vector4> clear = stackalloc Vector4[points];
        solid.Fill(ink);
        clear.Fill(Transparent(ink));
        ReadOnlySpan<Vector2> outerFade = rings.Slice(0, points);
        ReadOnlySpan<Vector2> outerEdge = rings.Slice(points, points);
        ReadOnlySpan<Vector2> innerEdge = rings.Slice(points * 2, points);
        ReadOnlySpan<Vector2> innerFade = rings.Slice(points * 3, points);
        Band(outerEdge, solid, outerFade, clear, output);
        if (hasCore) Band(innerEdge, solid, outerEdge, solid, output);
        Band(innerFade, clear, innerEdge, solid, output);
    }

    /// <summary>A convex shape given as its inner (solid) and outer (clear) rings.</summary>
    private static void FillRing(
        ReadOnlySpan<Vector2> inner, ReadOnlySpan<Vector2> outer, Vector4 color, List<UiColorVertex> output)
    {
        Span<Vector4> solid = stackalloc Vector4[inner.Length];
        Span<Vector4> clear = stackalloc Vector4[inner.Length];
        solid.Fill(color);
        clear.Fill(Transparent(color));
        Fan(inner, solid, output);
        Band(inner, solid, outer, clear, output);
    }

    /// <summary>A convex ring filled as a fan from its first point.</summary>
    private static void Fan(ReadOnlySpan<Vector2> ring, ReadOnlySpan<Vector4> colors, List<UiColorVertex> output)
    {
        for (int i = 1; i + 1 < ring.Length; i++)
        {
            output.Add(new UiColorVertex(ring[0], colors[0]));
            output.Add(new UiColorVertex(ring[i], colors[i]));
            output.Add(new UiColorVertex(ring[i + 1], colors[i + 1]));
        }
    }

    /// <summary>Two rings of the same length joined point for point, all the way round.</summary>
    private static void Band(
        ReadOnlySpan<Vector2> a, ReadOnlySpan<Vector4> colorsA,
        ReadOnlySpan<Vector2> b, ReadOnlySpan<Vector4> colorsB,
        List<UiColorVertex> output)
    {
        int count = a.Length;
        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            output.Add(new UiColorVertex(a[i], colorsA[i]));
            output.Add(new UiColorVertex(a[next], colorsA[next]));
            output.Add(new UiColorVertex(b[next], colorsB[next]));
            output.Add(new UiColorVertex(a[i], colorsA[i]));
            output.Add(new UiColorVertex(b[next], colorsB[next]));
            output.Add(new UiColorVertex(b[i], colorsB[i]));
        }
    }

    /// <summary>
    /// Chords per corner, from the largest radius any ring reaches there. A
    /// square corner is one point in every ring.
    /// </summary>
    private static void CornerSegments(CanvasCornerRadii radii, float grow, float pixel, Span<int> segments)
    {
        segments[0] = Corner(radii.TopLeft);
        segments[1] = Corner(radii.TopRight);
        segments[2] = Corner(radii.BottomRight);
        segments[3] = Corner(radii.BottomLeft);

        int Corner(float radius) => radius > 0f
            ? Math.Clamp(ArcSegments(radius + grow, MathF.PI * 0.5f, pixel), 1, MaximumSegmentsPerCorner)
            : 0;
    }

    private static int RingLength(ReadOnlySpan<int> segments) =>
        segments[0] + segments[1] + segments[2] + segments[3] + 4;

    /// <summary>
    /// The outline of the rectangle grown by <paramref name="grow"/> on every
    /// side (negative shrinks it, no further than to its middle), each
    /// rounded corner's radius grown alike and never below zero, clockwise on
    /// screen from the top-left corner. A square corner stays square.
    /// </summary>
    private static void RoundedRectRing(
        float x, float y, float width, float height, CanvasCornerRadii radii, float grow,
        ReadOnlySpan<int> segments, Span<Vector2> ring)
    {
        float left = x - grow, top = y - grow;
        float w = width + 2f * grow, h = height + 2f * grow;
        if (w < 0f) { left = x + width * 0.5f; w = 0f; }
        if (h < 0f) { top = y + height * 0.5f; h = 0f; }
        CanvasCornerRadii grown = ClampRadii(
            new CanvasCornerRadii(Grow(radii.TopLeft), Grow(radii.TopRight), Grow(radii.BottomRight), Grow(radii.BottomLeft)),
            w, h);

        int at = 0;
        Arc(left + grown.TopLeft, top + grown.TopLeft, grown.TopLeft, MathF.PI, segments[0], ring, ref at);
        Arc(left + w - grown.TopRight, top + grown.TopRight, grown.TopRight, MathF.PI * 1.5f, segments[1], ring, ref at);
        Arc(left + w - grown.BottomRight, top + h - grown.BottomRight, grown.BottomRight, 0f, segments[2], ring, ref at);
        Arc(left + grown.BottomLeft, top + h - grown.BottomLeft, grown.BottomLeft, MathF.PI * 0.5f, segments[3], ring, ref at);

        float Grow(float radius) => radius > 0f ? MathF.Max(radius + grow, 0f) : 0f;
    }

    /// <summary>A quarter turn clockwise on screen from <paramref name="start"/>, as segments + 1 points.</summary>
    private static void Arc(float cx, float cy, float radius, float start, int segments, Span<Vector2> ring, ref int at)
    {
        for (int i = 0; i <= segments; i++)
        {
            float angle = segments == 0 ? start : start + MathF.PI * 0.5f * i / segments;
            ring[at++] = new Vector2(cx + radius * MathF.Cos(angle), cy + radius * MathF.Sin(angle));
        }
    }

    private static int EllipseSegments(Vector2 radii, float grow, float pixel)
    {
        int segments = ArcSegments(MathF.Max(radii.X, radii.Y) + grow, MathF.PI * 2f, pixel);
        segments = Math.Clamp(segments, MinimumEllipseSegments, MaximumEllipseSegments);
        return (segments + 3) / 4 * 4;
    }

    /// <summary>The ellipse with both radii grown by <paramref name="grow"/> (never below zero), one point per slot.</summary>
    private static void EllipseRing(Vector2 centre, Vector2 radii, float grow, Span<Vector2> ring)
    {
        float rx = MathF.Max(radii.X + grow, 0f);
        float ry = MathF.Max(radii.Y + grow, 0f);
        for (int i = 0; i < ring.Length; i++)
        {
            float angle = MathF.PI * 2f * i / ring.Length;
            ring[i] = new Vector2(centre.X + rx * MathF.Cos(angle), centre.Y + ry * MathF.Sin(angle));
        }
    }

    private static bool SamePoint(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) <= 1e-8f;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector4 Transparent(Vector4 color) => color with { W = 0f };
}
