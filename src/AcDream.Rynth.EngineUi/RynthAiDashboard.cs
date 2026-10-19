using System;
using System.Collections.Generic;
using System.Diagnostics;
using RynthCore.Engine;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// Runs RynthCore's RynthAi dashboard: Start does what RynthCore's EntryPoint
/// does when RynthAi is installed (register the dashboard and the panels it opens: Settings, Items, Damage, Monster Detail, Nav, Meta, Loot Editor, Item Count, Lua, Skills), Tick is UiDataHub's
/// step (run posted work, then poll each subscribed source when due), Stop is
/// the hub's reset after plugins unload. Game thread.
/// </summary>
internal static class RynthAiDashboard
{
    private static bool _started;

    /// <summary>Their dashboard's title (the bar button and the layout key).</summary>
    internal const string Title = RynthAiFace.Title;

    /// <summary>Where posted work that throws is logged; the host sets it.</summary>
    internal static Action<string> Log { get; set; } = _ => { };

    internal static void Start()
    {
        if (!_started)
        {
            EntryPoint.InitStartedUtc = DateTime.UtcNow;
            _started = true;
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
        if (!ImGuiPanelHost.HasFace(LuaFace.Title))
            LuaFace.Register();
        if (!ImGuiPanelHost.HasFace(SkillsFace.Title))
            SkillsFace.Register();
    }

    private static readonly UiSource[] Sources =
        [
            UiSources.RynthAi, UiSources.Patrol, UiSources.Nav, UiSources.Settings, UiSources.RynthSortWatch,
            UiSources.Items, UiSources.Damage, UiSources.MonsterRules, UiSources.MonsterDetail,
            UiSources.Meta, UiSources.LootEdit, UiSources.Lua, UiSources.Progression,
        ];

    /// <summary>One hub step. Returns how many posted actions ran.</summary>
    internal static int Tick()
    {
        if (!_started)
            return 0;
        foreach (UiSource source in Sources)
            source.PromoteDeferredRefresh();
        // RynthCore's MainThreadSnapshots.Tick: reads only while the Skills panel wants it.
        PlayerProgressHooks.Prefetch();
        int ran = UiDataHub.Drain(Log);
        PollAll(Sources, Stopwatch.GetTimestamp());
        return ran;
    }

    /// <summary>
    /// Polls each subscribed, due source inside its own try/catch (as the hub's
    /// Step does): one that throws is logged and still marked polled, so it
    /// neither stops the others nor is retried every tick.
    /// </summary>
    internal static void PollAll(IReadOnlyList<UiSource> sources, long now)
    {
        foreach (UiSource source in sources)
        {
            if (!source.HasSubscribers || !source.IsDue(now))
                continue;
            long before = Stopwatch.GetTimestamp();
            try { source.Poll(); }
            catch (Exception ex) { Log($"[RynthAi] {source.Name} poll: {ex.GetType().Name}: {ex.Message}"); }
            long after = Stopwatch.GetTimestamp();
            source.MarkPolled(after, after - before);
        }
    }

    internal static void Stop()
    {
        try
        {
            UiDataHub.Clear();
            PluginManager.RaisePluginsUnloaded();
            foreach (UiSource source in Sources)
            {
                try { source.Reset(); }
                catch (Exception ex) { Log($"[RynthAi] {source.Name} reset: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
        finally
        {
            _started = false;
        }
    }
}
