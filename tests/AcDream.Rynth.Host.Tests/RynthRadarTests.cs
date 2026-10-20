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
        MapTilesReset.Settle();
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
    public void ARatioOnlyObjectAnswersItsRatioWithNoVitals()
    {
        _view.HealthRatios[0x80000005u] = 0.6f;
        using var hookup = new RynthMapHookup(_view, _log.Add);
        Assert.True(MonsterHudData.TryGetHealth(0x80000005u, out HealthObservation h));
        Assert.Equal(0.6f, h.Ratio);
        Assert.Equal(0u, h.Current);
        Assert.Equal(0u, h.Max);
    }

    [Fact]
    public void TheLatestRatioWinsOverTheAppraisalAndVitalsRideAlong()
    {
        _view.TargetVitals[0x80000005u] = new RynthVitals(30, 120, 0, 0, 0, 0);
        _view.HealthRatios[0x80000005u] = 0.1f;
        using var hookup = new RynthMapHookup(_view, _log.Add);
        Assert.True(MonsterHudData.TryGetHealth(0x80000005u, out HealthObservation h));
        Assert.Equal(0.1f, h.Ratio);
        Assert.Equal(30u, h.Current);
        Assert.Equal(120u, h.Max);
    }

    [Fact]
    public void AnUnknownObjectHasNoHealth()
    {
        _view.TargetVitals[0x80000006u] = new RynthVitals(30, 120, 0, 0, 0, 0);   // vitals alone: no ratio seen
        using var hookup = new RynthMapHookup(_view, _log.Add);
        Assert.False(MonsterHudData.TryGetHealth(0x80000006u, out _));
        Assert.False(MonsterHudData.TryGetHealth(0x80000007u, out _));
    }

    [Fact]
    public void AFailingViewAnswersNoHealthAndLogs()
    {
        _view.HealthRatios[0x80000005u] = 0.6f;
        using var hookup = new RynthMapHookup(_view, _log.Add);
        _view.Throw = true;
        Assert.False(MonsterHudData.TryGetHealth(0x80000005u, out _));
        Assert.Contains(_log, l => l.StartsWith("[Rynth] map ") && l.Contains("view failed"));
    }

    [Fact]
    public void DisposingAnEarlierHookupLeavesALaterHealthReader()
    {
        _view.HealthRatios[0x80000005u] = 0.6f;
        var first = new RynthMapHookup(_view, _log.Add);
        using var second = new RynthMapHookup(_view, _log.Add);
        first.Dispose();
        Assert.True(MonsterHudData.TryGetHealth(0x80000005u, out _));
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
    public void TheTextSizeMenuDrawsInsideAMenu()
    {
        using var imgui = new HeadlessImGui();
        Exception? error = null;
        for (int i = 0; i < 2; i++)
            imgui.Frame(() =>
            {
                try
                {
                    ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 200));
                    ImGuiNET.ImGui.Begin("menu host", ImGuiNET.ImGuiWindowFlags.MenuBar);
                    if (ImGuiNET.ImGui.BeginMenuBar())
                    {
                        if (ImGuiNET.ImGui.BeginMenu("Options"))
                        {
                            ImGuiPanelHost.TextSizeMenu("Radar");
                            ImGuiNET.ImGui.EndMenu();
                        }
                        ImGuiNET.ImGui.EndMenuBar();
                    }
                    ImGuiNET.ImGui.End();
                }
                catch (Exception e) { error ??= e; }
            });
        Assert.Null(error);
        Assert.Empty(_log);
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
            // With no radar snapshot the Radar draws no map backdrop, so it never starts
            // WorldMapMaps.Refresh. If it ever does, Dispose must pass awaitScan: true.
            Assert.False(WorldMapMaps.Scanned, "the Radar started a World Map scan; Dispose must settle it with awaitScan: true");
        }
        finally { LoginLifecycleHooks.HasObservedLoginComplete = false; }
    }
}
