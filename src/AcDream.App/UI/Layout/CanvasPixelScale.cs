using System.Numerics;

namespace AcDream.App.UI.Layout;

/// <summary>
/// How many device pixels a plugin canvas is painted at per canvas pixel.
/// The interface is laid out in window points and stretched over the
/// framebuffer; a canvas painted at its own size would be magnified and come
/// out blurred. Painting it at framebuffer pixels per point instead puts one
/// texel on each device pixel.
///
/// <para>A pre-game fixed canvas stretches the interface again, but that
/// stretch is left out. The screens that declare one are opaque windows over
/// the whole interface, and plugin canvases sit behind every window, so a
/// canvas painted larger for it would cost memory and font bakes that nobody
/// can see.</para>
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
    /// canvas's own size is considered: framebuffer pixels per window point,
    /// the larger of the two axes, rounded up to a step.
    /// </summary>
    /// <param name="framebufferPerPoint">Framebuffer pixels per window point on each axis.</param>
    internal static float ForInterface(Vector2 framebufferPerPoint)
    {
        float raw = MathF.Max(framebufferPerPoint.X, framebufferPerPoint.Y);
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
