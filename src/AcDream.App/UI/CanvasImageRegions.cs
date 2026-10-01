using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI;

/// <summary>
/// One upright textured quad: where it goes, in canvas pixels, and which
/// part of the texture it shows, in texture coordinates.
/// </summary>
internal readonly record struct CanvasImagePiece(
    float X, float Y, float Width, float Height,
    float U0, float V0, float U1, float V1);

/// <summary>
/// The arithmetic behind drawing part of an image: a source rectangle in
/// the image's pixels becomes texture coordinates, and a nine-slice frame
/// becomes up to nine such pieces. Pure, so it is tested on its own.
///
/// <para>On a linearly sampled texture a sample less than half a pixel
/// from a region's edge blends in the pixel just outside it, which on a
/// sheet is the neighbouring frame. An edge that lies inside the image is
/// therefore pulled in, just far enough that the sample taken at the
/// screen pixel nearest it lands half a pixel inside the region. A region
/// drawn at its own size on whole screen pixels already samples its pixel
/// centres, so it is not pulled at all; a magnified one is pulled by up to
/// half a pixel. When an edge is off a whole screen pixel, or the region is
/// turned or scaled, the nearest sample can sit right at the edge, so the
/// full half pixel is used. An edge on the image's border is left alone:
/// there it samples exactly as a whole-image draw does. Seams between the
/// pieces of one nine-slice are left alone too, since what lies across a
/// seam is the neighbouring piece of the same frame. Nearest sampling never
/// blends, so nothing is pulled in.</para>
/// </summary>
internal static class CanvasImageRegions
{
    /// <summary>The most pieces one nine-slice produces.</summary>
    internal const int MaximumNineSlicePieces = 9;

    /// <summary>How close to a whole screen pixel an edge must be to count as on it.</summary>
    private const double WholePixelTolerance = 1e-4;

    /// <summary>
    /// The piece that draws <paramref name="source"/> of an image over
    /// <paramref name="destination"/>. The source is cut to the image's
    /// bounds and the destination shrinks with it, in proportion, so what
    /// remains lands where it would have been. False when nothing is left
    /// to draw: an empty or non-finite rectangle, or a source wholly off
    /// the image.
    ///
    /// <para><paramref name="devicePixelsPerPixel"/> is how many screen
    /// pixels one canvas pixel covers, which decides how far a magnified
    /// region is pulled in. <paramref name="exactPull"/> asks for the full
    /// half pixel whatever the size, for a region that is then turned or
    /// scaled and so has no edge on a whole pixel.</para>
    /// </summary>
    internal static bool TryMapRegion(
        int imageWidth,
        int imageHeight,
        bool linearFiltered,
        PluginRect source,
        PluginRect destination,
        double devicePixelsPerPixel,
        bool exactPull,
        out CanvasImagePiece piece)
    {
        piece = default;
        if (imageWidth <= 0 || imageHeight <= 0
            || !IsFinite(source) || !IsFinite(destination)
            || !(source.Width > 0 && source.Height > 0)
            || !(destination.Width > 0 && destination.Height > 0))
        {
            return false;
        }

        if (!TryCut(source, imageWidth, imageHeight, out double x0, out double y0, out double x1, out double y1))
            return false;

        double scaleX = destination.Width / source.Width;
        double scaleY = destination.Height / source.Height;
        double left = destination.X + (x0 - source.X) * scaleX;
        double top = destination.Y + (y0 - source.Y) * scaleY;
        double width = (x1 - x0) * scaleX;
        double height = (y1 - y0) * scaleY;
        Coordinates(
            x0, x1, imageWidth, linearFiltered && x0 > 0, linearFiltered && x1 < imageWidth,
            left, width, devicePixelsPerPixel, exactPull,
            out float u0, out float u1);
        Coordinates(
            y0, y1, imageHeight, linearFiltered && y0 > 0, linearFiltered && y1 < imageHeight,
            top, height, devicePixelsPerPixel, exactPull,
            out float v0, out float v1);
        piece = new CanvasImagePiece((float)left, (float)top, (float)width, (float)height, u0, v0, u1, v1);
        return true;
    }

    /// <summary>
    /// The pieces of a nine-slice frame over <paramref name="destination"/>,
    /// written to <paramref name="pieces"/> row by row, top-left first.
    /// Returns how many were written: none for bad input, fewer than nine
    /// when a row or column is empty or the middle is left out.
    ///
    /// <para>The source (the whole image when null) is cut to the image's
    /// bounds; the destination is not moved. Insets that add up to more
    /// than the source on an axis are scaled down to fit it, and corners
    /// that add up to more than the destination on an axis shrink to fit
    /// it, both in proportion.</para>
    ///
    /// <para>Each piece's outer edges are pulled in as a region's are, by
    /// its own size on screen at <paramref name="devicePixelsPerPixel"/>:
    /// corners drawn at their own size on whole pixels are not pulled.</para>
    /// </summary>
    internal static int NineSlice(
        int imageWidth,
        int imageHeight,
        bool linearFiltered,
        PluginRect destination,
        PluginInsets insets,
        PluginRect? source,
        bool drawCenter,
        double devicePixelsPerPixel,
        Span<CanvasImagePiece> pieces)
    {
        if (pieces.Length < MaximumNineSlicePieces)
            throw new ArgumentException("A nine-slice needs room for nine pieces.", nameof(pieces));
        PluginRect frame = source ?? new PluginRect(0, 0, imageWidth, imageHeight);
        if (imageWidth <= 0 || imageHeight <= 0
            || !IsFinite(frame) || !IsFinite(destination)
            || !(frame.Width > 0 && frame.Height > 0)
            || !(destination.Width > 0 && destination.Height > 0)
            || !IsValid(insets.Left) || !IsValid(insets.Top)
            || !IsValid(insets.Right) || !IsValid(insets.Bottom))
        {
            return 0;
        }

        if (!TryCut(frame, imageWidth, imageHeight, out double x0, out double y0, out double x1, out double y1))
            return 0;

        (double left, double right, bool fittedAcross) = Fit(insets.Left, insets.Right, x1 - x0);
        (double top, double bottom, bool fittedDown) = Fit(insets.Top, insets.Bottom, y1 - y0);
        (double leftOut, double rightOut, _) = Fit(left, right, destination.Width);
        (double topOut, double bottomOut, _) = Fit(top, bottom, destination.Height);

        // Insets scaled down to fit meet with no middle between them; the
        // scaled pair can fall a rounding error short of the source, and
        // that sliver would otherwise be stretched across the middle.
        Span<double> sourceXs = [x0, x0 + left, x1 - right, x1];
        Span<double> sourceYs = [y0, y0 + top, y1 - bottom, y1];
        if (fittedAcross) sourceXs[2] = sourceXs[1];
        if (fittedDown) sourceYs[2] = sourceYs[1];
        double destinationRight = destination.X + destination.Width;
        double destinationBottom = destination.Y + destination.Height;
        ReadOnlySpan<double> destinationXs =
            [destination.X, destination.X + leftOut, destinationRight - rightOut, destinationRight];
        ReadOnlySpan<double> destinationYs =
            [destination.Y, destination.Y + topOut, destinationBottom - bottomOut, destinationBottom];

        int count = 0;
        for (int row = 0; row < 3; row++)
        {
            if (!(sourceYs[row + 1] > sourceYs[row]) || !(destinationYs[row + 1] > destinationYs[row]))
                continue;
            // Only the frame's own outer edges are pulled in: a seam is
            // between two pieces of the same frame.
            Coordinates(
                sourceYs[row], sourceYs[row + 1], imageHeight,
                linearFiltered && sourceYs[row] == y0 && y0 > 0,
                linearFiltered && sourceYs[row + 1] == y1 && y1 < imageHeight,
                destinationYs[row], destinationYs[row + 1] - destinationYs[row], devicePixelsPerPixel, false,
                out float v0, out float v1);
            for (int column = 0; column < 3; column++)
            {
                if (row == 1 && column == 1 && !drawCenter)
                    continue;
                if (!(sourceXs[column + 1] > sourceXs[column])
                    || !(destinationXs[column + 1] > destinationXs[column]))
                {
                    continue;
                }
                Coordinates(
                    sourceXs[column], sourceXs[column + 1], imageWidth,
                    linearFiltered && sourceXs[column] == x0 && x0 > 0,
                    linearFiltered && sourceXs[column + 1] == x1 && x1 < imageWidth,
                    destinationXs[column], destinationXs[column + 1] - destinationXs[column],
                    devicePixelsPerPixel, false,
                    out float u0, out float u1);
                pieces[count++] = new CanvasImagePiece(
                    (float)destinationXs[column],
                    (float)destinationYs[row],
                    (float)(destinationXs[column + 1] - destinationXs[column]),
                    (float)(destinationYs[row + 1] - destinationYs[row]),
                    u0, v0, u1, v1);
            }
        }
        return count;
    }

    /// <summary>The rectangle cut to the image, as edges; false when nothing of it is on the image.</summary>
    private static bool TryCut(
        PluginRect rect, int imageWidth, int imageHeight,
        out double x0, out double y0, out double x1, out double y1)
    {
        x0 = Math.Max(rect.X, 0);
        y0 = Math.Max(rect.Y, 0);
        x1 = Math.Min(rect.X + rect.Width, imageWidth);
        y1 = Math.Min(rect.Y + rect.Height, imageHeight);
        return x1 > x0 && y1 > y0;
    }

    /// <summary>
    /// Texture coordinates for the pixels from <paramref name="low"/> to
    /// <paramref name="high"/> on an axis of <paramref name="size"/> pixels,
    /// drawn from <paramref name="start"/> over <paramref name="length"/>
    /// canvas pixels, each end pulled in when asked by <see cref="Pull"/>.
    /// A span too narrow for both pulls shares what it has between them, so
    /// both ends meet in its middle rather than cross.
    /// </summary>
    private static void Coordinates(
        double low, double high, int size, bool pullLow, bool pullHigh,
        double start, double length, double devicePixelsPerPixel, bool exactPull,
        out float t0, out float t1)
    {
        double pull = pullLow || pullHigh
            ? Pull(high - low, start, length, devicePixelsPerPixel, exactPull, pullLow && pullHigh)
            : 0.0;
        double pullIn = pullLow ? pull : 0.0;
        double pullOut = pullHigh ? pull : 0.0;
        double room = high - low;
        if (pullIn + pullOut > room)
        {
            double share = room / (pullIn + pullOut);
            pullIn *= share;
            pullOut *= share;
        }
        t0 = (float)((low + pullIn) / size);
        t1 = (float)((high - pullOut) / size);
    }

    /// <summary>
    /// How far a pulled end of a span is pulled in, in image pixels: the
    /// least that puts the sample at the screen pixel centre nearest that
    /// end half a pixel inside the span, so filtering never reaches past
    /// it. With both ends of the span on whole screen pixels that centre is
    /// half a screen pixel in, which over <c>m</c> screen pixels and
    /// <paramref name="texels"/> <c>n</c> asks for
    /// <c>0.5 (m - n) / (m - 1)</c> when both ends are pulled and
    /// <c>0.5 (m - n) / (m - 0.5)</c> when one is: nothing at its own size
    /// or smaller, approaching half a pixel as it grows. Off a whole pixel
    /// the nearest centre can sit right at the end, so it is half a pixel,
    /// as it is when asked for exactly that or the span is a pixel or less.
    /// </summary>
    private static double Pull(
        double texels, double start, double length, double devicePixelsPerPixel, bool exactPull, bool bothEnds)
    {
        double pixels = length * devicePixelsPerPixel;
        if (exactPull || !(pixels > 1)
            || !OnWholePixel(start * devicePixelsPerPixel)
            || !OnWholePixel((start + length) * devicePixelsPerPixel))
        {
            return 0.5;
        }
        double pull = bothEnds
            ? 0.5 * (pixels - texels) / (pixels - 1)
            : 0.5 * (pixels - texels) / (pixels - 0.5);
        return Math.Clamp(pull, 0.0, 0.5);
    }

    private static bool OnWholePixel(double position) =>
        Math.Abs(position - Math.Round(position)) <= WholePixelTolerance;

    /// <summary>
    /// Two lengths along one axis, scaled down together when they add up to
    /// more than it; <c>Fitted</c> says whether they were.
    /// </summary>
    private static (double First, double Second, bool Fitted) Fit(double first, double second, double length)
    {
        double sum = first + second;
        if (sum <= length) return (first, second, false);
        double share = length / sum;
        return (first * share, second * share, true);
    }

    private static bool IsValid(double inset) => double.IsFinite(inset) && inset >= 0;

    private static bool IsFinite(PluginRect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width) && double.IsFinite(rect.Height);
}
