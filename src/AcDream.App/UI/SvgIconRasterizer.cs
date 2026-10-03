using AcDream.Core.Plugins;
using StbTrueTypeSharp;

namespace AcDream.App.UI;

/// <summary>
/// Bakes a parsed SVG icon into a square single-channel coverage bitmap with
/// StbTrueType's outline rasterizer, the one the canvas fonts use. Each paint
/// layer is rasterized on its own and composited in document order, so a
/// fill's hole cannot cancel a stroke that crosses it. Coordinates go to stb
/// as Int16 in 1/64 device pixels.
/// </summary>
internal static class SvgIconRasterizer
{
    internal const int MaximumSize = 128;
    internal const int MaximumPoints = SvgStroker.DefaultPointBudget;
    private const float Flatness = 0.35f;
    private const double StrokeTolerance = 0.2;
    private const double SubPixels = 64;
    private const double Clamp = 500; // device px; ±500 × 64 stays inside Int16

    /// <summary>The icon fitted into <paramref name="size"/>² device pixels, centred
    /// (<c>xMidYMid meet</c>), or null when it needs more than
    /// <see cref="MaximumPoints"/> flattened points or overflows device space.</summary>
    internal static byte[]? Bake(SvgIconDocument document, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaximumSize);

        double scale = size / Math.Max(document.Width, document.Height);
        var fit = new SvgMatrix(scale, 0, 0, scale,
            (size - document.Width * scale) / 2 - document.MinX * scale,
            (size - document.Height * scale) / 2 - document.MinY * scale);

        var result = new byte[size * size];
        var layer = new byte[size * size];
        long budget = MaximumPoints;
        foreach (SvgPaintLayer paint in document.Layers)
        {
            List<Vertex>? vertices = paint.Kind == SvgPaintKind.Fill
                ? FillVertices(paint, fit)
                : StrokeVertices(paint, fit, (int)Math.Max(budget, 0));
            if (vertices is null) return null; // stroker over budget
            if (!AllFinite(vertices)) return null;
            budget -= Cost(vertices);
            if (budget < 0) return null;
            if (vertices.Count == 0) continue;

            Array.Clear(layer);
            Rasterize(vertices, layer, size);
            double opacity = Math.Clamp(paint.Opacity, 0, 1);
            for (int i = 0; i < result.Length; i++)
            {
                if (layer[i] == 0) continue;
                double c = result[i] / 255.0, a = layer[i] / 255.0 * opacity;
                result[i] = (byte)Math.Round((c + a * (1 - c)) * 255);
            }
        }
        return result;
    }

    /// <summary>One stb vertex before it is pinned: kind, end point and controls, in device pixels.</summary>
    private readonly record struct Vertex(byte Type, SvgPoint P, SvgPoint C, SvgPoint C1);

    private const byte VMove = 1, VLine = 2, VCurve = 3, VCubic = 4;

    private static List<Vertex> FillVertices(SvgPaintLayer paint, SvgMatrix fit)
    {
        SvgMatrix m = fit.Then(paint.Transform);
        var vertices = new List<Vertex>();
        foreach (SvgSubpath path in paint.Subpaths)
        {
            if (path.Segments.Count == 0) continue;
            vertices.Add(new(VMove, m.Apply(path.Start), default, default));
            foreach (SvgSegment s in path.Segments)
            {
                vertices.Add(s.Kind switch
                {
                    SvgSegmentKind.Line => new(VLine, m.Apply(s.End), default, default),
                    SvgSegmentKind.Quadratic => new(VCurve, m.Apply(s.End), m.Apply(s.C1), default),
                    _ => new(VCubic, m.Apply(s.End), m.Apply(s.C1), m.Apply(s.C2)),
                });
            }
        }
        return vertices;
    }

    private static List<Vertex>? StrokeVertices(SvgPaintLayer paint, SvgMatrix fit, int budget)
    {
        List<SvgPoint[]>? polygons = SvgStroker.Expand(paint, fit, StrokeTolerance, Math.Max(budget, 0));
        if (polygons is null) return null;
        var vertices = new List<Vertex>();
        foreach (SvgPoint[] polygon in polygons)
        {
            vertices.Add(new(VMove, polygon[0], default, default));
            for (int i = 1; i < polygon.Length; i++) vertices.Add(new(VLine, polygon[i], default, default));
        }
        return vertices;
    }

    /// <summary>False when any device-space coordinate overflowed to infinity or NaN
    /// (finite input under a finite transform can still do that).</summary>
    private static bool AllFinite(List<Vertex> vertices)
    {
        foreach (Vertex v in vertices)
        {
            if (!double.IsFinite(v.P.X) || !double.IsFinite(v.P.Y)
                || !double.IsFinite(v.C.X) || !double.IsFinite(v.C.Y)
                || !double.IsFinite(v.C1.X) || !double.IsFinite(v.C1.Y)) return false;
        }
        return true;
    }

    /// <summary>An upper bound on the points stb flattens these vertices into: one per
    /// line or move, and for a curve 2·sqrt(control polygon length / flatness) + 2.
    /// Exact upper bound for quadratics (stb quarters the error per halving); for cubics
    /// stb's length test is heuristic, and this bound held with at least 11% margin over
    /// ~20M sampled curves.</summary>
    private static long Cost(List<Vertex> vertices)
    {
        long cost = 0;
        SvgPoint previous = default;
        foreach (Vertex v in vertices)
        {
            if (v.Type == VCurve)
            {
                double length = (v.C - previous).Length + (v.P - v.C).Length;
                cost += Curve(length);
            }
            else if (v.Type == VCubic)
            {
                double length = (v.C - previous).Length + (v.C1 - v.C).Length + (v.P - v.C1).Length;
                cost += Curve(length);
            }
            else cost++;
            previous = v.P;
        }
        return cost;
    }

    private static long Curve(double length) =>
        (long)Math.Min(Math.Ceiling(2 * Math.Sqrt(length / Flatness)), 1e9) + 2;

    private static short Fixed(double v) => (short)Math.Round(Math.Clamp(v, -Clamp, Clamp) * SubPixels);

    private static unsafe void Rasterize(List<Vertex> vertices, byte[] into, int size)
    {
        var pinned = new StbTrueType.stbtt_vertex[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            Vertex v = vertices[i];
            pinned[i].type = v.Type;
            pinned[i].x = Fixed(v.P.X);
            pinned[i].y = Fixed(v.P.Y);
            pinned[i].cx = Fixed(v.C.X);
            pinned[i].cy = Fixed(v.C.Y);
            pinned[i].cx1 = Fixed(v.C1.X);
            pinned[i].cy1 = Fixed(v.C1.Y);
        }

        fixed (StbTrueType.stbtt_vertex* vp = pinned)
        fixed (byte* pixels = into)
        {
            StbTrueType.stbtt__bitmap bitmap;
            bitmap.w = size;
            bitmap.h = size;
            bitmap.stride = size;
            bitmap.pixels = pixels;
            StbTrueType.stbtt_Rasterize(&bitmap, Flatness, vp, pinned.Length,
                (float)(1 / SubPixels), (float)(1 / SubPixels), 0, 0, 0, 0, 0, null, false);
        }
    }
}
