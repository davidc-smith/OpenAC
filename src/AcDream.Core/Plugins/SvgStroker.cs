namespace AcDream.Core.Plugins;

/// <summary>
/// Turns a stroke into closed polygons that a nonzero fill draws as the
/// stroke: one rectangle per flattened edge, plus joins and caps. The pieces
/// overlap, and every one is turned to the same winding, so they add up
/// instead of cancelling. Work happens in the element's own units and is
/// transformed afterwards, so a non-uniform scale or a skew gives the stroke
/// SVG would draw. Pure: no I/O, no GL.
/// </summary>
public static class SvgStroker
{
    private const double Epsilon = 1e-9;

    /// <summary>Default cap on generated points (flattened points plus piece vertices),
    /// matching the 65,536 points per bake limit.</summary>
    public const int DefaultPointBudget = 65_536;

    /// <summary>Expands <paramref name="layer"/>'s stroke into polygons in the space
    /// <paramref name="toDevice"/> maps the icon's viewBox to, flattened to within
    /// <paramref name="tolerance"/> of that space's units. Returns null when the work would
    /// generate more than <paramref name="maxPoints"/> points (flattened points plus piece
    /// vertices); generation stops as soon as the budget is exceeded. Also returns null when a
    /// transformed piece is not finite (the icon overflowed device space); pieces of
    /// near-zero area are skipped.</summary>
    public static List<SvgPoint[]>? Expand(SvgPaintLayer layer, SvgMatrix toDevice, double tolerance,
        int maxPoints = DefaultPointBudget)
    {
        long used = 0;
        ArgumentNullException.ThrowIfNull(layer.Stroke);
        SvgStrokeStyle style = layer.Stroke;
        SvgMatrix m = toDevice.Then(layer.Transform);
        double local = tolerance / Math.Max(m.MaximumScale, Epsilon);
        double h = style.Width / 2;
        var pieces = new List<SvgPoint[]>();
        bool Add(SvgPoint[] piece)
        {
            used += piece.Length;
            if (used > maxPoints) return false;
            pieces.Add(piece);
            return true;
        }

        foreach (SvgSubpath subpath in layer.Subpaths)
        {
            if (subpath.Segments.Count == 0 && !subpath.Closed) continue; // a lone moveto is never stroked
            List<SvgPoint>? flat = FlattenBudgeted(subpath, local, ref used, maxPoints);
            if (flat is null) return null;
            List<SvgPoint> points = Distinct(flat, subpath.Closed);
            if (points.Count == 1)
            {
                SvgPoint p = points[0];
                if (style.Cap == SvgLineCap.Round) { if (!Add(Circle(p, h, local))) return null; }
                else if (style.Cap == SvgLineCap.Square)
                {
                    if (!Add([new(p.X - h, p.Y - h), new(p.X + h, p.Y - h), new(p.X + h, p.Y + h), new(p.X - h, p.Y + h)])) return null;
                }
                continue;
            }

            bool closed = subpath.Closed; // closed subpaths get joins and never caps
            int edgeCount = closed ? points.Count : points.Count - 1;
            for (int i = 0; i < edgeCount; i++)
            {
                SvgPoint a = points[i], b = points[(i + 1) % points.Count];
                if (!closed && style.Cap == SvgLineCap.Square)
                {
                    if (i == 0) a -= Unit(b - a) * h;
                    if (i == edgeCount - 1) b += Unit(b - a) * h;
                }
                SvgPoint n = Normal(b - a) * h;
                if (!Add([a + n, b + n, b - n, a - n])) return null;
            }

            int joins = closed ? points.Count : points.Count - 2;
            for (int j = 0; j < joins; j++)
            {
                int at = closed ? j : j + 1;
                SvgPoint v = points[at];
                SvgPoint before = points[(at - 1 + points.Count) % points.Count];
                SvgPoint after = points[(at + 1) % points.Count];
                if (Join(v, before, after, h, style, local) is { } join && !Add(join)) return null;
            }

            if (!closed && style.Cap == SvgLineCap.Round)
            {
                if (!Add(Circle(points[0], h, local))) return null;
                if (!Add(Circle(points[^1], h, local))) return null;
            }
        }

        var result = new List<SvgPoint[]>(pieces.Count);
        foreach (SvgPoint[] piece in pieces)
        {
            for (int i = 0; i < piece.Length; i++) piece[i] = m.Apply(piece[i]);
            double area = SignedArea(piece);
            if (!double.IsFinite(area)) return null; // the icon overflowed device space
            if (Math.Abs(area) < Epsilon) continue;
            if (area < 0) Array.Reverse(piece);
            result.Add(piece);
        }
        return result;
    }

    /// <summary>A subpath as points, each curve cut into chords no further than
    /// <paramref name="tolerance"/> from it.</summary>
    public static List<SvgPoint> Flatten(SvgSubpath subpath, double tolerance)
    {
        long used = 0;
        return FlattenBudgeted(subpath, tolerance, ref used, long.MaxValue)!;
    }

    private static List<SvgPoint>? FlattenBudgeted(SvgSubpath subpath, double tolerance, ref long used, long max)
    {
        var points = new List<SvgPoint> { subpath.Start };
        SvgPoint current = subpath.Start;
        foreach (SvgSegment s in subpath.Segments)
        {
            switch (s.Kind)
            {
                case SvgSegmentKind.Line:
                    points.Add(s.End);
                    if (++used > max) return null;
                    break;
                case SvgSegmentKind.Quadratic:
                {
                    double dd = (current - s.C1 * 2 + s.End).Length;
                    int n = Steps(dd / 4, tolerance);
                    used += n;
                    if (used > max) return null;
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n, u = 1 - t;
                        points.Add(current * (u * u) + s.C1 * (2 * u * t) + s.End * (t * t));
                    }
                    break;
                }
                case SvgSegmentKind.Cubic:
                {
                    double dd = Math.Max((current - s.C1 * 2 + s.C2).Length, (s.C1 - s.C2 * 2 + s.End).Length);
                    int n = Steps(dd * 0.75, tolerance);
                    used += n;
                    if (used > max) return null;
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n, u = 1 - t;
                        points.Add(current * (u * u * u) + s.C1 * (3 * u * u * t) + s.C2 * (3 * u * t * t) + s.End * (t * t * t));
                    }
                    break;
                }
            }
            current = s.End;
        }
        return points;
    }

    /// <summary>Shoelace area, positive clockwise when y points down.</summary>
    public static double SignedArea(IReadOnlyList<SvgPoint> polygon)
    {
        double sum = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            SvgPoint a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum / 2;
    }

    // The 512-chord and 128-point clamps keep work bounded; they only exceed the 0.2 px
    // tolerance beyond the 128 px bake cap.
    private static int Steps(double bound, double tolerance) =>
        Math.Clamp((int)Math.Ceiling(Math.Sqrt(bound / Math.Max(tolerance, Epsilon))), 1, 512);

    private static List<SvgPoint> Distinct(List<SvgPoint> points, bool closed)
    {
        var result = new List<SvgPoint>(points.Count);
        foreach (SvgPoint p in points)
        {
            if (result.Count == 0 || (p - result[^1]).Length > Epsilon) result.Add(p);
        }
        if (closed && result.Count > 1 && (result[0] - result[^1]).Length <= Epsilon) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static SvgPoint Unit(SvgPoint d) => d * (1 / d.Length);

    /// <summary>The unit normal to the left of <paramref name="d"/>, in y-up terms.</summary>
    private static SvgPoint Normal(SvgPoint d)
    {
        SvgPoint u = Unit(d);
        return new SvgPoint(-u.Y, u.X);
    }

    private static SvgPoint[]? Join(SvgPoint v, SvgPoint before, SvgPoint after, double h, SvgStrokeStyle style, double tolerance)
    {
        SvgPoint d1 = Unit(v - before), d2 = Unit(after - v);
        double cross = d1.X * d2.Y - d1.Y * d2.X;
        double dot = d1.X * d2.X + d1.Y * d2.Y;
        if (Math.Abs(cross) < 1e-12 && dot > 0) return null; // straight on: the rectangles already meet

        if (style.Join == SvgLineJoin.Round) return Circle(v, h, tolerance);
        if (Math.Abs(cross) < 1e-12) return null; // a full reversal: nothing to bevel

        double side = cross > 0 ? -1 : 1;
        SvgPoint n1 = Normal(d1), n2 = Normal(d2);
        SvgPoint o1 = v + n1 * (h * side), o2 = v + n2 * (h * side);
        if (style.Join == SvgLineJoin.Miter)
        {
            SvgPoint sum = n1 + n2;
            double ratio = 2 / sum.Length; // 1 / sin(θ/2), θ the angle between the segments
            if (ratio <= style.MiterLimit)
                return [v, o1, v + sum * (h * side * 2 / (sum.X * sum.X + sum.Y * sum.Y)), o2];
        }
        return [v, o1, o2];
    }

    private static SvgPoint[] Circle(SvgPoint c, double r, double tolerance)
    {
        double ratio = Math.Clamp(1 - tolerance / Math.Max(r, Epsilon), -1, 1);
        int n = Math.Clamp((int)Math.Ceiling(Math.PI / Math.Acos(ratio)), 8, 128);
        var points = new SvgPoint[n];
        for (int i = 0; i < n; i++)
        {
            double a = 2 * Math.PI * i / n;
            points[i] = new SvgPoint(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }
        return points;
    }
}
