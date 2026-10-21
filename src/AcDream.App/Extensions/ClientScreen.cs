using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.App.World;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Extensions;

/// <summary>
/// <see cref="IClientScreen"/> over the world camera, the live world origin and the
/// retained radar window (found through the retained UI's window manager). Its sources arrive later than the extensions attach, so
/// each answers false until bound.
/// </summary>
internal sealed class ClientScreen(LiveWorldOriginState origin) : IClientScreen
{
    private Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>? _camera;
    private RetailWindowManager? _windows;

    public void BindCamera(Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>? camera) => _camera = camera;

    /// <summary>The retained UI's window manager, where the radar window is found.</summary>
    public void BindWindows(RetailWindowManager? windows) => _windows = windows;

    public bool TryGetViewportSize(out Vector2 size)
    {
        size = default;
        if (_camera is null)
            return false;
        Vector2 viewport = _camera().Viewport;
        if (!(viewport.X > 0 && viewport.Y > 0))
            return false;
        size = viewport;
        return true;
    }

    public bool TryCaptureProjection(out ClientScreenProjection projection)
    {
        projection = null!;
        if (_camera is null || !origin.IsKnown)
            return false;
        (Matrix4x4 view, Matrix4x4 proj, Vector2 viewport) = _camera();
        if (viewport.X <= 0 || viewport.Y <= 0)
            return false;
        projection = new ClientScreenProjection(view * proj, viewport, origin.CenterX, origin.CenterY);
        return true;
    }

    public bool TryProject(PluginNavigationPosition position, out Vector2 screen)
    {
        screen = default;
        return TryCaptureProjection(out ClientScreenProjection projection)
            && projection.TryProject(position, out screen);
    }

    public bool TryGetRadarDisc(out Vector2 centre, out float radius)
    {
        centre = default;
        radius = 0f;
        if (_windows is not { } windows
            || !windows.IsVisible(WindowNames.Radar)
            || !windows.TryGet(WindowNames.Radar, out RetailWindowHandle handle)
            || handle.ContentRoot is not UiRadar radar)
            return false;
        centre = radar.ScreenPosition + radar.Center;
        radius = RadarController.RadarPixelRadius;
        return true;
    }
}
