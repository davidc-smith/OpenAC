using System.Numerics;

namespace AcDream.App.UI.Layout;

/// <summary>
/// How many device pixels a plugin canvas is painted at per canvas pixel.
/// The interface is laid out in window points and stretched over the
/// framebuffer, and a pre-game fixed canvas stretches it again; a canvas
/// painted at its own size would be magnified by both and come out blurred.
/// Painting it at the product instead puts one texel on each device pixel.
///
/// <para>The scale goes up in quarter steps, rounded up so a texel is never
/// stretched, from 1 to 4. A canvas too large for the device at that scale
/// is painted at the largest step that fits, and never below 1.</para>
/// </summary>
internal static class CanvasPixelScale
{
    internal const float Step = 0.25f;

    internal const float Minimum = 1f;

    internal const float Maximum = 4f;

    /// <summary>
    /// The scale every canvas of the interface is painted at, before any
    /// canvas's own size is considered: framebuffer pixels per window point
    /// times the interface's fixed-canvas stretch, the larger of the two
    /// axes, rounded up to a step.
    /// </summary>
    /// <param name="framebufferPerPoint">Framebuffer pixels per window point on each axis.</param>
    /// <param name="interfaceStretch">The root's <see cref="UiRoot.CanvasScale"/>.</param>
    internal static float ForInterface(Vector2 framebufferPerPoint, Vector2 interfaceStretch)
    {
        float raw = MathF.Max(framebufferPerPoint.X * interfaceStretch.X, framebufferPerPoint.Y * interfaceStretch.Y);
        if (!float.IsFinite(raw) || raw <= Minimum)
            return Minimum;
        // A hair of tolerance, so 2.0000002 from a float division is 2, not 2.25.
        float stepped = MathF.Ceiling(raw / Step - 1e-3f) * Step;
        return Math.Clamp(stepped, Minimum, Maximum);
    }

    /// <summary>
    /// The interface scale lowered step by step until a canvas of this size
    /// fits the device's largest texture, but never below 1: a canvas too
    /// large even at 1 fails to get a target, as it always has.
    /// </summary>
    internal static float ForCanvas(float interfaceScale, int width, int height, uint maximumDimension)
    {
        float scale = interfaceScale;
        while (scale > Minimum && (DeviceSize(width, scale) > maximumDimension || DeviceSize(height, scale) > maximumDimension))
            scale -= Step;
        return MathF.Max(scale, Minimum);
    }

    /// <summary>A canvas length in device pixels: rounded up, so the last canvas pixel is whole.</summary>
    internal static int DeviceSize(int canvasPixels, float scale) =>
        (int)Math.Ceiling(canvasPixels * (double)scale);
}
