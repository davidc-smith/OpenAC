using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Hud;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthRadarTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-radar-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public RynthRadarTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        WorldMapTiles.Shutdown();
        AcMainThreadQueue.SelectObject = null;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        _sandbox.Dispose();
    }

    [Fact]
    public void TheDashboardRegistersTheRadarAndKeepsInventory()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        Assert.True(ImGuiPanelHost.HasFace(RadarFace.Title));
        Assert.True(ImGuiPanelHost.HasFace(InventoryFace.Title));
    }

    [Fact]
    public void HealthComesFromTheTargetsVitals()
    {
        _view.TargetVitals[0x80000005u] = new RynthVitals(30, 120, 0, 0, 0, 0);
        _view.TargetVitals[0x80000006u] = new RynthVitals(0, 0, 0, 0, 0, 0);
        using var hookup = new RynthMapHookup(_view, _log.Add);
        Assert.True(MonsterHudData.TryGetHealth(0x80000005u, out HealthObservation h));
        Assert.Equal(0.25f, h.Ratio);
        Assert.False(MonsterHudData.TryGetHealth(0x80000006u, out _));
        Assert.False(MonsterHudData.TryGetHealth(0x80000007u, out _));
    }

    [Fact]
    public void AClickOnADotSelectsIt()
    {
        uint selected = 0;
        AcMainThreadQueue.SelectObject = id => { selected = id; return true; };
        Assert.True(AcMainThreadQueue.EnqueueOverlaySelect(0x80000009u));
        Assert.Equal(0x80000009u, selected);
    }

    [Fact]
    public void TheBarHasARadarButton() =>
        Assert.Equal(["Chat", "RynthAi", "RynthNav", "Radar"], RynthClientExtension.BarPanels);

    [Fact]
    public void TheRadarDrawsWithNoErrors()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        try
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
            RynthCore.Engine.UI.PanelRouter.Toggle(RadarFace.Title);
            for (int i = 0; i < 3; i++)
                imgui.Frame(() => { AcDream.Rynth.EngineUi.RynthAiDashboard.BeginFrame(); ImGuiPanelHost.DrawAll(); });
            Assert.True(ImGuiPanelHost.IsOpen(RadarFace.Title));
            Assert.Empty(_log);
        }
        finally { LoginLifecycleHooks.HasObservedLoginComplete = false; }
    }
}
