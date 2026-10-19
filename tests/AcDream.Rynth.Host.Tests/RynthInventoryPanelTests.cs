using AcDream.Rynth.EngineUi;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Sections;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthInventoryPanelTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-invpanel-");
    private readonly HeadlessImGui _imgui = new();
    private readonly bool _classic = InventorySettingsStore.Classic;
    private readonly Func<byte[], int, int, IntPtr>? _upload = DX9Backend.Upload;

    public RynthInventoryPanelTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        RynthAiDashboard.Stop();
        ImGuiPanelHost.Reset();
        DX9Backend.Upload = _upload;
        ProcessPathStandIn.DatDirectory = null;
        InventorySettingsStore.SetClassic(_classic);   // the sandbox redirects the file, but the flag is static
        _imgui.Dispose();
        ImGuiPanelHost.Log = null;
        _sandbox.Dispose();
    }

    /// <summary>Draws n frames; the model captures the game on its main-thread tick, throttled by real time.</summary>
    private void DrawFrames(int n)
    {
        for (int i = 0; i < n; i++)
            _imgui.Frame(() => { InventoryModel.MainThreadTick(); RynthAiDashboard.BeginFrame(); ImGuiPanelHost.DrawAll(); });
    }

    [Fact]
    public void TheDashboardRegistersTheInventoryAndItDrawsInBothLooks()
    {
        using var fx = new InventoryFixture();
        RynthAiDashboard.Start();
        Assert.True(ImGuiPanelHost.HasFace(InventoryFace.Title));
        Assert.True(ImGuiPanelHost.CanEmbed(InventoryFace.Title));
        InventorySettingsStore.SetClassic(true);
        ImGuiPanelHost.Open(InventoryFace.Title);
        DrawFrames(3);
        Thread.Sleep(150);
        DrawFrames(3);
        Assert.NotEqual(0u, InventoryModel.Current.PlayerId);
        InventorySettingsStore.SetClassic(false);
        DrawFrames(3);
        Assert.DoesNotContain(_log, l => l.Contains("stopped drawing") || l.Contains("Exception"));
    }

    [Fact]
    public void RcInventoryTogglesThePanel()
    {
        RynthAiDashboard.Start();
        var replies = new List<string>();
        RynthCoreChatCommands.TryHandle("inventory", replies.Add);
        Assert.True(ImGuiPanelHost.IsOpen(InventoryFace.Title));
        RynthCoreChatCommands.TryHandle("inventory", replies.Add);
        Assert.False(ImGuiPanelHost.IsOpen(InventoryFace.Title));
        Assert.Empty(replies);
    }

    [Fact]
    public void RcInventoryWithoutTheFaceSaysSo()
    {
        var replies = new List<string>();
        RynthCoreChatCommands.TryHandle("inventory", replies.Add);
        Assert.Equal(["The Inventory panel isn't available (the ImGui layer is off?)."], replies);
    }

    [Fact]
    public void ClassicArtIsUploadedThroughTheOverlay()
    {
        string dats = TestDats.Directory;
        if (!File.Exists(Path.Combine(dats, "client_portal.dat")))
            return;   // xunit 2 has no dynamic skip (as the Skills dat test)

        using var fx = new InventoryFixture();
        int n = 0;
        DX9Backend.Upload = (_, _, _) => (IntPtr)(++n);
        ProcessPathStandIn.DatDirectory = dats;
        RynthAiDashboard.Start();
        InventorySettingsStore.SetClassic(true);
        ImGuiPanelHost.Open(InventoryFace.Title);
        for (int i = 0; i < 50 && Volatile.Read(ref n) == 0; i++)
        {
            DrawFrames(1);
            Thread.Sleep(20);
        }
        Assert.True(Volatile.Read(ref n) > 0, "Classic never uploaded a retail picture");
    }
}
