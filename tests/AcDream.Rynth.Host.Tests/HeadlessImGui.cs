using System.Numerics;
using ImGuiNET;

namespace AcDream.Rynth.Host.Tests;

/// <summary>A headless ImGui context (fonts built, no renderer) for one test.</summary>
internal sealed class HeadlessImGui : IDisposable
{
    private readonly IntPtr _context;

    public HeadlessImGui()
    {
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280, 800);
        io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
        io.Fonts.SetTexID(1);
    }

    /// <summary>Runs one frame and returns its draw data's vertex count.</summary>
    public int Frame(Action draw)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.DeltaTime = 1f / 60f;
        ImGui.NewFrame();
        draw();
        ImGui.Render();
        return ImGui.GetDrawData().TotalVtxCount;
    }

    public void Dispose() => ImGui.DestroyContext(_context);
}
