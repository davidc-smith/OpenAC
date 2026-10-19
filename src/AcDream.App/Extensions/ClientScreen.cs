using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.App.World;
using AcDream.ClientExtensions;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Extensions;

/// <summary>
/// <see cref="IClientScreen"/> over the world camera, the live world origin and the
/// retained radar window. Its sources arrive later than the extensions attach, so
/// each answers false until bound.
/// </summary>
internal sealed class ClientScreen(LiveWorldOriginState origin) : IClientScreen
{
    private Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>? _camera;
    private RetailUiRuntime? _ui;

    public void BindCamera(Func<(Matrix4x4 View, Matrix4x4 Projection, Vector2 Viewport)>? camera) => _camera = camera;

    public void BindUi(RetailUiRuntime? ui) => _ui = ui;

    public bool TryGetViewportSize(out Vector2 size)
    {
        size = default;
        if (_camera is null)
            return false;
        size = _camera().Viewport;
        return size.X > 0 && size.Y > 0;
    }

    public bool TryProject(PluginNavigationPosition position, out Vector2 screen)
    {
        screen = default;
        if (_camera is null || !origin.IsKnown)
            return false;
        (Matrix4x4 view, Matrix4x4 projection, Vector2 viewport) = _camera();
        if (viewport.X <= 0 || viewport.Y <= 0)
            return false;
        Vector3 world = PluginNavigationProjection.ToWorld(position, origin.CenterX, origin.CenterY);
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), view * projection);
        if (clip.W <= 0.001f)
            return false;
        float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        screen = new Vector2((ndcX * 0.5f + 0.5f) * viewport.X, (1f - (ndcY * 0.5f + 0.5f)) * viewport.Y);
        return true;
    }

    public bool TryGetRadarDisc(out Vector2 centre, out float radius)
    {
        centre = default;
        radius = 0f;
        if (_ui?.Host.WindowManager is not { } windows
            || !windows.IsVisible(WindowNames.Radar)
            || !windows.TryGet(WindowNames.Radar, out RetailWindowHandle handle)
            || handle.ContentRoot is not UiRadar radar)
            return false;
        centre = radar.ScreenPosition + radar.Center;
        radius = RadarController.RadarPixelRadius;
        return true;
    }
}
