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

    /// <summary>
    /// The shape as the renderer will draw it, or false when it is dropped.
    /// A ring or disc becomes a full turn from north (its start, sweep and
    /// facing are ignored); a disc or filled arc has no width.
    /// </summary>
    internal static bool TryNormalizeShape(PluginGroundShape shape, out PluginGroundShape normalized)
    {
        normalized = default;
        if (!IsValidAnchor(shape.Anchor)
            || !float.IsFinite(shape.Radius)
            || shape.Radius < PluginGroundShape.MinimumRadius
            || shape.Radius > PluginGroundShape.MaximumRadius)
        {
            return false;
        }

        switch (shape.Kind)
        {
            case PluginGroundShapeKind.Ring:
                if (!IsValidBand(shape.Width, shape.Radius))
                    return false;
                normalized = shape with { StartDegrees = 0f, SweepDegrees = 360f, Filled = false, FacesObject = false };
                return true;
            case PluginGroundShapeKind.Disc:
                normalized = shape with { Width = 0f, StartDegrees = 0f, SweepDegrees = 360f, Filled = true, FacesObject = false };
                return true;
            case PluginGroundShapeKind.Arc:
                if (!float.IsFinite(shape.StartDegrees)
                    || !float.IsFinite(shape.SweepDegrees)
                    || shape.SweepDegrees <= 0f
                    || shape.SweepDegrees > 360f)
                {
                    return false;
                }
                if (shape.Filled)
                {
                    normalized = shape with { Width = 0f };
                    return true;
                }
                if (!IsValidBand(shape.Width, shape.Radius))
                    return false;
                normalized = shape;
                return true;
            default:
                return false;
        }
    }

    // A band runs inward from the radius: it must have some width and cannot
    // run past the centre.
    private static bool IsValidBand(float width, float radius) =>
        float.IsFinite(width) && width > 0f && width <= radius;
}
