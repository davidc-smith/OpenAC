using System.Globalization;

namespace AcDream.Core.Plugins;

/// <summary>A point in an icon's own user units, or in device pixels once transformed.</summary>
public readonly record struct SvgPoint(double X, double Y)
{
    public static SvgPoint operator +(SvgPoint a, SvgPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static SvgPoint operator -(SvgPoint a, SvgPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static SvgPoint operator *(SvgPoint a, double s) => new(a.X * s, a.Y * s);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

/// <summary>
/// An SVG affine transform, <c>matrix(a b c d e f)</c>: x' = a·x + c·y + e,
/// y' = b·x + d·y + f.
/// </summary>
public readonly record struct SvgMatrix(double A, double B, double C, double D, double E, double F)
{
    public static SvgMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>This transform applied after <paramref name="inner"/>.</summary>
    public SvgMatrix Then(SvgMatrix inner) => new(
        A * inner.A + C * inner.B,
        B * inner.A + D * inner.B,
        A * inner.C + C * inner.D,
        B * inner.C + D * inner.D,
        A * inner.E + C * inner.F + E,
        B * inner.E + D * inner.F + F);

    public SvgPoint Apply(SvgPoint p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    public double Determinant => A * D - B * C;

    /// <summary>The largest factor this transform stretches any length by.</summary>
    public double MaximumScale
    {
        get
        {
            double p = A * A + B * B + C * C + D * D;
            double det = Determinant;
            double root = Math.Sqrt(Math.Max(0, p * p - 4 * det * det));
            return Math.Sqrt((p + root) / 2);
        }
    }
}

public enum SvgSegmentKind { Line, Quadratic, Cubic }

/// <summary>One piece of a subpath from the previous end point. A quadratic uses
/// <see cref="C1"/> only; a line uses neither control point.</summary>
public readonly record struct SvgSegment(SvgSegmentKind Kind, SvgPoint C1, SvgPoint C2, SvgPoint End)
{
    public static SvgSegment Line(SvgPoint end) => new(SvgSegmentKind.Line, default, default, end);
    public static SvgSegment Quadratic(SvgPoint c, SvgPoint end) => new(SvgSegmentKind.Quadratic, c, default, end);
    public static SvgSegment Cubic(SvgPoint c1, SvgPoint c2, SvgPoint end) => new(SvgSegmentKind.Cubic, c1, c2, end);
}

public sealed record SvgSubpath(SvgPoint Start, IReadOnlyList<SvgSegment> Segments, bool Closed);

/// <summary>
/// Turns SVG path data and the basic shapes into subpaths of lines,
/// quadratics and cubics. Arcs become cubics of at most 90° each (SVG 1.1
/// implementation notes F.6.5 and F.6.6). Pure: no I/O, no GL.
/// </summary>
public static class SvgOutline
{
    /// <summary>The cubic control distance for a quarter circle of radius 1.</summary>
    public const double Kappa = 0.5522847498307936;

    /// <summary>Parses <paramref name="d"/>. <paramref name="commands"/> counts every command,
    /// implicit repeats included, so a caller can hold a whole file to a limit.</summary>
    public static bool TryParsePath(string d, List<SvgSubpath> into, ref int commands, int maximumCommands, out string? reason)
    {
        var reader = new PathReader(d);
        var segments = new List<SvgSegment>();
        SvgPoint current = default, start = default, lastControl = default;
        char last = ' ';
        bool open = false;

        void Flush(bool closed)
        {
            if (open) into.Add(new SvgSubpath(start, segments.ToArray(), closed));
            segments.Clear();
            open = false;
        }

        void Begin()
        {
            if (open) return;
            start = current;
            open = true;
        }

        reader.SkipWhitespace();
        if (reader.AtEnd) { reason = null; return true; }
        if (!reader.TryCommand(out char command) || (command is not ('M' or 'm')))
        {
            reason = $"path data must start with a moveto, at offset {reader.Offset}";
            return false;
        }

        while (true)
        {
            bool relative = char.IsLower(command);
            char upper = char.ToUpperInvariant(command);
            bool first = true;
            do
            {
                if (++commands > maximumCommands)
                {
                    reason = $"has more than {maximumCommands} path commands";
                    return false;
                }

                int before = segments.Count;
                SvgPoint origin = relative ? current : default;
                switch (upper)
                {
                    case 'M':
                    {
                        if (!reader.TryPoint(out SvgPoint p)) return Malformed(reader, out reason);
                        if (first)
                        {
                            Flush(false);
                            current = origin + p;
                            Begin();
                        }
                        else
                        {
                            Begin();
                            current = origin + p;
                            segments.Add(SvgSegment.Line(current));
                        }
                        lastControl = current;
                        break;
                    }
                    case 'L':
                    {
                        if (!reader.TryPoint(out SvgPoint p)) return Malformed(reader, out reason);
                        Begin();
                        current = origin + p;
                        segments.Add(SvgSegment.Line(current));
                        lastControl = current;
                        break;
                    }
                    case 'H':
                    {
                        if (!reader.TryNumber(out double x)) return Malformed(reader, out reason);
                        Begin();
                        current = new SvgPoint(relative ? current.X + x : x, current.Y);
                        segments.Add(SvgSegment.Line(current));
                        lastControl = current;
                        break;
                    }
                    case 'V':
                    {
                        if (!reader.TryNumber(out double y)) return Malformed(reader, out reason);
                        Begin();
                        current = new SvgPoint(current.X, relative ? current.Y + y : y);
                        segments.Add(SvgSegment.Line(current));
                        lastControl = current;
                        break;
                    }
                    case 'C':
                    {
                        if (!reader.TryPoint(out SvgPoint c1) || !reader.TryPoint(out SvgPoint c2)
                            || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        segments.Add(SvgSegment.Cubic(origin + c1, origin + c2, origin + p));
                        lastControl = origin + c2;
                        current = origin + p;
                        break;
                    }
                    case 'S':
                    {
                        if (!reader.TryPoint(out SvgPoint c2) || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        SvgPoint c1 = last is 'C' or 'S' ? current + (current - lastControl) : current;
                        segments.Add(SvgSegment.Cubic(c1, origin + c2, origin + p));
                        lastControl = origin + c2;
                        current = origin + p;
                        break;
                    }
                    case 'Q':
                    {
                        if (!reader.TryPoint(out SvgPoint c) || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        segments.Add(SvgSegment.Quadratic(origin + c, origin + p));
                        lastControl = origin + c;
                        current = origin + p;
                        break;
                    }
                    case 'T':
                    {
                        if (!reader.TryPoint(out SvgPoint p)) return Malformed(reader, out reason);
                        Begin();
                        SvgPoint c = last is 'Q' or 'T' ? current + (current - lastControl) : current;
                        segments.Add(SvgSegment.Quadratic(c, origin + p));
                        lastControl = c;
                        current = origin + p;
                        break;
                    }
                    case 'A':
                    {
                        if (!reader.TryNumber(out double rx) || !reader.TryNumber(out double ry)
                            || !reader.TryNumber(out double rotation)
                            || !reader.TryFlag(out bool large) || !reader.TryFlag(out bool sweep)
                            || !reader.TryPoint(out SvgPoint p))
                            return Malformed(reader, out reason);
                        Begin();
                        SvgPoint end = origin + p;
                        AppendArc(segments, current, Math.Abs(rx), Math.Abs(ry), rotation, large, sweep, end);
                        current = end;
                        lastControl = current;
                        break;
                    }
                    case 'Z':
                    {
                        if (open) Flush(true);
                        current = start;
                        lastControl = current;
                        break;
                    }
                    default:
                        reason = $"path data has an unknown command '{command}' at offset {reader.Offset}";
                        return false;
                }

                bool finite = double.IsFinite(current.X) && double.IsFinite(current.Y);
                for (int i = Math.Min(before, segments.Count); finite && i < segments.Count; i++)
                {
                    SvgSegment seg = segments[i];
                    finite = IsFinite(seg.End) && IsFinite(seg.C1) && IsFinite(seg.C2);
                }
                if (!finite)
                {
                    reason = $"path data has a non-finite coordinate at offset {reader.Offset}";
                    return false;
                }

                last = upper;
                first = false;
                if (upper == 'M') last = 'L';
            }
            while (upper != 'Z' && reader.AtNumberStart);

            reader.SkipWhitespace();
            if (reader.AtEnd) break;
            if (!reader.TryCommand(out command))
                return Malformed(reader, out reason);
        }

        Flush(false);
        reason = null;
        return true;
    }

    private static bool IsFinite(SvgPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y);

    private static bool Malformed(PathReader reader, out string? reason)
    {
        reason = $"path data is malformed at offset {reader.Offset}";
        return false;
    }

    /// <summary>An ellipse as four cubics, clockwise on screen from its rightmost point.</summary>
    public static SvgSubpath Ellipse(double cx, double cy, double rx, double ry)
    {
        double kx = rx * Kappa, ky = ry * Kappa;
        var right = new SvgPoint(cx + rx, cy);
        var bottom = new SvgPoint(cx, cy + ry);
        var left = new SvgPoint(cx - rx, cy);
        var top = new SvgPoint(cx, cy - ry);
        return new SvgSubpath(right,
        [
            SvgSegment.Cubic(new(cx + rx, cy + ky), new(cx + kx, cy + ry), bottom),
            SvgSegment.Cubic(new(cx - kx, cy + ry), new(cx - rx, cy + ky), left),
            SvgSegment.Cubic(new(cx - rx, cy - ky), new(cx - kx, cy - ry), top),
            SvgSegment.Cubic(new(cx + kx, cy - ry), new(cx + rx, cy - ky), right),
        ], true);
    }

    /// <summary>A rectangle, with corner radii already clamped to half its sides.</summary>
    public static SvgSubpath Rect(double x, double y, double w, double h, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0)
        {
            return new SvgSubpath(new(x, y),
            [
                SvgSegment.Line(new(x + w, y)), SvgSegment.Line(new(x + w, y + h)),
                SvgSegment.Line(new(x, y + h)), SvgSegment.Line(new(x, y)),
            ], true);
        }

        double kx = rx * (1 - Kappa), ky = ry * (1 - Kappa);
        double r = x + w, b = y + h;
        return new SvgSubpath(new(x + rx, y),
        [
            SvgSegment.Line(new(r - rx, y)),
            SvgSegment.Cubic(new(r - kx, y), new(r, y + ky), new(r, y + ry)),
            SvgSegment.Line(new(r, b - ry)),
            SvgSegment.Cubic(new(r, b - ky), new(r - kx, b), new(r - rx, b)),
            SvgSegment.Line(new(x + rx, b)),
            SvgSegment.Cubic(new(x + kx, b), new(x, b - ky), new(x, b - ry)),
            SvgSegment.Line(new(x, y + ry)),
            SvgSegment.Cubic(new(x, y + ky), new(x + kx, y), new(x + rx, y)),
        ], true);
    }

    /// <summary>A polyline or polygon from its points.</summary>
    public static SvgSubpath Poly(IReadOnlyList<SvgPoint> points, bool closed)
    {
        if (points.Count == 0) throw new ArgumentException("A polyline needs at least one point.", nameof(points));
        var segments = new SvgSegment[points.Count - 1];
        for (int i = 1; i < points.Count; i++) segments[i - 1] = SvgSegment.Line(points[i]);
        return new SvgSubpath(points[0], segments, closed);
    }

    /// <summary>Parses a <c>points</c> list: pairs of numbers.</summary>
    public static bool TryParsePoints(string text, out List<SvgPoint> points)
    {
        points = [];
        var reader = new PathReader(text);
        reader.SkipWhitespace();
        while (!reader.AtEnd)
        {
            if (!reader.TryPoint(out SvgPoint p)) return false;
            points.Add(p);
            reader.SkipWhitespace();
        }
        return points.Count >= 2;
    }

    /// <summary>Parses a list of numbers separated by whitespace and/or commas.</summary>
    public static bool TryParseNumbers(string text, out List<double> numbers)
    {
        numbers = [];
        var reader = new PathReader(text);
        reader.SkipWhitespace();
        while (!reader.AtEnd)
        {
            if (!reader.TryNumber(out double n)) return false;
            numbers.Add(n);
            reader.SkipWhitespace();
        }
        return true;
    }

    internal static void AppendArc(
        List<SvgSegment> into, SvgPoint from, double rx, double ry, double rotationDegrees,
        bool large, bool sweep, SvgPoint to)
    {
        if (from == to) return;
        if (rx == 0 || ry == 0)
        {
            into.Add(SvgSegment.Line(to));
            return;
        }

        double phi = rotationDegrees * Math.PI / 180;
        double cos = Math.Cos(phi), sin = Math.Sin(phi);
        double dx = (from.X - to.X) / 2, dy = (from.Y - to.Y) / 2;
        double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;

        double lambda = x1 * x1 / (rx * rx) + y1 * y1 / (ry * ry);
        if (lambda > 1)
        {
            double s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
        double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
        if (!(den > 0) || !double.IsFinite(num) || !double.IsFinite(den))
        {
            into.Add(SvgSegment.Line(to));
            return;
        }

        double coef = Math.Sqrt(Math.Max(0, num / den)) * (large == sweep ? -1 : 1);
        double cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx;
        double cx = cos * cx1 - sin * cy1 + (from.X + to.X) / 2;
        double cy = sin * cx1 + cos * cy1 + (from.Y + to.Y) / 2;

        double theta1 = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
        double delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
        if (!sweep && delta > 0) delta -= 2 * Math.PI;
        else if (sweep && delta < 0) delta += 2 * Math.PI;

        int pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9));
        double step = delta / pieces;
        double t = 4.0 / 3.0 * Math.Tan(step / 4);
        for (int i = 0; i < pieces; i++)
        {
            double a1 = theta1 + i * step, a2 = a1 + step;
            double c1 = Math.Cos(a1), s1 = Math.Sin(a1), c2 = Math.Cos(a2), s2 = Math.Sin(a2);
            SvgPoint P(double ux, double uy) => new(
                cos * rx * ux - sin * ry * uy + cx,
                sin * rx * ux + cos * ry * uy + cy);
            SvgPoint end = i == pieces - 1 ? to : P(c2, s2);
            into.Add(SvgSegment.Cubic(P(c1 - t * s1, s1 + t * c1), P(c2 + t * s2, s2 - t * c2), end));
        }
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        return a;
    }

    /// <summary>A cursor over path data and number lists, with the SVG 1.1 number grammar.</summary>
    private sealed class PathReader(string text)
    {
        private int _i;

        public int Offset => _i;
        public bool AtEnd => _i >= text.Length;

        public bool AtNumberStart
        {
            get
            {
                SkipCommaWhitespace();
                if (AtEnd) return false;
                char c = text[_i];
                return char.IsAsciiDigit(c) || c is '.' or '-' or '+';
            }
        }

        public void SkipWhitespace()
        {
            while (!AtEnd && text[_i] is ' ' or '\t' or '\n' or '\r' or '\f') _i++;
        }

        private void SkipCommaWhitespace()
        {
            SkipWhitespace();
            if (!AtEnd && text[_i] == ',')
            {
                _i++;
                SkipWhitespace();
            }
        }

        public bool TryCommand(out char command)
        {
            SkipWhitespace();
            command = AtEnd ? '\0' : text[_i];
            if (!char.IsAsciiLetter(command) || command is 'e' or 'E') return false;
            _i++;
            return true;
        }

        public bool TryPoint(out SvgPoint p)
        {
            p = default;
            if (!TryNumber(out double x) || !TryNumber(out double y)) return false;
            p = new SvgPoint(x, y);
            return true;
        }

        public bool TryFlag(out bool flag)
        {
            SkipCommaWhitespace();
            flag = false;
            if (AtEnd || text[_i] is not ('0' or '1')) return false;
            flag = text[_i] == '1';
            _i++;
            return true;
        }

        public bool TryNumber(out double value)
        {
            SkipCommaWhitespace();
            value = 0;
            int begin = _i;
            if (!AtEnd && text[_i] is '+' or '-') _i++;
            int digits = 0;
            while (!AtEnd && char.IsAsciiDigit(text[_i])) { _i++; digits++; }
            if (!AtEnd && text[_i] == '.')
            {
                _i++;
                while (!AtEnd && char.IsAsciiDigit(text[_i])) { _i++; digits++; }
            }
            if (digits == 0)
            {
                _i = begin;
                return false;
            }
            if (!AtEnd && text[_i] is 'e' or 'E')
            {
                int mark = _i;
                _i++;
                if (!AtEnd && text[_i] is '+' or '-') _i++;
                int exponent = 0;
                while (!AtEnd && char.IsAsciiDigit(text[_i])) { _i++; exponent++; }
                if (exponent == 0) _i = mark;
            }
            return double.TryParse(text.AsSpan(begin, _i - begin), NumberStyles.Float,
                       CultureInfo.InvariantCulture, out value)
                   && double.IsFinite(value);
        }
    }
}
