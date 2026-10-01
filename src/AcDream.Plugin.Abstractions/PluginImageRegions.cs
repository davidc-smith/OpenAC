namespace AcDream.Plugin.Abstractions;

/// <summary>
/// How far in from each edge of an image the borders of a nine-slice
/// frame run, in the image's own pixels. The four corners are drawn at
/// these sizes; the edges and the middle between them stretch.
/// </summary>
/// <param name="Left">The width of the left column.</param>
/// <param name="Top">The height of the top row.</param>
/// <param name="Right">The width of the right column.</param>
/// <param name="Bottom">The height of the bottom row.</param>
public readonly record struct PluginInsets(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The same inset on all four sides.</summary>
    /// <param name="all">The inset, in the image's pixels.</param>
    /// <returns>Insets of <paramref name="all"/> on every side.</returns>
    public static PluginInsets Uniform(double all) => new(all, all, all, all);
}
