using AcDream.Rynth.Host;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.ScriptWindows;
using RynthCore.PluginSdk;

namespace AcDream.Rynth.Host.Tests;

public sealed unsafe class ScriptWindowPipelineTests : IDisposable
{
    private const string OracleKey = "RynthOracle/Oracle";
    private readonly HeadlessImGui _imgui = new();
    private readonly List<string> _log = new();
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    public ScriptWindowPipelineTests()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
    }

    public void Dispose()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = false;
        _imgui.Dispose();
    }

    private RynthHost CreateOracleHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthOracle.PluginExports)], scriptWindows: true);

    private string Diag() => string.Join("\n", ScriptWindowRegistry.Describe()) + "\n" + string.Join("\n", _log);

    // Describe() prints the owner's last submitted flag as "visible=True/False".
    private static string OracleLine() =>
        ScriptWindowRegistry.Describe().FirstOrDefault(l => l.Contains(OracleKey)) ?? "(no oracle window)";

    [Fact]
    public void HasUiFollowsTheScriptWindowsFlag()
    {
        using (var with = new RynthApiTable(_view, _log.Add, scriptWindows: true))
            Assert.True(new RynthCoreHost(*with.Native).HasUi);
        using (var without = new RynthApiTable(_view, _log.Add))
            Assert.False(new RynthCoreHost(*without.Native).HasUi);
    }

    [Fact]
    public void SubmitOutsideADispatchIsRefused()
    {
        using var table = new RynthApiTable(_view, _log.Add, scriptWindows: true);
        var host = new RynthCoreHost(*table.Native);
        Assert.Equal(-2, host.UiSubmit(new byte[16]));
    }

    [Fact]
    public void SlashRoOpensOraclesWindowAndImGuiDrawsIt()
    {
        using RynthHost host = CreateOracleHost();

        host.Plugins[0].OnChatBarEnter("/ro");
        host.Tick();
        host.Tick();

        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey), Diag());
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        int vertices = _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.True(vertices > 100, $"only {vertices} vertices\n{Diag()}");
    }

    [Fact]
    public void APlayerCloseReachesThePluginAndTheWindowStaysClosed()
    {
        using RynthHost host = CreateOracleHost();
        host.Plugins[0].OnChatBarEnter("/ro");
        host.Tick();
        host.Tick();
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        _imgui.Frame(ImGuiPanelHost.DrawAll);

        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey), Diag());
        Assert.Contains("visible=True", OracleLine());

        ImGuiPanelHost.Close("script:" + OracleKey);
        for (int i = 0; i < 5; i++)
            host.Tick();

        Assert.False(ScriptWindowRegistry.IsOpen(OracleKey), Diag());
        // The plugin heard the Visibility event and now submits the window as hidden.
        Assert.Contains("visible=False", OracleLine());
    }

    [Fact]
    public void AWindowSavedAsOpenIsReopenedAndThePluginHearsOfIt()
    {
        var layout = new FakePanelLayout();
        layout.Saved["script:" + OracleKey] = new RynthCore.Engine.ImGuiBackend.PanelPlacement(null, true);
        ImGuiPanelHost.Layout = layout;
        using RynthHost host = CreateOracleHost();
        host.Tick();
        host.Tick();
        Assert.Contains("visible=False", OracleLine());

        // Out of the world nothing is restored; at login the saved-open window reopens.
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        Assert.False(ScriptWindowRegistry.IsOpen(OracleKey), Diag());
        RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete = true;
        _imgui.Frame(ImGuiPanelHost.DrawAll);
        for (int i = 0; i < 5; i++)
            host.Tick();

        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey), Diag());
        Assert.Contains("visible=True", OracleLine());
    }

    [Fact]
    public void DisposingTheHostRemovesItsWindows()
    {
        RynthHost host = CreateOracleHost();
        host.Plugins[0].OnChatBarEnter("/ro");
        host.Tick();
        host.Tick();
        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey), Diag());
        Assert.Equal(1, ImGuiPanelHost.OpenCount);

        host.Dispose();

        Assert.False(ScriptWindowRegistry.IsOpen(OracleKey));
        Assert.Equal(0, ImGuiPanelHost.OpenCount);
        Assert.False(EngineSettings.EnableImGuiBackend);
    }
}
