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
///
/// <para>Strokes grow both radii of an ellipse alike, so a strongly
/// eccentric ellipse's stroke is thinner along its flat sides.</para>
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
    /// nothing. Either winding is accepted. The fringe is half a pixel
    /// either side of the outline; see <see cref="FillConvexCore"/> for how the
    /// inside is pulled in and how a polygon too thin for it is handled.
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

        return FillConvexCore(corner, color, turn, pixel, output);
    }

    /// <summary>
    /// Fringes a convex polygon already merged and checked: the outer ring is
    /// the outline pushed out half a device pixel (bevelled where a mitre
    /// would reach past <see cref="MaximumMiter"/>), the inner ring the
    /// outline pulled in half a device pixel. The pull-in moves every edge's
    /// line inward; an edge that shrinks to nothing on the way drops out and
    /// its neighbours meet directly, so the inner ring never folds. A polygon
    /// too thin to pull in that far stops where it collapses to a point or a
    /// line, and is then mostly fringe, so its alpha is lowered until it
    /// covers its own area.
    /// </summary>
    private static CanvasShapeOutcome FillConvexCore(
        ReadOnlySpan<Vector2> corner, ReadOnlySpan<Vector4> color, int turn, float pixel, List<UiColorVertex> output)
    {
        int count = corner.Length;
        double half = pixel * 0.5;

        // Outward unit normal of each edge, corner i to corner i + 1.
        Span<double> normalX = stackalloc double[count];
        Span<double> normalY = stackalloc double[count];
        for (int i = 0; i < count; i++)
        {
            double ex = (double)corner[(i + 1) % count].X - corner[i].X;
            double ey = (double)corner[(i + 1) % count].Y - corner[i].Y;
            double length = Math.Sqrt(ex * ex + ey * ey);
            ex /= length;
            ey /= length;
            normalX[i] = turn > 0 ? ey : -ey;
            normalY[i] = turn > 0 ? -ex : ex;
        }

        // The polygon being pulled in: its corners, the edge leaving each, and
        // which of them each original corner has become.
        Span<double> x = stackalloc double[count];
        Span<double> y = stackalloc double[count];
        Span<int> leaving = stackalloc int[count];
        Span<int> becomes = stackalloc int[count];
        Span<double> miterX = stackalloc double[count];
        Span<double> miterY = stackalloc double[count];
        Span<double> shrink = stackalloc double[count];
        Span<bool> vanishes = stackalloc bool[count];
        for (int i = 0; i < count; i++)
        {
            x[i] = corner[i].X;
            y[i] = corner[i].Y;
            leaving[i] = i;
            becomes[i] = i;
        }

        int active = count;
        double pulled = 0;
        bool collapsed = false;
        while (true)
        {
            // Each corner moves along its mitre, and each end of an edge eats
            // tan(half the turn) of it for every pixel pulled in.
            for (int j = 0; j < active; j++)
            {
                int into = leaving[(j + active - 1) % active];
                int outOf = leaving[j];
                double ax = (normalX[into] + normalX[outOf]) * 0.5;
                double ay = (normalY[into] + normalY[outOf]) * 0.5;
                double lengthSquared = Math.Max(ax * ax + ay * ay, 1e-18);
                miterX[j] = ax / lengthSquared;
                miterY[j] = ay / lengthSquared;
                shrink[j] = Math.Sqrt(Math.Max(0, 1 - lengthSquared) / lengthSquared);
            }

            double soonest = double.MaxValue;
            for (int j = 0; j < active; j++)
            {
                int next = (j + 1) % active;
                double rate = shrink[j] + shrink[next];
                if (rate <= 0) continue;
                double length = Math.Sqrt((x[next] - x[j]) * (x[next] - x[j]) + (y[next] - y[j]) * (y[next] - y[j]));
                soonest = Math.Min(soonest, length / rate);
            }

            bool reaches = soonest >= half - pulled;
            double step = reaches ? half - pulled : soonest;
            for (int j = 0; j < active; j++)
            {
                x[j] -= miterX[j] * step;
                y[j] -= miterY[j] * step;
            }
            pulled += step;
            if (reaches) break;

            // Drop every edge that has just shrunk to nothing.
            int dropping = 0;
            for (int j = 0; j < active; j++)
            {
                int next = (j + 1) % active;
                double rate = shrink[j] + shrink[next];
                double length = Math.Sqrt((x[next] - x[j]) * (x[next] - x[j]) + (y[next] - y[j]) * (y[next] - y[j]));
                vanishes[j] = rate > 0 && length <= 1e-9 * (1 + Math.Abs(x[j]) + Math.Abs(y[j]));
                if (vanishes[j]) dropping++;
            }
            if (dropping == 0) break; // numerically stuck: stop where it is
            // Pulled in to a point or a line -- or, from four corners or
            // more, to a triangle: from there it would shrink along its own
            // length to a point and lose the thin shape's spine.
            if (active - dropping < 3 || (count > 3 && active - dropping == 3))
            {
                collapsed = true;
                break;
            }
            while (dropping > 0)
            {
                int j = 0;
                while (!vanishes[j]) j++;
                // Corner j and the next become one, which keeps the edge leaving the next.
                int next = (j + 1) % active;
                x[j] = (x[j] + x[next]) * 0.5;
                y[j] = (y[j] + y[next]) * 0.5;
                leaving[j] = leaving[next];
                vanishes[j] = vanishes[next];
                for (int i = 0; i < count; i++)
                    if (becomes[i] == next) becomes[i] = j;
                for (int k = next; k + 1 < active; k++)
                {
                    x[k] = x[k + 1];
                    y[k] = y[k + 1];
                    leaving[k] = leaving[k + 1];
                    vanishes[k] = vanishes[k + 1];
                }
                for (int i = 0; i < count; i++)
                    if (becomes[i] > next) becomes[i]--;
                active--;
                dropping--;
            }

            // Thinner now than what is left to pull in: going on would shrink
            // it along its length rather than across it. Stop and let the
            // alpha make up for the fringe.
            double activeArea = 0, perimeter = 0;
            for (int j = 0; j < active; j++)
            {
                int next = (j + 1) % active;
                activeArea += (x[j] - x[0]) * (y[next] - y[0]) - (x[next] - x[0]) * (y[j] - y[0]);
                perimeter += Math.Sqrt((x[next] - x[j]) * (x[next] - x[j]) + (y[next] - y[j]) * (y[next] - y[j]));
            }
            if (Math.Abs(activeArea) * 0.5 < perimeter * (half - pulled))
            {
                collapsed = true;
                break;
            }
        }

        // The rings, one or two points per original corner: a corner whose
        // mitre would reach too far is bevelled on the outside and repeated
        // on the inside, so the rings still pair point for point.
        Span<Vector2> inner = stackalloc Vector2[count * 2];
        Span<Vector2> outer = stackalloc Vector2[count * 2];
        Span<Vector4> solid = stackalloc Vector4[count * 2];
        Span<Vector4> clear = stackalloc Vector4[count * 2];
        int points = 0;
        for (int i = 0; i < count; i++)
        {
            int into = (i + count - 1) % count;
            var inside = new Vector2((float)x[becomes[i]], (float)y[becomes[i]]);
            double ax = (normalX[into] + normalX[i]) * 0.5;
            double ay = (normalY[into] + normalY[i]) * 0.5;
            double lengthSquared = ax * ax + ay * ay;
            if (lengthSquared * MaximumMiter * MaximumMiter >= 1)
            {
                Put(inner, outer, solid, clear, ref points, inside,
                    new Vector2((float)(corner[i].X + ax / lengthSquared * half), (float)(corner[i].Y + ay / lengthSquared * half)), color[i]);
            }
            else
            {
                Put(inner, outer, solid, clear, ref points, inside,
                    new Vector2((float)(corner[i].X + normalX[into] * half), (float)(corner[i].Y + normalY[into] * half)), color[i]);
                Put(inner, outer, solid, clear, ref points, inside,
                    new Vector2((float)(corner[i].X + normalX[i] * half), (float)(corner[i].Y + normalY[i] * half)), color[i]);
            }
        }

        int start = output.Count;
        Fan(inner[..points], solid[..points], output);
        Band(inner[..points], solid[..points], outer[..points], clear[..points], output);
        if (collapsed)
        {
            double area = 0;
            for (int i = 1; i + 1 < count; i++)
            {
                double ux = (double)corner[i].X - corner[0].X, uy = (double)corner[i].Y - corner[0].Y;
                double vx = (double)corner[i + 1].X - corner[0].X, vy = (double)corner[i + 1].Y - corner[0].Y;
                area += ux * vy - uy * vx;
            }
            ScaleToArea(output, start, Math.Abs(area) * 0.5);
        }
        return CanvasShapeOutcome.Drawn;
    }

    private static void Put(
        Span<Vector2> inner, Span<Vector2> outer, Span<Vector4> solid, Span<Vector4> clear, ref int points,
        Vector2 inside, Vector2 outside, Vector4 ink)
    {
        inner[points] = inside;
        outer[points] = outside;
        solid[points] = ink;
        clear[points] = Transparent(ink);
        points++;
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
        int start = output.Count;
        FillRing(inner, outer, color, output);
        if (width <= pixel || height <= pixel)
        {
            double corners = (double)radii.TopLeft * radii.TopLeft + (double)radii.TopRight * radii.TopRight
                + (double)radii.BottomRight * radii.BottomRight + (double)radii.BottomLeft * radii.BottomLeft;
            ScaleToArea(output, start, (double)width * height - (4 - Math.PI) / 4 * corners);
        }
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
        int start = output.Count;
        FillRing(inner, outer, color, output);
        if (radii.X <= half || radii.Y <= half) ScaleToArea(output, start, Math.PI * radii.X * radii.Y);
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

    /// <summary>
    /// Weakens the triangles written from <paramref name="start"/> on, all by
    /// one factor, until what they cover weighted by alpha is
    /// <paramref name="area"/>. A fill narrower than its fringe has no solid
    /// core, and its two fringes alone would cover more than the shape does.
    /// Never raises alpha.
    /// </summary>
    private static void ScaleToArea(List<UiColorVertex> output, int start, double area)
    {
        double covered = 0;
        for (int i = start; i + 2 < output.Count; i += 3)
        {
            Vector2 a = output[i].Position, b = output[i + 1].Position, c = output[i + 2].Position;
            double triangle = Math.Abs((b.X - a.X) * (double)(c.Y - a.Y) - (c.X - a.X) * (double)(b.Y - a.Y)) / 2;
            covered += triangle * (output[i].Color.W + output[i + 1].Color.W + output[i + 2].Color.W) / 3;
        }
        if (!(covered > area) || !(covered > 0)) return;
        float factor = (float)(area / covered);
        for (int i = start; i < output.Count; i++)
        {
            UiColorVertex vertex = output[i];
            output[i] = vertex with { Color = vertex.Color with { W = vertex.Color.W * factor } };
        }
    }

    private static bool SamePoint(Vector2 a, Vector2 b) => Vector2.DistanceSquared(a, b) <= 1e-8f;

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static Vector4 Transparent(Vector4 color) => color with { W = 0f };
}
