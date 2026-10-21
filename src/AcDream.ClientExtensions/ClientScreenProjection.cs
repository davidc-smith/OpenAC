using System.Numerics;
using AcDream.Plugin.Abstractions;

namespace AcDream.ClientExtensions;

/// <summary>
/// One frame's camera, captured: answers <see cref="IClientScreen.TryProject"/> the way the
/// screen did for that frame, from any thread.
/// </summary>
public sealed record ClientScreenProjection(Matrix4x4 ViewProjection, Vector2 Viewport, int CenterX, int CenterY)
{
    public bool TryProject(PluginNavigationPosition position, out Vector2 screen)
    {
        screen = default;
        if (Viewport.X <= 0 || Viewport.Y <= 0)
            return false;
        // Same as AcDream.App.World.PluginNavigationProjection.ToWorld (internal to AcDream.App).
        Vector3 world = new(
            (float)(position.EastWest * 240d + (127 - CenterX) * 192d + 84d),
            (float)(position.NorthSouth * 240d + (127 - CenterY) * 192d + 84d),
            (float)(position.Elevation * 240d));
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), ViewProjection);
        if (clip.W <= 0.001f)
            return false;
        float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        screen = new Vector2((ndcX * 0.5f + 0.5f) * Viewport.X, (1f - (ndcY * 0.5f + 0.5f)) * Viewport.Y);
        return true;
    }
}
