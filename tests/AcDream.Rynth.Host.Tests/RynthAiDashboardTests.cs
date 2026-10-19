using AcDream.Rynth.Host;
using AcDream.TestSupport;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthAiDashboardTests : IDisposable
{
    private static readonly string[] TheirFiles =
    [
        @"ImGui\Panels\RynthAiFace.cs", @"ImGui\Panels\RynthAiFace.Drawers.cs", @"ImGui\Panels\SettingsRows.cs",
        @"ImGui\Panels\QuickSettingsDrawer.cs", @"ImGui\Panels\SettingsFace.cs", @"UI\Data\RynthAiData.cs", @"UI\Data\SettingsData.cs",
        @"UI\Data\NavData.cs", @"UI\Data\RynthSortData.cs", @"UI\RynthAiDashboardState.cs",
        @"UI\PanelTextScale.cs", @"UI\QuickSettings.cs", @"UI\BarLayout.cs",
    ];

    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-ai-dash-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public RynthAiDashboardTests()
    {
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = _log.Add;
        RynthCore.Engine.UI.Data.UiDataHub.Clear();
    }

    public void Dispose()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
        RynthCore.Engine.UI.Data.PluginExportBinder.Resolver = null;
        ImGuiPanelHost.Reset();
        ImGuiPanelHost.Log = null;
        LoginLifecycleHooks.HasObservedLoginComplete = false;
        _sandbox.Dispose();
    }

    private sealed class ProbeSource(string name, bool throws) : RynthCore.Engine.UI.Data.UiSource(name, 1000)
    {
        public int Polls;

        protected internal override void Poll()
        {
            Polls++;
            if (throws)
                throw new InvalidOperationException("boom");
        }
    }

    [Fact]
    public void ASourceThatThrowsDoesNotStopTheOthersOrRepeatEveryTick()
    {
        var bad = new ProbeSource("Bad", throws: true);
        var good = new ProbeSource("Good", throws: false);
        bad.Subscribe();
        good.Subscribe();
        AcDream.Rynth.EngineUi.RynthAiDashboard.Log = _log.Add;
        try
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            AcDream.Rynth.EngineUi.RynthAiDashboard.PollAll([bad, good], now);
            AcDream.Rynth.EngineUi.RynthAiDashboard.PollAll([bad, good], now);
        }
        finally
        {
            AcDream.Rynth.EngineUi.RynthAiDashboard.Log = _ => { };
        }

        Assert.Equal(1, good.Polls);
        Assert.Equal(1, bad.Polls);
        Assert.Single(_log, line => line == "[RynthAi] Bad poll: InvalidOperationException: boom");
    }

    [Fact]
    public void TheProjectCompilesTheirFilesAndHasNoCopies()
    {
        string root = RepoRoot();
        string project = File.ReadAllText(Path.Combine(root, "src/AcDream.Rynth.EngineUi/AcDream.Rynth.EngineUi.csproj"));
        foreach (string file in TheirFiles)
            Assert.Contains("$(RynthEngine)" + file, project, StringComparison.Ordinal);

        var names = TheirFiles.Select(f => f[(f.LastIndexOf('\\') + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copies = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => names.Contains(Path.GetFileName(p)))
            .ToList();
        Assert.Empty(copies);
    }

    [Fact]
    public void TheDashboardRegistersAndDrawsHeadless()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        RynthAiFace.Register();
        Assert.True(ImGuiPanelHost.HasFace(RynthAiFace.Title));
        ImGuiPanelHost.Open(RynthAiFace.Title);

        for (int i = 0; i < 3; i++)
        {
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
        }

        Assert.Empty(_log);
    }

    [Fact]
    public void TheSettingsPanelRegistersWithTheDashboardAndDrawsEveryTabHeadless()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        Assert.True(ImGuiPanelHost.HasFace(SettingsFace.Title));
        ImGuiPanelHost.Open(SettingsFace.Title);

        foreach (RynthCore.Engine.UI.Data.SettingsTab tab in RynthCore.Engine.UI.Data.SettingsSchema.Tabs)
        {
            SettingsFace.RequestTab(tab.Name);
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 100, $"{tab.Name}: only {vertices} vertices");
        }

        Assert.Empty(_log);
    }

    private (RynthHost Host, RynthChatHookup Exports, RynthAiHookup Ai) StartWithRynthAi()
    {
        var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        var exports = new RynthChatHookup(host, _view, _log.Add);
        var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        return (host, exports, ai);
    }

    private static RynthCore.Engine.UI.Data.RynthAiView? Snapshot() => RynthCore.Engine.UI.Data.UiSources.RynthAi.Current?.Value;

    [Fact]
    public void TheSnapshotReachesTheRynthAiSource()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            RynthCore.Engine.UI.Data.UiSources.RynthAi.Subscribe();
            try
            {
                RynthCore.Engine.UI.Data.UiSources.RynthAi.RequestRefresh();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.NotNull(Snapshot());
                Assert.False(Snapshot()!.Raw.MacroRunning);
                Assert.Equal(host.Plugins[0].Owner.VersionString, Snapshot()!.Version);
            }
            finally { RynthCore.Engine.UI.Data.UiSources.RynthAi.Unsubscribe(); }
        }
    }

    [Fact]
    public void TheMacroToggleRoundTripsThroughThePump()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            RynthCore.Engine.UI.Data.UiSources.RynthAi.Subscribe();
            try
            {
                RynthCore.Engine.UI.Data.RynthAiCommands.ToggleMacro();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.True(Snapshot()!.Raw.MacroRunning);

                RynthCore.Engine.UI.Data.RynthAiCommands.ToggleMacro();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.False(Snapshot()!.Raw.MacroRunning);
            }
            finally { RynthCore.Engine.UI.Data.UiSources.RynthAi.Unsubscribe(); }
        }
    }

    [Fact]
    public void PostedWorkThatThrowsIsLoggedAndTheRestRuns()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        bool ran = false;
        RynthCore.Engine.UI.Data.UiDataHub.Post("boom", () => throw new InvalidOperationException("bad"));
        RynthCore.Engine.UI.Data.UiDataHub.Post("after", () => ran = true);

        AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();

        Assert.True(ran);
        Assert.Contains(_log, l => l.Contains("[RynthAi] boom: InvalidOperationException: bad", StringComparison.Ordinal));
    }

    [Fact]
    public void StopClearsPostedWorkAndTheSnapshot()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            RynthCore.Engine.UI.Data.UiSources.RynthAi.Subscribe();
            try
            {
                RynthCore.Engine.UI.Data.UiSources.RynthAi.RequestRefresh();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.NotNull(Snapshot());
                bool ran = false;
                RynthCore.Engine.UI.Data.UiDataHub.Post("late", () => ran = true);

                AcDream.Rynth.EngineUi.RynthAiDashboard.Stop();
                Assert.Null(Snapshot());

                AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                Assert.False(ran);
            }
            finally { RynthCore.Engine.UI.Data.UiSources.RynthAi.Unsubscribe(); }
        }
    }

    [Fact]
    public void TheHookupPointsTheStandInsUntilDispose()
    {
        using var host = new RynthHost(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);
        var ai = new RynthAiHookup(host, _view, _log.Add);

        Assert.Same(host.Plugins[0].Owner, Assert.Single(RynthCore.Engine.Plugins.PluginManager.Plugins));
        RynthCore.Engine.UI.ServerMenus.Run("/ro fellowship");
        Assert.Contains("/ro fellowship", _view.SubmittedCommands);
        Assert.True(RynthCore.Engine.Compatibility.AcMainThreadQueue.EnqueueWriteToChat("hello", 1));
        Assert.Contains(("hello", 1), _view.ChatLines);

        ai.Dispose();
        Assert.Empty(RynthCore.Engine.Plugins.PluginManager.Plugins);
        Assert.Null(RynthCore.Engine.UI.ServerMenus.Runner);
        Assert.False(RynthCore.Engine.Compatibility.AcMainThreadQueue.EnqueueWriteToChat("gone", 1));
    }

    [Fact]
    public void AThrowingViewIsLoggedNotThrown()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        using var ai = new RynthAiHookup(host, _view, _log.Add);
        _view.Throw = true;
        RynthCore.Engine.Compatibility.AcMainThreadQueue.EnqueueWriteToChat("x", 1);
        Assert.Contains(_log, l => l.Contains("view failed", StringComparison.Ordinal));
    }

    internal static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AcDream.slnx")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
