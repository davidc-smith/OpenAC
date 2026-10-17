using System.Numerics;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Turns an ImGui clip rectangle (display points, origin top-left) into an
/// OpenAC scissor: framebuffer pixels with Y counted from the bottom, which
/// is how IGpuPassEncoder.SetScissor takes it.
/// </summary>
internal static class ImGuiScissor
{
    public static bool TryMap(
        Vector4 clipRect,
        Vector2 displayPos,
        Vector2 framebufferScale,
        int framebufferWidth,
        int framebufferHeight,
        out (int X, int Y, int Width, int Height) scissor)
    {
        float x0 = MathF.Max(0f, (clipRect.X - displayPos.X) * framebufferScale.X);
        float y0 = MathF.Max(0f, (clipRect.Y - displayPos.Y) * framebufferScale.Y);
        float x1 = MathF.Min(framebufferWidth, (clipRect.Z - displayPos.X) * framebufferScale.X);
        float y1 = MathF.Min(framebufferHeight, (clipRect.W - displayPos.Y) * framebufferScale.Y);
        if (x1 <= x0 || y1 <= y0)
        {
            scissor = default;
            return false;
        }

        int left = (int)x0;
        int top = (int)y0;
        int width = (int)MathF.Ceiling(x1) - left;
        int height = (int)MathF.Ceiling(y1) - top;
        scissor = (left, framebufferHeight - (top + height), width, height);
        return true;
    }
}
