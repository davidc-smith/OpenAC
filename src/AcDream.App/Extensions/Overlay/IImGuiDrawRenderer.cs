using System.Numerics;
using ImGuiNET;

namespace AcDream.App.Extensions.Overlay;

/// <summary>Draws ImGui's output. The client's is <c>ImGuiVulkanRenderer</c>; tests use a fake.</summary>
internal interface IImGuiDrawRenderer : IDisposable
{
    /// <summary>
    /// Called before ImGui.NewFrame each frame, with no pass open: build or
    /// rebuild the font atlas for this framebuffer scale and upload it.
    /// </summary>
    void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale);

    /// <summary>Draws one frame's output; called after ImGui.Render.</summary>
    void Render(ImDrawDataPtr drawData, Vector2 framebufferSize);
}
