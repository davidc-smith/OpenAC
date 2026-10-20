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
        @"UI\Data\MetaRuleDto.cs", @"UI\Data\MetaStateClock.cs", @"ImGui\Panels\LootEditorFace.cs",
        @"ImGui\Panels\LootAddDialog.cs", @"UI\Data\LootEditData.cs", @"ImGui\Panels\RangesSlideOut.cs", @"ImGui\Panels\ItemCountFace.cs",
        @"UI\Data\ItemHudModel.cs", @"UI\Data\ItemHudStore.cs", @"ImGui\Panels\LuaFace.cs", @"UI\Data\LuaData.cs", @"UI\Data\RynthAiData.cs", @"UI\Data\SettingsData.cs",
        @"UI\Data\NavData.cs", @"UI\Data\RynthSortData.cs", @"UI\RynthAiDashboardState.cs",
        @"UI\PanelTextScale.cs", @"UI\QuickSettings.cs", @"UI\BarLayout.cs",
        @"ImGui\Panels\SkillsFace.cs", @"ImGui\Panels\SkillsFace.Progression.cs", @"ImGui\Panels\SkillsData.cs",
        @"UI\Data\ProgressionData.cs", @"Compatibility\TrainingCosts.cs", @"Compatibility\PlayerProgressHooks.cs",
        @"ImGui\MonsterHud\PortalSpellTable.cs", @"UI\ScriptWindows\AcIconDecoder.cs",
        @"ImGui\Panels\DungeonMapFace.cs", @"UI\Data\RadarData.cs", @"UI\Data\RadarSnapshotJson.cs", @"UI\Data\DungeonMapMath.cs",
        @"ImGui\Panels\RadarMarkerStyle.cs", @"ImGui\Panels\CircleClip.cs", @"UI\Panels\MarkerStyleSettings.cs",
        @"ImGui\Panels\MarkerStyleEditor.cs", @"UI\Panels\RadarSettingsStore.cs", @"UI\Data\RynthNavAtlasData.cs", @"UI\Data\RynthNavData.cs",
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
    public void TheHookupReadsProgressStatModsTimeAndSendsRaises()
    {
        _view.Progress = new RynthPlayerProgress(
            [new RynthSkillProgress(6, 3, 10, 5000, 10, true, 140, 120)],
            [new RynthStatProgress(1, 20, 100, 900, 130, 120)],
            [new RynthStatProgress(1, 4, 0, 300, 80, 70)],
            30, 7, 1_000_000, 2500, 40, 500, 0.95f);
        _view.StatMods = [new RynthStatMod(0x1234, 99.0, 0x8010, 6, 35f, 77), new RynthStatMod(0x1300, double.MaxValue, 0x8001, 1, 10f, 3)];
        _view.Time = 12.5;
        using var host = new RynthHost(_view, _log.Add, []);
        using (var hookup = new RynthChatHookup(host, _view, _log.Add))
        {
            var p = new PlayerProgress();
            Assert.True(ClientObjectHooks.ReadPlayerProgressLive(p));
            Assert.True(p.SkillTableRead && p.SkillInTable[6] && p.SkillLevelKnown[6] && p.AttributeCacheRead);
            Assert.Equal((3u, 5000u, 10u, 10u, 140, 120), (p.SkillClass[6], p.SkillXpSpent[6], p.SkillRanks[6], p.SkillInit[6], p.SkillBuffed[6], p.SkillBase[6]));
            Assert.Equal((20u, 100u, 900u, 130u, 120u), (p.AttrRanks[1], p.AttrInit[1], p.AttrXpSpent[1], p.AttrBuffed[1], p.AttrBase[1]));
            Assert.Equal((4u, 0u, 300u, 80u, 70u), (p.VitalRanks[1], p.VitalInit[1], p.VitalXpSpent[1], p.VitalBuffed[1], p.VitalBase[1]));
            Assert.Equal((30, 7, 1_000_000L, 2500L, 40L, 500L, 0.95f), (p.Level, p.SkillCredits, p.TotalXp, p.UnassignedXp, p.Luminance, p.MaxLuminance, p.Vitae));
            Assert.False(p.SkillInTable[15]);

            uint[] spell = new uint[1], type = new uint[1], key = new uint[1], category = new uint[1];
            double[] expiry = new double[1];
            float[] value = new float[1];
            Assert.Equal(1, EnchantmentHooks.CopyPlayerStatMods(spell, expiry, type, key, value, category));   // capped at the arrays
            Assert.Equal((0x1234u, 99.0, 0x8010u, 6u, 35f, 77u), (spell[0], expiry[0], type[0], key[0], value[0], category[0]));

            Assert.Equal(12.5, TimeSyncHooks.GetCurrentServerTime());
            Assert.True(PlayerTraining.Raise(PlayerTraining.TrainKind.Skill, 6, 100));
            Assert.Equal([(3, 6u, 100u)], _view.Advancements);

            _view.Progress = null;
            Assert.False(ClientObjectHooks.ReadPlayerProgressLive(p));
        }

        Assert.Null(ClientObjectHooks.ProgressReader);
        Assert.Null(EnchantmentHooks.Copier);
        Assert.Null(TimeSyncHooks.ServerTime);
        Assert.Null(PlayerTraining.Sender);
        Assert.Empty(_log);
    }

    [Fact]
    public void TheSkillsPanelDrawsAPublishedSnapshot()
    {
        _view.Progress = new RynthPlayerProgress(
            [new RynthSkillProgress(6, 3, 10, 5000, 10, true, 140, 120)],
            [new RynthStatProgress(1, 20, 100, 900, 130, 120)],
            [new RynthStatProgress(1, 4, 0, 300, 80, 70)],
            30, 7, 1_000_000, 2500, 40, 500, 1f);
        using var host = new RynthHost(_view, _log.Add, []);
        InventoryModelReset.Run();
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        using var inventory = new RynthInventoryHookup(_view, null, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);

        try
        {
            for (int i = 0; i < 3; i++)
            {
                PlayerProgressHooks.Want();
                PlayerProgressHooks.RefreshSoon();
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
            }

            Assert.NotNull(PlayerProgressHooks.Current);
            // Their SkillDat logs one "Skills: " status line when the panel opens, but only while its
            // static tables are unloaded (another test may have loaded them); nothing else is logged.
            Assert.All(_log, line => Assert.StartsWith("[Rynth] [ScriptWindows] Skills: ", line, StringComparison.Ordinal));
        }
        finally
        {
            RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);
        }
    }

    [Fact]
    public void TheSkillsPanelRegistersWithTheDashboardAndDrawsHeadless()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        InventoryModelReset.Run();
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        using var inventory = new RynthInventoryHookup(_view, null, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);

        try
        {
            // No progress reader: the panel draws empty.
            for (int i = 0; i < 3; i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 100, $"empty frame {i}: only {vertices} vertices");
            }

            // A reader: the next tick publishes a snapshot and the panel draws it.
            ClientObjectHooks.ProgressReader = dst =>
            {
                dst.Clear();
                dst.PlayerId = _view.PlayerId;
                dst.Level = 50;
                dst.TotalXp = 10_000_000;
                dst.UnassignedXp = 250_000;
                dst.SkillCredits = 3;
                dst.SkillTableRead = true;
                dst.SkillInTable[6] = true;
                dst.SkillClass[6] = 2;
                dst.SkillLevelKnown[6] = true;
                dst.SkillBase[6] = 120;
                dst.SkillBuffed[6] = 130;
                dst.AttributeCacheRead = true;
                for (int a = 1; a <= 6; a++)
                {
                    dst.AttrInit[a] = 50;
                    dst.AttrBase[a] = dst.AttrBuffed[a] = 60;
                }
                for (int v = 1; v <= 3; v++)
                    dst.VitalBase[v] = dst.VitalBuffed[v] = 100;
                return true;
            };
            PlayerProgressHooks.RefreshSoon();
            for (int i = 0; i < 3; i++)
            {
                AcDream.Rynth.EngineUi.RynthAiDashboard.Tick();
                int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
                Assert.True(vertices > 100, $"progress frame {i}: only {vertices} vertices");
            }

            Assert.Equal(50, PlayerProgressHooks.Current?.Level);
            Assert.True(ImGuiPanelHost.IsOpen(SkillsFace.Title));
            // Their SkillDat logs one status line when the panel opens ("client_portal.dat not
            // found" here, with no dat folder set; the tables' sizes when it loads); nothing else.
            Assert.All(_log, line => Assert.StartsWith("[Rynth] [ScriptWindows] Skills: ", line, StringComparison.Ordinal));
        }
        finally
        {
            ClientObjectHooks.ProgressReader = null;
            RynthCore.Engine.UI.PanelRouter.Toggle(SkillsFace.Title);
        }
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

    [Fact]
    public void TheHookupSendsMiniRemoteUsesThroughTheView()
    {
        const uint Player = 0x50000001u, Kit = 0x80000020u;
        _view.Objects[Kit] = Carried(Player, 0, 5);
        using var host = new RynthHost(_view, _log.Add, []);
        InventoryModelReset.Run();
        using (var hookup = new RynthChatHookup(host, _view, _log.Add))
        using (var inventory = new RynthInventoryHookup(_view, null, _log.Add))
        {
            Assert.Null(AccountHooks.AnnouncedWorldName);
            _view.WorldName = "Coldeve";
            Assert.Equal("Coldeve", AccountHooks.AnnouncedWorldName);
            _view.WorldName = "";
            Assert.Null(AccountHooks.AnnouncedWorldName);
            _view.WorldName = "Coldeve";
            Assert.True(InventoryModel.Request(new InventoryRequest { Kind = InventoryActionKind.Use, ItemId = Kit, ItemName = "Kit" }, out _));
            InventoryModel.MainThreadTick();
            Assert.Contains($"use {Kit:X8}", _view.Actions);
        }
        // The hooks the old stand-in's UseSender / Busy / Replies / ReadItem / PlayerId were.
        Assert.Null(ClientHelperHooks.Use);
        Assert.Null(ClientActionGates.Pending);
        Assert.Null(SmartBoxHooks.Replies);
        Assert.Null(ClientObjectHooks.ItemFields);
        Assert.Null(ClientHelperHooks.PlayerId);
        Assert.Null(AccountHooks.WorldName);
        InventoryModelReset.Run();
    }

    private static RynthObjectInfo Carried(uint container, uint wielder, int stack, int capacity = 0) => new()
    {
        ContainerId = container, WielderId = wielder, EquipLocation = wielder != 0 ? 0x100u : 0u, ItemType = 0x80, Wcid = 300,
        Bitfield = 0, IconId = 0x06001111, IconUnderlayId = 0, IconOverlayId = 0, SpellId = null, HouseOwnerId = null,
        StackSize = stack, StackSizeMax = 100, Value = 5, Burden = 1, ItemsCapacity = capacity, Structure = 0,
        MaxStructure = 0, Workmanship = 0, MaterialType = null, Identified = false,
        AppraisedSpellIds = [], Ints = new Dictionary<uint, int>(), DataIds = new Dictionary<uint, uint>(),
        InstanceIds = new Dictionary<uint, uint>(),
        Bools = new Dictionary<uint, bool>(), Floats = new Dictionary<uint, double>(), Strings = new Dictionary<uint, string>(),
    };

    [Fact]
    public void TheInventoryModelIsTheCarriedPacksAndWornItems()
    {
        const uint Player = 0x50000001u, Pack = 0x80000010u, Arrows = 0x80000011u, MoreArrows = 0x80000012u, Bow = 0x80000013u;
        _view.Objects[Pack] = Carried(Player, 0, 1, capacity: 24);
        _view.Objects[Arrows] = Carried(Player, 0, 40);
        _view.Objects[MoreArrows] = Carried(Pack, 0, 60);
        _view.Objects[Bow] = Carried(0, Player, 1);
        _view.Contents[Player] = [Pack, Arrows, Bow];
        _view.Contents[Pack] = [MoreArrows];
        _view.Names[Arrows] = _view.Names[MoreArrows] = "Arrow";
        _view.SelectedObjectId = MoreArrows;
        using var host = new RynthHost(_view, _log.Add, []);
        InventoryModelReset.Run();
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        using var inventory = new RynthInventoryHookup(_view, null, _log.Add);
        InventoryModel.Subscribe();
        try
        {
            InventoryModel.MainThreadTick();
            InventoryView view = InventoryModel.Current;

            Assert.Equal(Player, view.PlayerId);
            Assert.Equal([Player, Pack], view.Packs.Select(p => p.Id));
            Assert.True(view.Packs[0].IsMain);
            // Their model lists side packs only as packs, not as items of the main pack.
            Assert.Equal([Arrows], view.Packs[0].Items.Select(i => i.Id));
            InventoryItem inner = Assert.Single(view.Packs[1].Items);
            Assert.Equal(("Arrow", 60), (inner.Name, inner.Stack));
            Assert.Equal(Bow, Assert.Single(view.Worn).Id);
            Assert.Equal(MoreArrows, InventoryModel.SelectedItem);
            Assert.Same(view, InventoryModel.Current);
        }
        finally
        {
            InventoryModel.Unsubscribe();
            InventoryModelReset.Run();
        }
    }

    [Fact]
    public void TheItemCountPanelRegistersWithTheDashboardAndDrawsHeadless()
    {
        using var host = new RynthHost(_view, _log.Add, []);
        InventoryModelReset.Run();
        using var hookup = new RynthChatHookup(host, _view, _log.Add);
        using var inventory = new RynthInventoryHookup(_view, null, _log.Add);
        using var imgui = new HeadlessImGui();
        LoginLifecycleHooks.HasObservedLoginComplete = true;
        AcDream.Rynth.EngineUi.RynthAiDashboard.Start();
        RynthCore.Engine.UI.PanelRouter.Toggle(ItemCountFace.Title);

        for (int i = 0; i < 3; i++)
        {
            int vertices = imgui.Frame(ImGuiPanelHost.DrawAll);
            Assert.True(vertices > 100, $"frame {i}: only {vertices} vertices");
        }

        Assert.True(ImGuiPanelHost.IsOpen(ItemCountFace.Title));
        Assert.Empty(_log);
        RynthCore.Engine.UI.PanelRouter.Toggle(ItemCountFace.Title);
        InventoryModelReset.Run();
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
