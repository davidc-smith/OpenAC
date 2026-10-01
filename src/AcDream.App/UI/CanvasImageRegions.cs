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
/// <para>On a linearly sampled texture a sample at a region's edge blends
/// in the pixel just outside it, which on a sheet is the neighbouring
/// frame. An edge that lies inside the image is therefore pulled in by half
/// a pixel, so the outermost sample is the centre of the region's own edge
/// pixel. An edge on the image's border is left alone: there it samples
/// exactly as a whole-image draw does. Seams between the pieces of one
/// nine-slice are left alone too, since what lies across a seam is the
/// neighbouring piece of the same frame. Nearest sampling never blends,
/// so nothing is pulled in.</para>
/// </summary>
internal static class CanvasImageRegions
{
    /// <summary>The most pieces one nine-slice produces.</summary>
    internal const int MaximumNineSlicePieces = 9;

    /// <summary>
    /// The piece that draws <paramref name="source"/> of an image over
    /// <paramref name="destination"/>. The source is cut to the image's
    /// bounds and the destination shrinks with it, in proportion, so what
    /// remains lands where it would have been. False when nothing is left
    /// to draw: an empty or non-finite rectangle, or a source wholly off
    /// the image.
    /// </summary>
    internal static bool TryMapRegion(
        int imageWidth,
        int imageHeight,
        bool linearFiltered,
        PluginRect source,
        PluginRect destination,
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
        Coordinates(x0, x1, imageWidth, linearFiltered && x0 > 0, linearFiltered && x1 < imageWidth,
            out float u0, out float u1);
        Coordinates(y0, y1, imageHeight, linearFiltered && y0 > 0, linearFiltered && y1 < imageHeight,
            out float v0, out float v1);
        piece = new CanvasImagePiece(
            (float)(destination.X + (x0 - source.X) * scaleX),
            (float)(destination.Y + (y0 - source.Y) * scaleY),
            (float)((x1 - x0) * scaleX),
            (float)((y1 - y0) * scaleY),
            u0, v0, u1, v1);
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
    /// </summary>
    internal static int NineSlice(
        int imageWidth,
        int imageHeight,
        bool linearFiltered,
        PluginRect destination,
        PluginInsets insets,
        PluginRect? source,
        bool drawCenter,
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

        (double left, double right) = Fit(insets.Left, insets.Right, x1 - x0);
        (double top, double bottom) = Fit(insets.Top, insets.Bottom, y1 - y0);
        (double leftOut, double rightOut) = Fit(left, right, destination.Width);
        (double topOut, double bottomOut) = Fit(top, bottom, destination.Height);

        ReadOnlySpan<double> sourceXs = [x0, x0 + left, x1 - right, x1];
        ReadOnlySpan<double> sourceYs = [y0, y0 + top, y1 - bottom, y1];
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
    /// each end pulled in by half a pixel when asked. A span too narrow for
    /// both pulls shares what it has between them, so both ends meet in its
    /// middle rather than cross.
    /// </summary>
    private static void Coordinates(
        double low, double high, int size, bool pullLow, bool pullHigh, out float t0, out float t1)
    {
        double pullIn = pullLow ? 0.5 : 0.0;
        double pullOut = pullHigh ? 0.5 : 0.0;
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

    /// <summary>Two lengths along one axis, scaled down together when they add up to more than it.</summary>
    private static (double First, double Second) Fit(double first, double second, double length)
    {
        double sum = first + second;
        if (sum <= length) return (first, second);
        double share = length / sum;
        return (first * share, second * share);
    }

    private static bool IsValid(double inset) => double.IsFinite(inset) && inset >= 0;

    private static bool IsFinite(PluginRect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y)
        && double.IsFinite(rect.Width) && double.IsFinite(rect.Height);
}
