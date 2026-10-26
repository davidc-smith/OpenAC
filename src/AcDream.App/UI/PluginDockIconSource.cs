using AcDream.Core.Plugins;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI;

/// <summary>
/// The icons one dock button may draw, best first: the window's own PNG, an SVG (the window's
/// own or the plugin's <c>icon.svg</c>), then the plugin's <c>icon.png</c>. The button falls
/// back to <c>IconSurfaceId</c> and the monogram itself. The window's PNG may come with a
/// light and a dark version (<see cref="PluginImageVariants"/>), drawn under a light or a dark
/// theme in its place.
/// </summary>
internal readonly record struct PluginDockIcons(
    PluginFileIconCache.Entry? WindowImage,
    PluginSvgIconCache.PluginSvgIconEntry? Svg,
    (uint Texture, int Width, int Height)? PluginImage,
    PluginFileIconCache.Entry? WindowImageLight = null,
    PluginFileIconCache.Entry? WindowImageDark = null);

/// <summary>
/// Picks a dock button's icons from a window's descriptor and its plugin's folder, in order:
/// the descriptor's <c>IconFile</c> (<c>.svg</c> drawn in the theme colour, <c>.png</c> in its
/// own colours), the plugin's <c>icon.svg</c>, the plugin's <c>icon.png</c>. A file that cannot
/// be used is logged once by the cache that refused it and the next icon is tried.
/// </summary>
internal sealed class PluginDockIconSource(
    PluginSvgIconCache svgIcons,
    PluginFileIconCache windowImages,
    Func<string, string?, (uint Texture, int Width, int Height)?> pluginImage)
{
    /// <summary>
    /// The icons for one window. A returned SVG carries one hold, which the caller passes on to
    /// the dock. An SVG is only taken when the window has no usable PNG of its own, so a window
    /// PNG never leaves an unused SVG held.
    /// </summary>
    internal PluginDockIcons Resolve(string pluginId, PluginPanelDescriptor descriptor, string? pluginDirectory)
    {
        (uint, int, int)? plugin = pluginImage(pluginId, pluginDirectory);
        if (pluginDirectory is null)
            return new PluginDockIcons(null, null, plugin);

        if (descriptor.IconFile is { } iconFile)
        {
            if (iconFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                if (windowImages.Acquire(pluginId, pluginDirectory, iconFile) is { } image)
                    return new PluginDockIcons(image, null, plugin,
                        Variant(pluginId, pluginDirectory, iconFile, PluginImageVariants.Light),
                        Variant(pluginId, pluginDirectory, iconFile, PluginImageVariants.Dark));
            }
            else if (iconFile.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                if (AcquireSvg(pluginId, pluginDirectory, iconFile) is { } own)
                    return new PluginDockIcons(null, own, plugin);
            }
            else
            {
                svgIcons.Reject(Label(pluginId, iconFile), Path.Combine(pluginDirectory, iconFile),
                    "is not an .svg or .png file");
            }
        }

        PluginSvgIconCache.PluginSvgIconEntry? shared =
            File.Exists(Path.Combine(pluginDirectory, PluginSvgIcon.FileName))
                ? AcquireSvg(pluginId, pluginDirectory, PluginSvgIcon.FileName)
                : null;
        return new PluginDockIcons(null, shared, plugin);
    }

    private PluginFileIconCache.Entry? Variant(string pluginId, string pluginDirectory, string iconFile, string variant) =>
        PluginImageVariants.PathFor(iconFile, variant) is { } path && PluginImageVariants.Exists(pluginDirectory, path)
            ? windowImages.Acquire(pluginId, pluginDirectory, path)
            : null;

    private PluginSvgIconCache.PluginSvgIconEntry? AcquireSvg(string pluginId, string pluginDirectory, string relative)
    {
        string label = Label(pluginId, relative);
        if (!PluginSvgIcon.TryResolvePath(pluginDirectory, relative, out string? fullPath, out string? reason))
        {
            svgIcons.Reject(label, Path.Combine(pluginDirectory, relative), reason);
            return null;
        }
        return svgIcons.Acquire(label, fullPath);
    }

    private static string Label(string pluginId, string relative) => $"{pluginId}/{relative.Replace('\\', '/')}";
}
