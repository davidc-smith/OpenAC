using AcDream.Rynth.EngineUi;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI;
using RynthCore.Engine.UI.Sections;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthMiniRemoteTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-mremote-");
    private readonly HeadlessImGui _imgui = new();

    public RynthMiniRemoteTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        RynthAiDashboard.Stop();
        ImGuiPanelHost.Reset();
        _imgui.Dispose();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    private static SectionRegistry.Snapshot StartAndTick()
    {
        RynthAiDashboard.Start();
        SectionRegistry.Invalidate();
        RynthAiDashboard.Tick();
        return SectionRegistry.Current;
    }

    private void Frames(int n)
    {
        for (int i = 0; i < n; i++)
            _imgui.Frame(ImGuiPanelHost.DrawAll);
    }

    /// <summary>
    /// True when the last frame's Mini Remote draw list holds the engine grid's status row:
    /// text clipped to its own 18-pixel strip (MiniRemoteFace.DrawStatus).
    /// </summary>
    private static unsafe bool StatusRowDrawn()
    {
        ImGuiNET.ImDrawDataPtr data = ImGuiNET.ImGui.GetDrawData();
        for (int i = 0; i < data.CmdListsCount; i++)
        {
            ImGuiNET.ImDrawListPtr list = data.CmdLists[i];
            string owner = list._OwnerName.ToString() ?? "";
            if (!owner.StartsWith(MiniRemoteFace.Title, StringComparison.Ordinal))
                continue;
            for (int c = 0; c < list.CmdBuffer.Size; c++)
            {
                ImGuiNET.ImDrawCmdPtr cmd = list.CmdBuffer[c];
                float h = cmd.ClipRect.W - cmd.ClipRect.Y;
                if (cmd.ElemCount > 0 && Math.Abs(h - 18f) < 0.5f)
                    return true;
            }
        }
        return false;
    }

    /// <summary>True when the last frame drew a window whose name contains <paramref name="part"/> (a child's name holds its id).</summary>
    private static bool DrewChild(string part)
    {
        ImGuiNET.ImDrawDataPtr data = ImGuiNET.ImGui.GetDrawData();
        for (int i = 0; i < data.CmdListsCount; i++)
            if ((data.CmdLists[i]._OwnerName.ToString() ?? "").Contains(part, StringComparison.Ordinal))
                return true;
        return false;
    }

    [Fact]
    public void TheCoreSectionsHoldOurEnginePanels()
    {
        SectionRegistry.Snapshot snap = StartAndTick();

        Assert.Equal(
            ["char", "map", "skills", "combat", "nav", "inventory", "crafting", "spells", "chat", "friends", "fellowship", "pets"],
            snap.Sections.Select(v => v.Id));
        string[] Panes(string id) => snap.Find(id)!.Panes.Select(p => p.Title).ToArray();
        Assert.Equal(["Dungeon Map"], Panes("map"));
        Assert.Equal(["Skills"], Panes("skills"));
        Assert.Equal(["Damage"], Panes("combat"));
        Assert.Equal(["Nav"], Panes("nav"));
        Assert.Equal(["Item Count", "Items"], Panes("inventory"));
        Assert.Equal(SectionPaneKind.Overview, Assert.Single(snap.Find("char")!.Panes).Kind);
        Assert.All(snap.Sections, v => Assert.Empty(v.Groups));
        Assert.Equal(["ilt", "aelrynth"], snap.Servers.Select(s => s.Id));
        Assert.All(snap.Servers, s => Assert.False(s.Active));
    }

    [Fact]
    public void TheDashboardRegistersTheMiniRemoteAndItDrawsTheGrid()
    {
        StartAndTick();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        Assert.True(ImGuiPanelHost.HasFace(MiniRemoteFace.Title));
        MiniRemoteFace.RequestSection(SectionCatalog.RemoteId);
        ImGuiPanelHost.Open(MiniRemoteFace.Title);

        Frames(4);
        Assert.True(StatusRowDrawn(), "the engine grid's status row never reached the Mini Remote's draw list");
        void After() { ImGuiNET.ImGui.Begin("after"); ImGuiNET.ImGui.Text("ok"); ImGuiNET.ImGui.End(); }
        _imgui.Frame(After);   // a new auto-sized window draws nothing on its first frame
        Assert.True(_imgui.Frame(After) > 0);
        Assert.DoesNotContain(_log, l => l.Contains("Mini Remote", StringComparison.Ordinal));
    }

    [Fact]
    public void ARailSectionEmbedsItsPanel()
    {
        StartAndTick();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        MiniRemoteFace.RequestSection("skills");
        ImGuiPanelHost.Open(MiniRemoteFace.Title);

        Frames(4);

        Assert.True(MiniRemoteFace.ShowsSection("skills"));
        Assert.True(DrewChild("##embed_Skills"), "the Skills section drew no embedded Skills copy");
        Assert.DoesNotContain(_log, l => l.Contains("embedded Skills", StringComparison.Ordinal) || l.Contains("embedding Skills", StringComparison.Ordinal));
    }

    [Fact]
    public void RcMiniremoteOpensSectionsAndRejectsBadOnes()
    {
        StartAndTick();
        var replies = new List<string>();
        var ran = new List<string>();
        ServerMenus.Runner = ran.Add;
        try
        {
            Assert.True(RynthCoreChatCommands.TryHandle("miniremote section map", replies.Add));
            _imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(ImGuiPanelHost.IsOpen(MiniRemoteFace.Title));
            LoginLifecycleHooks.HasObservedLoginComplete = true;
            Frames(1);
            Assert.True(MiniRemoteFace.ShowsSection("map"));

            RynthCoreChatCommands.TryHandle("miniremote section bogus", replies.Add);
            Assert.StartsWith("Usage: /rc miniremote section <remote|char|map|", replies[^1]);

            RynthCoreChatCommands.TryHandle("miniremote toggle", replies.Add);
            Assert.False(ImGuiPanelHost.IsOpen(MiniRemoteFace.Title));

            RynthCoreChatCommands.TryHandle("miniremote tab character", replies.Add);
            Assert.Equal(["/ro character"], ran);

            RynthCoreChatCommands.TryHandle("bogus", replies.Add);
            Assert.Equal("/rc bogus isn't in OpenAC (only /rc miniremote).", replies[^1]);
        }
        finally
        {
            ServerMenus.Runner = null;
        }
    }
}
