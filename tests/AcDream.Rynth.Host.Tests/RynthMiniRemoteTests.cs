using AcDream.Rynth.EngineUi;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.Sections;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthMiniRemoteTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-mremote-");

    public RynthMiniRemoteTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
    }

    public void Dispose()
    {
        RynthAiDashboard.Stop();
        ImGuiPanelHost.Reset();
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
}
