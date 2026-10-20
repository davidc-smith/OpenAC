using AcDream.Rynth.EngineUi;
using AcDream.TestSupport;
using AcDream.Rynth.Host;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthNavPanelTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-navpanel-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000004u };

    public RynthNavPanelTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        RynthNavPanel.Stop();
        RynthAiDashboard.Stop();
        PluginExportBinder.Resolver = null;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    [Fact]
    public void StartRegistersBothFacesAndRegistersAgainAfterAReset()
    {
        RynthNavPanel.Start();
        Assert.True(ImGuiPanelHost.HasFace(RynthNavFace.Title));
        Assert.True(ImGuiPanelHost.HasFace(RynthNavArrowFace.Title));
        Assert.True(RynthEngineHub.HasParts);

        ImGuiPanelHost.Reset();
        RynthNavPanel.Start();
        Assert.True(ImGuiPanelHost.HasFace(RynthNavFace.Title));
    }

    [Fact]
    public void TheStatusReachesTheSourceWithoutRynthAi()
    {
        _view.Pose = (0xA9B40021u, new System.Numerics.Vector3(100, 100, 50), System.Numerics.Quaternion.Identity);
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthNav.PluginExports)]);
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        RynthNavPanel.Start();
        UiSources.RynthNav.Subscribe();
        try
        {
            for (int i = 0; i < 3; i++)
            {
                host.Tick();
                UiSources.RynthNav.RequestRefresh();
                RynthEngineHub.Tick();
            }

            Assert.True(UiSources.RynthNav.Current?.Value?.HasPose);
        }
        finally { UiSources.RynthNav.Unsubscribe(); }
    }

    [Fact]
    public void ThePanelAndTheArrowDrawHeadless()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        RynthNavPanel.Start();

        RynthCore.Engine.UI.PanelRouter.Toggle(RynthNavFace.Title);
        RynthNavArrowFace.Show();
        try
        {
            Assert.True(ImGuiPanelHost.IsOpen(RynthNavArrowFace.Title));
            for (int i = 0; i < 3; i++)
            {
                RynthEngineHub.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
            }

            Assert.Empty(_log);
        }
        finally
        {
            RynthCore.Engine.UI.PanelRouter.Toggle(RynthNavFace.Title);
            ImGuiPanelHost.Close(RynthNavArrowFace.Title);
        }
    }
}
