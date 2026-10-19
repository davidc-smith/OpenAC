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
        @"ImGui\Panels\QuickSettingsDrawer.cs", @"ImGui\Panels\SettingsFace.cs", @"ImGui\Panels\ItemsFace.cs", @"ImGui\Panels\DamageFace.cs",
        @"ImGui\Panels\DamageDetailFace.cs", @"UI\Data\ItemsData.cs", @"UI\Data\DamageData.cs", @"UI\PanelColumnStore.cs", @"ImGui\Panels\NavFace.cs", @"ImGui\Panels\MetaFace.cs",
        @"ImGui\Panels\MetaFace.List.cs", @"ImGui\Panels\MetaFace.Editor.cs", @"ImGui\Panels\MetaFace.Schedule.cs",
        @"UI\Data\MetaData.cs", @"UI\Data\MetaEditing.cs", @"UI\Data\MetaLocation.cs", @"UI\Data\MetaRowText.cs",
        @"UI\Data\MetaRuleDto.cs", @"UI\Data\MetaStateClock.cs", @"ImGui\CodeEditor.cs", @"ImGui\Panels\LootEditorFace.cs",
        @"ImGui\Panels\LootAddDialog.cs", @"UI\Data\LootEditData.cs", @"ImGui\Panels\RangesSlideOut.cs", @"UI\Data\RynthAiData.cs", @"UI\Data\SettingsData.cs",
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

    [Fact]
    public void TheItemsDamageAndMonsterDetailPanelsRegisterWithTheDashboardAndDrawHeadless()
    {
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        ImGuiPanelHost.Open(ItemsFace.Title);
        ImGuiPanelHost.Open(DamageFace.Title);
        DamageDetailFace.Show(0x1234u, "Drudge Skulker");

        for (int i = 0; i < 3; i++)
        {
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 300, $"frame {i}: only {vertices} vertices");
        }

        Assert.True(ImGuiPanelHost.IsOpen(ItemsFace.Title));
        Assert.True(ImGuiPanelHost.IsOpen(DamageFace.Title));
        Assert.True(ImGuiPanelHost.IsOpen(DamageDetailFace.Title));
        Assert.Empty(_log);
    }

    [Fact]
    public void TheMonstersButtonTogglesTheDamagePanel()
    {
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();

        RynthCore.Engine.UI.PanelRouter.ToggleMonsters();
        Assert.True(ImGuiPanelHost.IsOpen(DamageFace.Title));
        Assert.Equal(DamageFace.Title, RynthCore.Engine.UI.PanelRouter.Canonical("Monsters"));

        RynthCore.Engine.UI.PanelRouter.ToggleMonsters();
        Assert.False(ImGuiPanelHost.IsOpen(DamageFace.Title));
    }

    [Fact]
    public void AnItemsEditRoundTripsThroughThePlugin()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            var items = RynthCore.Engine.UI.Data.UiSources.Items;
            items.Subscribe();
            try
            {
                items.RequestRefresh();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                RynthCore.Engine.UI.Panels.ItemsPanel.Payload payload = Assert.IsType<RynthCore.Engine.UI.Data.ItemsSnapshot>(items.Current?.Value).ParseCopy();
                int keep = payload.ManaStoneKeepCount + 3;
                payload.ManaStoneKeepCount = keep;

                RynthCore.Engine.UI.Data.ItemsCommands.SetJson(RynthCore.Engine.UI.Data.ItemsCommands.Serialize(payload));
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();

                Assert.Equal(keep, items.Current!.Value.Parsed.ManaStoneKeepCount);
            }
            finally { items.Unsubscribe(); }
        }
    }

    [Fact]
    public void TheMonsterRulesAndDamageSourcesReadThePlugin()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            var rules = RynthCore.Engine.UI.Data.UiSources.MonsterRules;
            var damage = RynthCore.Engine.UI.Data.UiSources.Damage;
            rules.Subscribe();
            damage.Subscribe();
            try
            {
                rules.RequestRefresh();
                damage.RequestRefresh();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();

                Assert.NotNull(rules.Current);
                Assert.True(damage.Current?.Value.Bound);
            }
            finally
            {
                rules.Unsubscribe();
                damage.Unsubscribe();
            }
        }
    }

    [Fact]
    public void TheNavMetaAndLootEditorPanelsRegisterWithTheDashboardAndDrawHeadless()
    {
        (RynthHost host, RynthChatHookup exports, RynthAiHookup ai) = StartWithRynthAi();
        using (host) using (exports) using (ai)
        {
            using var imgui = new HeadlessImGui();
            LoginLifecycleHooks.HasObservedLoginComplete = true;
            RynthCore.Engine.UI.PanelRouter.Toggle("Nav");
            RynthCore.Engine.UI.PanelRouter.Toggle("Meta");
            LootEditorFace.ToggleFor(null);

            for (int i = 0; i < 3; i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 300, $"frame {i}: only {vertices} vertices");
            }

            Assert.True(ImGuiPanelHost.IsOpen(NavFace.Title));
            Assert.True(ImGuiPanelHost.IsOpen(MetaFace.Title));
            Assert.True(ImGuiPanelHost.IsOpen(LootEditorFace.Title));
            Assert.NotNull(RynthCore.Engine.UI.Data.UiSources.Meta.Current);
            Assert.NotNull(RynthCore.Engine.UI.Data.UiSources.LootEdit.Current);
            Assert.DoesNotContain(_log, l => l.Contains("[RynthAi]", StringComparison.Ordinal) && l.Contains("Exception", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheRangesDrawerDrawsHeadless()
    {
        using var imgui = new HeadlessImGui();
        var ranges = new RangesSlideOut();
        void Draw()
        {
            ImGuiNET.ImGui.SetNextWindowSize(new System.Numerics.Vector2(300, 450));
            ImGuiNET.ImGui.Begin("ranges-host");
            ranges.DrawPanel(ImGuiNET.ImGui.GetCursorScreenPos(), ranges.Width(1), 400, 1);
            ImGuiNET.ImGui.End();
        }

        imgui.Frame(Draw);   // a new window is hidden for its first frame
        int vertices = imgui.Frame(Draw);
        Assert.True(vertices > 100, $"only {vertices} vertices");
    }

    [Fact]
    public void TheHookupAnswersObjectFactsAndThePlayerPosition()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        var hookup = new RynthChatHookup(host, _view, _log.Add);
        const uint Pet = 0x80000020u;
        _view.Objects[Pet] = new RynthObjectInfo
        {
            ContainerId = 0, WielderId = 0, EquipLocation = 0, ItemType = 0x10, Wcid = 1234, Bitfield = 0x8,
            IconId = 0, IconUnderlayId = 0, IconOverlayId = 0, SpellId = null, HouseOwnerId = null,
            StackSize = 0, StackSizeMax = 0, Value = 0, Burden = 0, ItemsCapacity = 0, Structure = 0,
            MaxStructure = 0, Workmanship = 0, MaterialType = null, Identified = false,
            AppraisedSpellIds = [], Ints = new Dictionary<uint, int>(), DataIds = new Dictionary<uint, uint>(),
            InstanceIds = new Dictionary<uint, uint> { [40] = _view.PlayerId },
            Bools = new Dictionary<uint, bool>(), Floats = new Dictionary<uint, double>(), Strings = new Dictionary<uint, string>(),
        };
        _view.Attackable.Add(Pet);
        _view.Positions[_view.PlayerId] = (0xA9B40021u, new System.Numerics.Vector3(84f, 7.5f, 94f));

        Assert.True(ClientObjectHooks.TryGetObjectWcid(Pet, out uint wcid));
        Assert.Equal(1234u, wcid);
        Assert.True(ClientObjectHooks.TryGetSnapshotPwdInfo(Pet, out uint bitfield, out _, out _));
        Assert.Equal(0x8u, bitfield);
        Assert.True(ClientObjectHooks.TryGetObjectInstanceIdProperty(Pet, 40, out uint owner));
        Assert.Equal(_view.PlayerId, owner);
        Assert.True(ClientObjectHooks.TryGetSnapshotAttackable(Pet, out bool attackable));
        Assert.True(attackable);
        Assert.False(ClientObjectHooks.TryGetSnapshotAttackable(0x80000099u, out _));
        Assert.True(PlayerPhysicsHooks.TryGetPlayerPoseSnapshot(out uint cell, out float x, out _, out _, out _, out _, out _, out _));
        Assert.Equal(0xA9B40021u, cell);
        Assert.Equal(84f, x);

        hookup.Dispose();
        Assert.False(ClientObjectHooks.TryGetObjectWcid(Pet, out _));
        Assert.False(PlayerPhysicsHooks.TryGetPlayerPoseSnapshot(out _, out _, out _, out _, out _, out _, out _, out _));
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
