using System;
using System.Collections.Generic;
using RynthCore.Engine;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// Runs RynthCore's RynthAi dashboard: Start does what RynthCore's EntryPoint
/// does when RynthAi is installed (register the dashboard and the panels it opens: Settings, Items, Damage, Monster Detail, Nav, Meta, Loot Editor, Item Count, Lua, Skills, Dungeon Map, Mini Remote), Tick runs the shared
/// RynthEngineHub (this dashboard is one of its parts), Stop leaves it. Game thread.
/// </summary>
internal static class RynthAiDashboard
{
    private static bool _started;

    /// <summary>Their dashboard's title (the bar button and the layout key).</summary>
    internal const string Title = RynthAiFace.Title;

    /// <summary>Where posted work that throws is logged; the host sets it (the hub's log).</summary>
    internal static Action<string> Log
    {
        get => RynthEngineHub.Log;
        set => RynthEngineHub.Log = value;
    }

    /// <summary>RynthCore's EngineFrameController: the retail pictures and World Map tiles read since the last frame become textures before the faces draw.</summary>
    internal static void BeginFrame()
    {
        if (!_started)
            return;
        RetailSprites.BeginFrame(IntPtr.Zero);
        WorldMapTiles.BeginFrame(IntPtr.Zero);
    }

    internal static void Start()
    {
        if (!_started)
        {
            EntryPoint.InitStartedUtc = DateTime.UtcNow;
            _started = true;
            // RynthCore's EntryPoint loads radar_settings.txt at startup; before the Radar registers.
            RynthCore.Engine.UI.Panels.RadarSettingsStore.Load();
            NavAtlasInstall.EnsureOnce(Log);
        }

        // The panel host can be reset (tests, an extension restart): register again then.
        if (!ImGuiPanelHost.HasFace(RynthAiFace.Title))
            RynthAiFace.Register();
        if (!ImGuiPanelHost.HasFace(SettingsFace.Title))
            SettingsFace.Register();
        if (!ImGuiPanelHost.HasFace(ItemsFace.Title))
            ItemsFace.Register();
        if (!ImGuiPanelHost.HasFace(DamageFace.Title))
            DamageFace.Register();
        if (!ImGuiPanelHost.HasFace(DamageDetailFace.Title))
            DamageDetailFace.Register();
        if (!ImGuiPanelHost.HasFace(NavFace.Title))
            NavFace.Register();
        if (!ImGuiPanelHost.HasFace(MetaFace.Title))
            MetaFace.Register();
        if (!ImGuiPanelHost.HasFace(LootEditorFace.Title))
            LootEditorFace.Register();
        if (!ImGuiPanelHost.HasFace(ItemCountFace.Title))
            ItemCountFace.Register();
        if (!ImGuiPanelHost.HasFace(InventoryFace.Title))
            InventoryFace.Register();
        if (!ImGuiPanelHost.HasFace(LuaFace.Title))
            LuaFace.Register();
        if (!ImGuiPanelHost.HasFace(SkillsFace.Title))
            SkillsFace.Register();
        if (!ImGuiPanelHost.HasFace(DungeonMapFace.Title))
            DungeonMapFace.Register();
        if (!ImGuiPanelHost.HasFace(MiniRemoteFace.Title))
            MiniRemoteFace.Register();
        // Registers the Mini Map too, and loads world_map_settings.txt.
        if (!ImGuiPanelHost.HasFace(WorldMapFace.Title))
            WorldMapFace.Register();
        // Registers Inventory again (RynthCore does it from here); RegisterDynamic replaces that entry in place.
        if (!ImGuiPanelHost.HasFace(RadarFace.Title))
            RadarFace.Register();
        RynthEngineHub.Join(HubPart);
    }

    private static readonly UiSource[] Sources =
        [
            UiSources.RynthAi, UiSources.Patrol, UiSources.Nav, UiSources.Settings, UiSources.RynthSortWatch,
            UiSources.Items, UiSources.Damage, UiSources.MonsterRules, UiSources.MonsterDetail,
            UiSources.Meta, UiSources.LootEdit, UiSources.Lua, UiSources.Progression, UiSources.Radar,
            UiSources.Characters,
        ];

    internal static IReadOnlyList<UiSource> HubSources => Sources;

    // RynthCore's ImGuiController frame and MainThreadSnapshots.Tick, in their order around the drain.
    private static readonly RynthEngineHub.Part HubPart = new("RynthAi", Sources,
        BeforeDrain: () =>
        {
            // MainThreadSnapshots.Tick: reads only while the Skills panel wants it.
            PlayerProgressHooks.Prefetch();
            // Servers, plugin manifests and the Mini Remote's sections.
            RynthCore.Engine.UI.Sections.SectionRegistry.Tick();
        },
        AfterDrain: InventoryModel.MainThreadTick);

    /// <summary>One hub step (the hub runs every part, this one included). Returns how many posted actions ran.</summary>
    internal static int Tick() => _started ? RynthEngineHub.Tick() : 0;

    internal static void Stop()
    {
        try { RynthEngineHub.Leave(HubPart.Name); }
        finally { _started = false; }
    }
}
