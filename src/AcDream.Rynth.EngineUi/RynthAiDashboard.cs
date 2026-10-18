using System;
using System.Diagnostics;
using RynthCore.Engine;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// Runs RynthCore's RynthAi dashboard: Start does what RynthCore's EntryPoint
/// does when RynthAi is installed (register the face), Tick is UiDataHub's
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
    }

    /// <summary>One hub step. Returns how many posted actions ran.</summary>
    internal static int Tick()
    {
        if (!_started)
            return 0;
        int ran = UiDataHub.Drain(Log);
        Poll(UiSources.RynthAi);
        Poll(UiSources.Patrol);
        Poll(UiSources.Nav);
        Poll(UiSources.Settings);
        Poll(UiSources.RynthSortWatch);
        return ran;
    }

    private static void Poll(UiSource source)
    {
        source.PromoteDeferredRefresh();
        long now = Stopwatch.GetTimestamp();
        if (!source.HasSubscribers || !source.IsDue(now))
            return;
        source.Poll();
        source.MarkPolled(now, Stopwatch.GetTimestamp() - now);
    }

    internal static void Stop()
    {
        UiDataHub.Clear();
        PluginManager.RaisePluginsUnloaded();
        UiSources.RynthAi.Reset();
        UiSources.Patrol.Reset();
        UiSources.Nav.Reset();
        UiSources.Settings.Reset();
        UiSources.RynthSortWatch.Reset();
        _started = false;
    }
}
