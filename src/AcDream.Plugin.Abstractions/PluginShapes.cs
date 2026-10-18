namespace AcDream.Plugin.Abstractions;

/// <summary>
/// The radius of each corner of a rounded rectangle, in pixels. Zero gives
/// a square corner. Radii too large for the rectangle are scaled down
/// together, keeping their proportions, until neighbouring corners fit
/// along the side they share.
/// </summary>
/// <param name="TopLeft">The top-left corner's radius.</param>
/// <param name="TopRight">The top-right corner's radius.</param>
/// <param name="BottomRight">The bottom-right corner's radius.</param>
/// <param name="BottomLeft">The bottom-left corner's radius.</param>
public readonly record struct PluginCornerRadii(double TopLeft, double TopRight, double BottomRight, double BottomLeft)
{
    /// <summary>The same radius at all four corners.</summary>
    /// <param name="radius">The radius, in pixels.</param>
    /// <returns>Radii with <paramref name="radius"/> at every corner.</returns>
    public static PluginCornerRadii Uniform(double radius) => new(radius, radius, radius, radius);
}

/// <summary>Which way a gradient runs across a rectangle.</summary>
public enum PluginGradientDirection
{
    /// <summary>From the left edge, in the first colour, to the right edge, in the second.</summary>
    Horizontal,

    /// <summary>From the top edge, in the first colour, to the bottom edge, in the second.</summary>
    Vertical,
}
