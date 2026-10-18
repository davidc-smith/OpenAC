using System.Numerics;
using AcDream.Rynth.Host;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthBarTests : IDisposable
{
    private const string OracleKey = "RynthOracle/Oracle";
    private readonly HeadlessImGui _imgui = new();
    private readonly List<string> _log = new();
    private readonly RynthLayoutStore _store;
    private readonly FakeGameView _view = new()
    {
        PlayerId = 0x50000000u | (uint)Random.Shared.Next(1, 0xFFFF),
    };

    public RynthBarTests()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        _store = new RynthLayoutStore(null, _log.Add);
    }

    public void Dispose()
    {
        ScriptWindowRegistry.Reset();
        ImGuiPanelHost.Reset();
        _imgui.Dispose();
    }

    private RynthHost CreateOracleHost()
    {
        var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthOracle.PluginExports)], scriptWindows: true);
        host.Tick();
        host.Tick();
        return host;
    }

    private static string OracleLine() =>
        ScriptWindowRegistry.Describe().FirstOrDefault(l => l.Contains(OracleKey)) ?? "(no oracle window)";

    private static Vector2 BarPos()
    {
        ImGui.Begin("##RynthBar");
        Vector2 pos = ImGui.GetWindowPos();
        ImGui.End();
        return pos;
    }

    [Fact]
    public void WithNothingOnItTheBarDrawsNothing()
    {
        var bar = new RynthBar(_store, _log.Add);

        Assert.False(RynthBar.HasContent);
        Assert.Equal(0, _imgui.Frame(bar.Draw));
    }

    [Fact]
    public void OraclesWindowsPutTheBarOnScreen()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);

        Assert.True(RynthBar.HasContent);
        _imgui.Frame(bar.Draw);
        Assert.True(_imgui.Frame(bar.Draw) > 0);
    }

    [Fact]
    public void ToggleOpensAndClosesTheWindowAndThePluginHearsOfIt()
    {
        using RynthHost host = CreateOracleHost();
        ScriptBarItem item = ScriptWindowRegistry.BarItems.First(i => i.PanelKey.EndsWith(OracleKey, StringComparison.OrdinalIgnoreCase));

        Assert.True(RynthBar.Toggle(item));
        for (int i = 0; i < 5; i++)
            host.Tick();
        Assert.True(ScriptWindowRegistry.IsOpen(OracleKey));
        Assert.Contains("visible=True", OracleLine());

        Assert.False(RynthBar.Toggle(item));
        for (int i = 0; i < 5; i++)
            host.Tick();
        Assert.False(ScriptWindowRegistry.IsOpen(OracleKey));
    }

    [Fact]
    public void TheBarStartsWhereItWasLeft()
    {
        _store.SetBarPosition(new Vector2(300, 150));
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);
        Vector2 pos = default;

        _imgui.Frame(() => { bar.Draw(); pos = BarPos(); });

        Assert.Equal(new Vector2(300, 150), pos);
    }

    [Fact]
    public void WithNothingSavedTheBarStartsAtItsDefaultSpot()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);
        Vector2 pos = default;

        _imgui.Frame(() => { bar.Draw(); pos = BarPos(); });

        Assert.Equal(RynthBar.DefaultPos, pos);
    }

    [Fact]
    public void AMovedBarIsSaved()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add);
        _imgui.Frame(bar.Draw);

        _imgui.Frame(() => { ImGui.SetWindowPos("##RynthBar", new Vector2(200, 100)); bar.Draw(); });

        Assert.Equal(new Vector2(200, 100), _store.BarPosition);
    }

    [Fact]
    public void ABarThatKeepsFailingStopsAndImGuiStaysBalanced()
    {
        using RynthHost host = CreateOracleHost();
        var bar = new RynthBar(_store, _log.Add) { ContentsHook = () => throw new InvalidOperationException("boom") };

        for (int i = 0; i < 6; i++)
            _imgui.Frame(bar.Draw);
        void After() { ImGui.Begin("after"); ImGui.TextUnformatted("fine"); ImGui.End(); }
        _imgui.Frame(After);

        Assert.Equal(3, _log.Count(line => line.Contains("bar", StringComparison.OrdinalIgnoreCase) && line.Contains("boom", StringComparison.Ordinal)));
        Assert.True(_imgui.Frame(After) > 0);
    }

    [Fact]
    public void AToggleThatThrowsOnAnOpenButtonLeavesImGuiBalanced()
    {
        using RynthHost host = CreateOracleHost();
        ScriptBarItem item = ScriptWindowRegistry.BarItems.First(i => i.PanelKey.EndsWith(OracleKey, StringComparison.OrdinalIgnoreCase));
        // Open, so the button pushes a style colour around itself.
        RynthBar.Toggle(item);
        for (int i = 0; i < 5; i++)
            host.Tick();
        Assert.True(ImGuiPanelHost.IsOpen(item.PanelKey));
        int toggles = 0;
        var bar = new RynthBar(_store, _log.Add)
        {
            ButtonSource = () => [item],
            ToggleAction = _ => { toggles++; throw new InvalidOperationException("boom"); },
        };
        _imgui.Frame(bar.Draw);
        Vector2 pos = default;
        _imgui.Frame(() => { bar.Draw(); pos = BarPos(); });
        // Second button: past the "::" grip button, its spacing and the window padding.
        ImGuiStylePtr style = ImGui.GetStyle();
        float grip = ImGui.CalcTextSize("::").X + style.FramePadding.X * 2;
        var target = pos + new Vector2(style.WindowPadding.X + grip + style.ItemSpacing.X + 6, style.WindowPadding.Y + 6);

        ImGuiIOPtr io = ImGui.GetIO();
        io.AddMousePosEvent(target.X, target.Y);
        _imgui.Frame(bar.Draw);
        io.AddMouseButtonEvent(0, true);
        _imgui.Frame(bar.Draw);
        io.AddMouseButtonEvent(0, false);
        _imgui.Frame(bar.Draw);

        Assert.True(toggles > 0, "the click never reached the toggle");
        Assert.Contains(_log, line => line.Contains("boom", StringComparison.Ordinal));
        void After() { ImGui.Begin("after"); ImGui.TextUnformatted("fine"); ImGui.End(); }
        _imgui.Frame(After); // an auto-sized window draws nothing on its first frame
        Assert.True(_imgui.Frame(After) > 0);
    }

    private sealed class NullPanel : IImGuiPanel
    {
        public void Draw() { }
    }

    [Fact]
    public void AChatPanelButtonShowsWithoutScriptWindowsAndToggles()
    {
        ImGuiPanelHost.Register("Chat", new PanelSpec(new Vector2(460, 300), new Vector2(260, 140)), () => new NullPanel());
        var bar = new RynthBar(_store, _log.Add) { PanelButtons = () => ["Chat"] };
        Assert.True(bar.HasAnyContent);
        bar.ClickPanel("Chat");
        Assert.True(ImGuiPanelHost.IsOpen("Chat"));
        bar.ClickPanel("Chat");
        Assert.False(ImGuiPanelHost.IsOpen("Chat"));
    }

    [Fact]
    public void AnUnregisteredPanelHasNoButton()
    {
        var bar = new RynthBar(_store, _log.Add) { PanelButtons = () => ["Chat"] };
        Assert.False(bar.HasAnyContent);
    }
}
