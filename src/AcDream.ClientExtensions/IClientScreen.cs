using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.ClientExtensions;

/// <summary>
/// The client's 3D view and radar as an extension sees them, in logical points (the
/// space the overlay and the retained UI draw in; not framebuffer pixels).
/// </summary>
public interface IClientScreen
{
    /// <summary>The window size; false before the world view exists.</summary>
    bool TryGetViewportSize(out Vector2 size);

    /// <summary>
    /// Where a world position falls on screen, Y down. False when it is behind the
    /// camera or the view is not ready; a point beside the screen still answers true.
    /// </summary>
    bool TryProject(PluginNavigationPosition position, out Vector2 screen);

    /// <summary>The radar disc's centre and radius on screen; false while the radar is hidden.</summary>
    bool TryGetRadarDisc(out Vector2 centre, out float radius);
}
