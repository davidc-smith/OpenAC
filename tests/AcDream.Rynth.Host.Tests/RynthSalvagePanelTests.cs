using AcDream.Rynth.Host;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class RynthSalvagePanelTests
{
    private const uint Ust = 0x80000050, A = 0x80000101, B = 0x80000102, Foreign = 0x80000999;
    private readonly List<string> _log = new();

    private static FakeGameView View()
    {
        var view = new FakeGameView { PlayerId = 0x50000001 };
        view.Tools.Add(Ust);
        view.Owned.UnionWith(new[] { Ust, A, B });
        return view;
    }

    [Fact]
    public void OpenAddExecuteSendsOneSalvageAndKeepsTheTool()
    {
        FakeGameView view = View();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasSalvagePanel);
        Assert.False(host.SalvagePanelAddItem(A));     // not open yet
        Assert.False(host.SalvagePanelOpen(A));        // not a tool
        Assert.True(host.SalvagePanelOpen(Ust));
        Assert.True(host.SalvagePanelAddItem(A));
        Assert.True(host.SalvagePanelAddItem(A));      // repeat: listed once
        Assert.False(host.SalvagePanelAddItem(Ust));   // the tool itself
        Assert.False(host.SalvagePanelAddItem(Foreign));
        Assert.False(host.SalvagePanelAddItem(0));
        Assert.True(host.SalvagePanelAddItem(B));
        Assert.True(host.SalvagePanelExecute());
        Assert.False(host.SalvagePanelExecute());      // list emptied
        Assert.True(host.SalvagePanelAddItem(A));      // tool kept: next round
        Assert.True(host.SalvagePanelExecute());

        Assert.Equal(new[] { "salvage 80000050 80000101,80000102", "salvage 80000050 80000101" }, view.Actions);
    }

    // RynthAi's SalvageManager opens the panel by using the Ust (UseFor → UseObject), never
    // through SalvagePanelOpen; using a Ust only opens the panel, as in retail, so nothing is sent.
    [Fact]
    public void UsingTheUstOpensThePanelAsRynthAiDoes()
    {
        FakeGameView view = View();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.UseObject(Ust));
        Assert.True(host.SalvagePanelAddItem(A));
        Assert.True(host.SalvagePanelAddItem(B));
        Assert.True(host.SalvagePanelExecute());
        Assert.True(host.UseObject(A));               // anything else is an ordinary use

        Assert.Equal(new[] { "salvage 80000050 80000101,80000102", "use 80000101" }, view.Actions);
    }

    [Fact]
    public void TheListHoldsAtMostOneHundredItems()
    {
        FakeGameView view = View();
        for (uint i = 0; i < 101; i++)
            view.Owned.Add(0x80010000 + i);
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.SalvagePanelOpen(Ust));
        for (uint i = 0; i < 100; i++)
            Assert.True(host.SalvagePanelAddItem(0x80010000 + i));
        Assert.False(host.SalvagePanelAddItem(0x80010000 + 100));
    }

    [Fact]
    public void LogoutClosesThePanel()
    {
        FakeGameView view = View();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.SalvagePanelOpen(Ust));
        Assert.True(host.SalvagePanelAddItem(A));
        table.Salvage.Close();
        Assert.False(host.SalvagePanelExecute());
        Assert.False(host.SalvagePanelAddItem(A));
    }

    [Fact]
    public void SalvageDirectFollowsRynthCoresRules()
    {
        FakeGameView view = View();
        using var table = new RynthApiTable(view, _log.Add);
        var host = new RynthCoreHost(*table.Native);

        Assert.True(host.HasSalvageDirect);
        Assert.Equal(1, host.SalvageDirect(Ust, new[] { A, B }));
        Assert.Equal(0, host.SalvageDirect(Ust, new[] { A, 0u }));
        Assert.Equal(0, host.SalvageDirect(Ust, new[] { A, Ust }));
        Assert.Equal(0, host.SalvageDirect(0, new[] { A }));
        Assert.Equal(new[] { "salvage 80000050 80000101,80000102" }, view.Actions);
    }
}
