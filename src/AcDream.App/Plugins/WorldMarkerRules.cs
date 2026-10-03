using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// Which world markers a plugin's set keeps, and in what form. Pure: the
/// store applies it to every entry it is given.
/// </summary>
internal static class WorldMarkerRules
{
    /// <summary>
    /// True when the anchor pins something: an object with a non-zero id, or
    /// a position whose coordinates are all finite numbers.
    /// </summary>
    internal static bool IsValidAnchor(PluginMarkerAnchor anchor) => anchor.Kind switch
    {
        PluginMarkerAnchorKind.Object => anchor.ObjectId != 0u,
        PluginMarkerAnchorKind.Position =>
            double.IsFinite(anchor.Position.EastWest)
            && double.IsFinite(anchor.Position.NorthSouth)
            && double.IsFinite(anchor.Position.Elevation),
        _ => false,
    };

    /// <summary>
    /// The icon as the overlay will draw it, with its size clamped into
    /// range, or false when it is dropped.
    /// </summary>
    internal static bool TryNormalizeIcon(PluginWorldIcon icon, out PluginWorldIcon normalized)
    {
        normalized = default;
        if (!IsValidAnchor(icon.Anchor)
            || !icon.Image.IsValid
            || !float.IsFinite(icon.SizePixels)
            || !float.IsFinite(icon.MaxRange)
            || icon.MaxRange <= 0f)
        {
            return false;
        }

        normalized = icon with
        {
            SizePixels = Math.Clamp(icon.SizePixels, PluginWorldIcon.MinimumSize, PluginWorldIcon.MaximumSize),
        };
        return true;
    }
}
