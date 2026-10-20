using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RynthCore.Engine.Plugins;
using RynthCore.Engine.UI.Data;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// RynthCore's UiDataHub step, shared by everything that shows their engine panels (the
/// RynthAi dashboard, the RynthNav panel): each tick runs the parts' per-frame steps, the
/// posted work, then polls every subscribed source that is due. Parts join when they start
/// and leave when they stop; the last to leave clears the queue and tells the sources the
/// plugins are gone. Game thread.
/// </summary>
internal static class RynthEngineHub
{
    /// <summary>One panel set's sources and the per-frame steps it runs around the posted work.</summary>
    internal sealed record Part(string Name, IReadOnlyList<UiSource> Sources, Action? BeforeDrain = null, Action? AfterDrain = null);

    private static readonly List<Part> Parts = new();

    /// <summary>Where posted work or a source that throws is logged; the host sets it.</summary>
    internal static Action<string> Log { get; set; } = _ => { };

    internal static bool HasParts => Parts.Count > 0;

    internal static void Join(Part part)
    {
        if (!Parts.Any(p => p.Name == part.Name))
            Parts.Add(part);
    }

    /// <summary>One hub step. Returns how many posted actions ran.</summary>
    internal static int Tick()
    {
        if (Parts.Count == 0)
            return 0;
        UiSource[] sources = AllSources();
        foreach (UiSource source in sources)
            source.PromoteDeferredRefresh();
        foreach (Part part in Parts.ToArray())
            part.BeforeDrain?.Invoke();
        int ran = UiDataHub.Drain(Log);
        foreach (Part part in Parts.ToArray())
            part.AfterDrain?.Invoke();
        PollAll(sources, Stopwatch.GetTimestamp());
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
            catch (Exception ex) { Log($"[Rynth] {source.Name} poll: {ex.GetType().Name}: {ex.Message}"); }
            long after = Stopwatch.GetTimestamp();
            source.MarkPolled(after, after - before);
        }
    }

    /// <summary>Resets the part's sources; the last part out clears the queue and unloads.</summary>
    internal static void Leave(string name)
    {
        Part? part = Parts.FirstOrDefault(p => p.Name == name);
        if (part is not null)
        {
            Parts.Remove(part);
            foreach (UiSource source in part.Sources)
            {
                try { source.Reset(); }
                catch (Exception ex) { Log($"[Rynth] {source.Name} reset: {ex.GetType().Name}: {ex.Message}"); }
            }
        }

        if (Parts.Count == 0)
        {
            UiDataHub.Clear();
            PluginManager.RaisePluginsUnloaded();
        }
    }

    /// <summary>Test reset: every part leaves.</summary>
    internal static void LeaveAll()
    {
        foreach (Part part in Parts.ToArray())
            Leave(part.Name);
        Leave(string.Empty);
    }

    private static UiSource[] AllSources() => Parts.SelectMany(p => p.Sources).Distinct().ToArray();
}
