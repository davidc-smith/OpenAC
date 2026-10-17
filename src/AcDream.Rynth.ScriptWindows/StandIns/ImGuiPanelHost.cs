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
    }

    private static readonly object _gate = new();
    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a body's exception is reported.</summary>
    internal static Action<string>? Log { get; set; }

    internal static int OpenCount
    {
        get { lock (_gate) return _entries.Values.Count(e => e.Open); }
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
            removed.Instance?.OnHidden();
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
            shown = entry.Instance ??= entry.Factory();
        }

        shown.OnShown();
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

        hidden?.OnHidden();
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
        KeyValuePair<string, Entry>[] open;
        lock (_gate) open = _entries.Where(pair => pair.Value.Open).ToArray();

        foreach ((string key, Entry entry) in open)
        {
            float s = EngineFrameController.FontScale;
            if (entry.RequestedSize is { } size)
            {
                ImGui.SetNextWindowSize(size * s, ImGuiCond.Always);
                entry.RequestedSize = null;
            }
            else
            {
                ImGui.SetNextWindowSize(entry.Spec.DefaultSize * s, ImGuiCond.FirstUseEver);
            }

            if (entry.RequestedPos is { } pos)
            {
                ImGui.SetNextWindowPos(pos * s, ImGuiCond.Always);
                entry.RequestedPos = null;
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
                    entry.Instance?.Draw();
            }
            catch (Exception error)
            {
                Log?.Invoke($"[ScriptWindows] {entry.Title}: {error.GetType().Name}: {error.Message}");
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
