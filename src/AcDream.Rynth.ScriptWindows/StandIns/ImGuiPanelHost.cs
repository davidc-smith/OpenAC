using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace RynthCore.Engine.ImGuiBackend;

/// <summary>
/// Our stand-in for RynthCore's ImGuiPanelHost: keeps each script window's
/// open state and draws the open ones as ImGui windows. Open and Close apply
/// at once (RynthCore queues them to the next frame), so nothing is ever
/// "transitioning". Every Begin has its End, even when a body throws.
/// </summary>
internal static class ImGuiPanelHost
{
    private sealed class Entry
    {
        public string Title = "";
        public PanelSpec Spec = null!;
        public Func<IImGuiPanel> Factory = null!;
        public IImGuiPanel? Instance;
        public bool Open;
        public Vector2? RequestedSize;
        public Vector2? RequestedPos;
        public int Failures;
    }

    private const int MaxFailures = 3;

    private static readonly object _gate = new();
    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a body's exception is reported.</summary>
    internal static Action<string>? Log { get; set; }

    internal static int OpenCount
    {
        get { lock (_gate) return _entries.Values.Count(e => e.Open); }
    }

    private static void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception error)
        {
            Log?.Invoke($"[ScriptWindows] {what}: {error.GetType().Name}: {error.Message}");
        }
    }

    public static void RegisterDynamic(string key, string displayTitle, PanelSpec spec, Func<IImGuiPanel> factory)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry))
                _entries[key] = entry = new Entry();
            entry.Title = displayTitle;
            entry.Spec = spec;
            entry.Factory = factory;
        }
    }

    public static void SetDisplayTitle(string key, string displayTitle)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
                entry.Title = displayTitle;
        }
    }

    public static void Unregister(string key)
    {
        Entry? removed;
        lock (_gate)
        {
            if (!_entries.Remove(key, out removed))
                return;
        }

        if (removed.Open)
        {
            IImGuiPanel? instance = removed.Instance;
            if (instance is not null)
                Guard(removed.Title + " OnHidden", instance.OnHidden);
        }
    }

    public static bool IsOpen(string key)
    {
        lock (_gate) return _entries.TryGetValue(key, out Entry? e) && e.Open;
    }

    public static void Open(string key)
    {
        IImGuiPanel? shown = null;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry) || entry.Open)
                return;
            entry.Open = true;
            entry.Failures = 0;
            if (entry.Instance is null)
            {
                try { entry.Instance = entry.Factory(); }
                catch (Exception error)
                {
                    entry.Open = false;
                    Log?.Invoke($"[ScriptWindows] {entry.Title} factory: {error.GetType().Name}: {error.Message}");
                    return;
                }
            }

            shown = entry.Instance;
        }

        Guard(key + " OnShown", shown.OnShown);
    }

    public static void Close(string key)
    {
        IImGuiPanel? hidden = null;
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out Entry? entry) || !entry.Open)
                return;
            entry.Open = false;
            hidden = entry.Instance;
        }

        if (hidden is not null)
            Guard(key + " OnHidden", hidden.OnHidden);
    }

    public static void RequestSize(string key, Vector2 logicalSize)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
                entry.RequestedSize = logicalSize;
        }
    }

    public static void RequestPos(string key, Vector2 logicalPos)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
                entry.RequestedPos = logicalPos;
        }
    }

    public static bool IsTransitioning(string key) => false;

    /// <summary>Forgets every window (tests, and host shutdown).</summary>
    internal static void Reset()
    {
        lock (_gate) _entries.Clear();
    }

    /// <summary>Draws every open window. Call between ImGui.NewFrame and ImGui.Render.</summary>
    internal static void DrawAll()
    {
        List<(string Key, Entry Entry, Vector2? Size, Vector2? Pos)> open = new();
        lock (_gate)
        {
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (!pair.Value.Open)
                    continue;
                open.Add((pair.Key, pair.Value, pair.Value.RequestedSize, pair.Value.RequestedPos));
                pair.Value.RequestedSize = null;
                pair.Value.RequestedPos = null;
            }
        }

        foreach ((string key, Entry entry, Vector2? requestedSize, Vector2? requestedPos) in open)
        {
            float s = EngineFrameController.FontScale;
            if (requestedSize is { } size)
            {
                ImGui.SetNextWindowSize(size * s, ImGuiCond.Always);
            }
            else
            {
                ImGui.SetNextWindowSize(entry.Spec.DefaultSize * s, ImGuiCond.FirstUseEver);
            }

            if (requestedPos is { } pos)
            {
                ImGui.SetNextWindowPos(pos * s, ImGuiCond.Always);
            }
            else if (entry.Spec.DefaultPos is { } defaultPos)
            {
                ImGui.SetNextWindowPos(defaultPos * s, ImGuiCond.FirstUseEver);
            }

            ImGui.SetNextWindowSizeConstraints(entry.Spec.MinSize * s, new Vector2(float.MaxValue, float.MaxValue));

            ImGuiWindowFlags flags = entry.Spec.BodyFlags;
            if (entry.Spec.Chrome == PanelChrome.None)
                flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize;
            if (entry.Spec.Background == 0u)
                flags |= ImGuiWindowFlags.NoBackground;

            string label = entry.Title + "###" + key;
            bool keepOpen = true;
            bool visible = entry.Spec.Chrome == PanelChrome.None
                ? ImGui.Begin(label, flags)
                : ImGui.Begin(label, ref keepOpen, flags);
            try
            {
                if (visible)
                {
                    if (entry.Failures >= MaxFailures)
                        ImGui.TextDisabled("This window failed and was stopped.");
                    else
                        entry.Instance?.Draw();
                }
            }
            catch (Exception error)
            {
                entry.Failures++;
                string tail = entry.Failures >= MaxFailures ? " (stopped after repeated failures)" : "";
                Log?.Invoke($"[ScriptWindows] {entry.Title}: {error.GetType().Name}: {error.Message}{tail}");
            }
            finally
            {
                ImGui.End();
            }

            if (!keepOpen)
                Close(key);
        }
    }
}
