using System.Numerics;
using AcDream.App.Extensions.Overlay;
using AcDream.App.Input;
using ImGuiNET;

namespace AcDream.App.Tests.Extensions;

[Collection("Rynth")]
public sealed class OverlayWiringTests
{
    [Fact]
    public void DevToolsCaptureReadsTheProviders()
    {
        bool mouse = false, keyboard = false;
        var source = new DevToolsInputCaptureSource(false, () => mouse, () => keyboard);
        Assert.False(source.WantCaptureMouse);
        mouse = true;
        keyboard = true;
        Assert.True(source.WantCaptureMouse);
        Assert.True(source.WantCaptureKeyboard);
        Assert.False(new DevToolsInputCaptureSource(true).WantCaptureMouse);
    }

    [Fact]
    public void TheGateFollowsTheBoundOverlay()
    {
        var overlay = new ImGuiOverlay(_ => { });
        OverlayInputGate.Bind(overlay);
        Assert.False(OverlayInputGate.SuppressMouse);
        OverlayInputGate.Bind(null);
        Assert.False(OverlayInputGate.SuppressKeyboard);
        overlay.Dispose();
    }

    [Fact]
    public void TheGateSuppressesWhileImGuiHasTheMouse()
    {
        var overlay = new ImGuiOverlay(_ => { });
        try
        {
            overlay.Initialize(new AtlasOnlyRenderer(), () => (new Vector2(800, 600), new Vector2(800, 600)));
            overlay.AddFrameCallback(() =>
            {
                ImGui.SetNextWindowPos(new Vector2(100, 100));
                ImGui.SetNextWindowSize(new Vector2(200, 200));
                ImGui.Begin("gate");
                ImGui.End();
            });
            OverlayInputGate.Bind(overlay);
            RunFrame(overlay);
            overlay.Input.MouseMove(new Vector2(150, 150));
            RunFrame(overlay);
            RunFrame(overlay);
            Assert.True(OverlayInputGate.SuppressMouse);
            OverlayInputGate.Bind(null);
            Assert.False(OverlayInputGate.SuppressMouse);
        }
        finally
        {
            OverlayInputGate.Bind(null);
            overlay.Dispose();
        }
    }

    [Fact]
    public void LosingFocusReleasesHeldKeys()
    {
        var overlay = new ImGuiOverlay(_ => { });
        try
        {
            overlay.Initialize(new AtlasOnlyRenderer(), () => (new Vector2(800, 600), new Vector2(800, 600)));
            overlay.Input.KeyChange(Silk.NET.Input.Key.A, true);
            RunFrame(overlay);
            overlay.BeginFrame(1f / 60f);
            Assert.True(ImGui.IsKeyDown(ImGuiKey.A));
            overlay.Render(1 / 60.0, 800, 600);
            overlay.Input.Focus(false);
            overlay.BeginFrame(1f / 60f);
            Assert.False(ImGui.IsKeyDown(ImGuiKey.A));
            overlay.Render(1 / 60.0, 800, 600);
        }
        finally
        {
            overlay.Dispose();
        }
    }

    private static void RunFrame(ImGuiOverlay overlay)
    {
        overlay.BeginFrame(1f / 60f);
        overlay.Render(1 / 60.0, 800, 600);
    }

    private sealed class AtlasOnlyRenderer : IImGuiDrawRenderer
    {
        private bool _built;

        public void PrepareFrame(ImGuiIOPtr io, Vector2 framebufferScale)
        {
            if (_built)
                return;
            io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
            io.Fonts.SetTexID(1);
            _built = true;
        }

        public void Render(ImDrawDataPtr drawData, Vector2 framebufferSize)
        {
        }

        public void Dispose()
        {
        }
    }
}
