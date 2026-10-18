using System;
using System.Numerics;

namespace AcDream.App.Rendering;

/// <summary>
/// One corner of an untextured triangle in screen pixels, with its own
/// colour; the rasteriser blends the colours across the triangle.
/// </summary>
internal readonly record struct UiColorVertex(Vector2 Position, Vector4 Color);

/// <summary>
/// Trims one coloured triangle to an axis-aligned clip rectangle, carrying
/// each corner's colour along with its position, so a gradient or an
/// anti-aliased fringe cut by the clip keeps the colour it had at the cut.
///
/// <para>Most triangles are wholly inside or wholly outside, and those are
/// answered without clipping. The rest go through the same
/// Sutherland-Hodgman walk as <see cref="TransformedQuadClipper"/>: one pass
/// per clip edge, each adding at most one corner, so a triangle comes out
/// with at most seven.</para>
/// </summary>
internal static class ColoredTriangleClipper
{
    /// <summary>The most corners a clipped triangle can have: three, plus one per clip edge.</summary>
    public const int MaxClippedVertices = 7;

    /// <summary>
    /// Writes the part of the triangle inside the clip rectangle into
    /// <paramref name="destination"/> as a convex polygon and returns how
    /// many corners it took; zero means nothing is left.
    /// <paramref name="destination"/> must hold <see cref="MaxClippedVertices"/>.
    /// </summary>
    public static int Clip(
        float clipLeft,
        float clipTop,
        float clipRight,
        float clipBottom,
        in UiColorVertex a,
        in UiColorVertex b,
        in UiColorVertex c,
        Span<UiColorVertex> destination)
    {
        if (destination.Length < MaxClippedVertices)
            throw new ArgumentException(
                $"A clipped triangle needs room for {MaxClippedVertices} corners.", nameof(destination));
        if (clipRight <= clipLeft || clipBottom <= clipTop)
            return 0;

        Vector2 pa = a.Position, pb = b.Position, pc = c.Position;
        if ((pa.X < clipLeft && pb.X < clipLeft && pc.X < clipLeft)
            || (pa.X > clipRight && pb.X > clipRight && pc.X > clipRight)
            || (pa.Y < clipTop && pb.Y < clipTop && pc.Y < clipTop)
            || (pa.Y > clipBottom && pb.Y > clipBottom && pc.Y > clipBottom))
            return 0;

        destination[0] = a;
        destination[1] = b;
        destination[2] = c;
        if (Inside(pa) && Inside(pb) && Inside(pc))
            return 3;

        Span<UiColorVertex> scratch = stackalloc UiColorVertex[MaxClippedVertices];
        int count = ClipAgainstEdge(ClipEdge.Left, clipLeft, destination, 3, scratch);
        if (count == 0) return 0;
        count = ClipAgainstEdge(ClipEdge.Top, clipTop, scratch, count, destination);
        if (count == 0) return 0;
        count = ClipAgainstEdge(ClipEdge.Right, clipRight, destination, count, scratch);
        if (count == 0) return 0;
        return ClipAgainstEdge(ClipEdge.Bottom, clipBottom, scratch, count, destination);

        bool Inside(Vector2 p) =>
            p.X >= clipLeft && p.X <= clipRight && p.Y >= clipTop && p.Y <= clipBottom;
    }

    private enum ClipEdge
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    /// <summary>
    /// Keeps the part of the outline on the inside of one clip edge, emitting
    /// the crossing point wherever consecutive corners straddle it.
    /// </summary>
    private static int ClipAgainstEdge(
        ClipEdge edge, float boundary, ReadOnlySpan<UiColorVertex> input, int count, Span<UiColorVertex> output)
    {
        int written = 0;
        for (int index = 0; index < count; index++)
        {
            UiColorVertex current = input[index];
            UiColorVertex previous = input[(index + count - 1) % count];
            float currentDistance = SignedDistance(edge, boundary, current.Position);
            float previousDistance = SignedDistance(edge, boundary, previous.Position);
            bool currentInside = currentDistance >= 0f;
            bool previousInside = previousDistance >= 0f;

            if (currentInside)
            {
                if (!previousInside)
                    output[written++] = Crossing(previous, current, previousDistance, currentDistance);
                output[written++] = current;
            }
            else if (previousInside)
            {
                output[written++] = Crossing(previous, current, previousDistance, currentDistance);
            }
        }
        return written;
    }

    /// <summary>How far a point sits on the inside of one clip edge; negative is outside.</summary>
    private static float SignedDistance(ClipEdge edge, float boundary, Vector2 position)
        => edge switch
        {
            ClipEdge.Left => position.X - boundary,
            ClipEdge.Top => position.Y - boundary,
            ClipEdge.Right => boundary - position.X,
            _ => boundary - position.Y,
        };

    private static UiColorVertex Crossing(
        in UiColorVertex from, in UiColorVertex to, float fromDistance, float toDistance)
    {
        float span = fromDistance - toDistance;
        // The ends straddle the edge, so the span is not zero; the guard is
        // for the degenerate input where both distances are exactly zero.
        float t = MathF.Abs(span) > float.Epsilon ? fromDistance / span : 0f;
        return new UiColorVertex(
            Vector2.Lerp(from.Position, to.Position, t),
            Vector4.Lerp(from.Color, to.Color, t));
    }
}
