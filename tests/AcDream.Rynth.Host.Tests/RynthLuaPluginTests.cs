using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthLuaPluginTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-lua-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public RynthLuaPluginTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    private RynthHost CreateHost() => new(
        _view,
        _log.Add,
        [typeof(RynthCore.Plugin.RynthAi.PluginExports), typeof(RynthCore.Plugin.RynthLua.PluginExports)]);

    [Fact]
    public void RynthLuaLoadsAndTicksBesideRynthAi()
    {
        using RynthHost host = CreateHost();
        RynthPlugin lua = Assert.Single(host.Plugins, p => p.Name.Contains("RynthLua", StringComparison.Ordinal));

        for (int i = 0; i < 30; i++)
            host.Tick();

        Assert.True(lua.IsActive);
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void TheLuaPanelRunsCodeAndShowsItsConsole()
    {
        using RynthHost host = CreateHost();
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UiSources.Lua.Subscribe();
        try
        {
            LuaCommands.Send("run", "probe", "print('hello from ' .. 'lua')");
            for (int i = 0; i < 10 && !(UiSources.Lua.Current?.Value.Console.Contains("hello from lua", StringComparison.Ordinal) ?? false); i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                host.Tick();
                UiSources.Lua.RequestRefresh();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
            }

            Assert.Contains("hello from lua", UiSources.Lua.Current?.Value.Console ?? string.Empty, StringComparison.Ordinal);
        }
        finally { UiSources.Lua.Unsubscribe(); }
    }

    [Fact]
    public void AScriptCommandTypedInChatIsEatenAndRunsItsHandler()
    {
        using RynthHost host = CreateHost();
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        UiSources.Lua.Subscribe();
        try
        {
            string Console() => UiSources.Lua.Current?.Value.Console ?? string.Empty;
            void Pump(Func<bool> done)
            {
                for (int i = 0; i < 20 && !done(); i++)
                {
                    AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                    host.Tick();
                    UiSources.Lua.RequestRefresh();
                    AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                }
            }

            LuaCommands.Send("run", "probe",
                "game.RegisterCommand('/hello', function(args) print('hello got ' .. args) end) print('registered')");
            Pump(() => Console().Contains("registered", StringComparison.Ordinal));
            Assert.Contains("registered", Console(), StringComparison.Ordinal);

            // As the client's chat interceptor offers a typed line (RynthClientExtension.OfferChatBarText).
            bool Offer(string line) => RynthClientExtension.OfferToPlugins(
                host.Plugins.Select(plugin => new RynthClientExtension.ChatBarOffer(plugin.OnChatBarEnter)), line);

            Assert.True(Offer("/hello there"));
            Assert.False(Offer("/goodbye there"));
            Pump(() => Console().Contains("hello got there", StringComparison.Ordinal));

            Assert.Contains("hello got there", Console(), StringComparison.Ordinal);
        }
        finally { UiSources.Lua.Unsubscribe(); }
    }

    [Fact]
    public void TheLuaPanelRegistersWithTheDashboardAndDrawsHeadless()
    {
        using RynthHost host = CreateHost();
        using var exports = new RynthChatHookup(host, _view, _log.Add);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        RynthCore.Engine.UI.PanelRouter.Toggle(LuaFace.Title);

        for (int i = 0; i < 3; i++)
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
            host.Tick();
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 300, $"frame {i}: only {vertices} vertices");
        }

        Assert.True(ImGuiPanelHost.IsOpen(LuaFace.Title));
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal));
    }
}
