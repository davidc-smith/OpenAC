namespace AcDream.App.UI;

/// <summary>
/// A plugin image may come in a light-theme and a dark-theme version beside the file
/// markup names: <c>icons/sword.light.png</c> and <c>icons/sword.dark.png</c> next to
/// <c>icons/sword.png</c>. A light theme draws the <c>.light</c> file and every other
/// theme, Classic included, the <c>.dark</c> one, each only when it is there; otherwise
/// the named file is drawn.
/// </summary>
internal static class PluginImageVariants
{
    internal const string Light = "light";
    internal const string Dark = "dark";

    /// <summary>The variant a theme draws: <see cref="Light"/> under a light theme, else <see cref="Dark"/>.</summary>
    internal static string For(bool light) => light ? Light : Dark;

    /// <summary>
    /// <paramref name="path"/> with <c>.variant</c> put before the file's extension, or
    /// after the name when it has none; null when the file is already a variant.
    /// </summary>
    internal static string? PathFor(string path, string variant)
    {
        int nameStart = path.LastIndexOfAny(['/', '\\']) + 1;
        int dot = path.LastIndexOf('.');
        if (dot < nameStart) dot = path.Length;
        string stem = path[nameStart..dot];
        if (stem.EndsWith("." + Light, StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("." + Dark, StringComparison.OrdinalIgnoreCase))
            return null;
        return $"{path[..dot]}.{variant}{path[dot..]}";
    }

    /// <summary>Whether the variant file is there, so a missing one is skipped without a report.</summary>
    internal static bool Exists(string pluginDirectory, string variantPath)
    {
        try { return File.Exists(Path.Combine(pluginDirectory, variantPath)); }
        catch (ArgumentException) { return false; }
    }
}
