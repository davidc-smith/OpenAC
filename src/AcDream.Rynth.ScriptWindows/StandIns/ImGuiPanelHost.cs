using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.UI.ScriptWindows;

namespace RynthCore.Engine.ImGuiBackend;

/// <summary>
/// Our stand-in for RynthCore's ImGuiPanelHost: keeps each script window's
/// open state and draws the open ones as ImGui windows, and restores and
/// records each window's placement (rectangle, open state) through Layout. Open and Close apply
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
        public bool MissingLibraryLogged;
        /// <summary>Apply the saved rectangle on the next draw (set by Open).</summary>
        public bool PlacePending;
        /// <summary>Saved as open when first registered: reopen it on a draw while the player is in the world, as the bar would.</summary>
        public bool RestoreOpen;
        /// <summary>The rectangle last seen (logical units); a player change from it is saved.</summary>
        public PanelRect? Baseline;
    }

    private const int MaxFailures = 3;

    private static readonly object _gate = new();
    private static readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a body's exception is reported.</summary>
    internal static Action<string>? Log { get; set; }

    /// <summary>Where window placement is kept between sessions; null keeps nothing.</summary>
    internal static IPanelLayout? Layout { get; set; }

    /// <summary>Reopens a window that was left open (tests replace it). Default: as the bar does.</summary>
    internal static Action<string> RestoreOpener { get; set; } = OpenLikeTheBar;

    /// <summary>
    /// Opens a script window the way RynthCore's bar does, so the plugin gets a
    /// Visibility event and its own earlier "hidden" flag is stale.
    /// </summary>
    private static void OpenLikeTheBar(string key)
    {
        if (key.StartsWith(ScriptWindowRegistry.PanelKeyPrefix, StringComparison.OrdinalIgnoreCase))
            ScriptWindowRegistry.PlayerSetVisible(key.Substring(ScriptWindowRegistry.PanelKeyPrefix.Length), true);
        else
            Open(key);
    }

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
            {
                _entries[key] = entry = new Entry();
                entry.RestoreOpen = Layout?.Get(key) is { Open: true };
            }
            entry.Title = displayTitle;
            entry.Spec = spec;
            entry.Factory = factory;
        }
    }

    /// <summary>
    /// RynthCore's grip reserve in a body's bottom-right corner. Our windows keep
    /// ImGui's own resize grip, so nothing is reserved.
    /// </summary>
    public static float BodyGripReserve => 0f;

    /// <summary>Client pixels per logical unit; the overlay's UI scale.</summary>
    internal static float PlacementScale()
    {
        float s = EngineFrameController.FontScale;
        return s > 0f ? s : 1f;
    }

    private static Vector2 _pendingDragDelta;

    /// <summary>
    /// Makes the last item a drag handle for its panel window (chromeless
    /// panels drag from their own title row). DrawAll moves the window after
    /// the face has drawn.
    /// </summary>
    public static void DragWindowWithLastItem()
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
            _pendingDragDelta += ImGui.GetIO().MouseDelta;
    }

    /// <summary>RynthCore's pop-out button; OpenAC has no pop-out windows (PanelRouter.CanPopOut is false).</summary>
    public static string PopOutGlyph => RynthCore.Engine.ImGuiBackend.PhosphorIcons.ArrowSquareOut;

    public static string PopOutTooltip => "Pop out (not available in OpenAC)";

    public static void TogglePopOut(string title)
    {
    }

    /// <summary>RynthCore's per-panel text size rows; the overlay has one font size, so this says so.</summary>
    public static void TextSizeRows(string title, float s, string heading = "Text size")
    {
        ImGui.TextUnformatted(heading);
        ImGui.TextDisabled("Uses OpenAC's UI scale.");
    }

    /// <summary>RynthCore's built-in panels (Chat, ChatFilters) register by title.</summary>
    public static void Register(string title, PanelSpec spec, Func<IImGuiPanel> factory) =>
        RegisterDynamic(title, title, spec, factory);

    public static bool HasFace(string title)
    {
        lock (_gate) return _entries.ContainsKey(title);
    }

    /// <summary>Opens a closed panel, closes an open one (RynthCore's PanelRouter.Toggle).</summary>
    public static void Toggle(string title)
    {
        if (IsOpen(title))
            Close(title);
        else
            Open(title);
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
            entry.PlacePending = true;
            entry.Baseline = null;
            entry.RestoreOpen = false;
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

        Layout?.SetOpen(key, true);
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

        Layout?.SetOpen(key, false);
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

    /// <summary>Forgets every window and the layout hookups (tests, and host shutdown).</summary>
    internal static void Reset()
    {
        lock (_gate) _entries.Clear();
        _pendingDragDelta = Vector2.Zero;
        Layout = null;
        RestoreOpener = OpenLikeTheBar;
    }

    /// <summary>Draws every open window. Call between ImGui.NewFrame and ImGui.Render.</summary>
    internal static void DrawAll()
    {
        List<string> restore = new();
        // Marks wait while out of the world: Oracle resubmits its window at
        // character select and reopens it itself at login.
        bool inWorld = RynthCore.Engine.Compatibility.LoginLifecycleHooks.HasObservedLoginComplete;
        lock (_gate)
        {
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (!inWorld || !pair.Value.RestoreOpen)
                    continue;
                pair.Value.RestoreOpen = false;
                if (!pair.Value.Open)
                    restore.Add(pair.Key);
            }
        }

        // Outside the lock: the registry opens the window through Open.
        foreach (string key in restore)
            Guard(key + " restore", () => RestoreOpener(key));

        List<(string Key, Entry Entry, Vector2? Size, Vector2? Pos, PanelRect? Saved)> open = new();
        lock (_gate)
        {
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                Entry e = pair.Value;
                if (!e.Open)
                    continue;
                PanelRect? saved = e.PlacePending ? Layout?.Get(pair.Key)?.Rect : null;
                open.Add((pair.Key, e, e.RequestedSize, e.RequestedPos, saved));
                e.RequestedSize = null;
                e.RequestedPos = null;
                e.PlacePending = false;
            }
        }

        foreach ((string key, Entry entry, Vector2? requestedSize, Vector2? requestedPos, PanelRect? saved) in open)
        {
            float s = EngineFrameController.FontScale;
            // A frame that places the window (saved spot, plugin request) only takes a new baseline.
            bool placed = false;
            if (requestedSize is { } size)
            {
                ImGui.SetNextWindowSize(size * s, ImGuiCond.Always);
                placed = true;
            }
            else if (saved is { } savedSize)
            {
                ImGui.SetNextWindowSize(savedSize.Size * s, ImGuiCond.Always);
                placed = true;
            }
            else
            {
                ImGui.SetNextWindowSize(entry.Spec.DefaultSize * s, ImGuiCond.FirstUseEver);
            }

            if (requestedPos is { } pos)
            {
                ImGui.SetNextWindowPos(pos * s, ImGuiCond.Always);
                placed = true;
            }
            else if (saved is { } savedPos)
            {
                ImGui.SetNextWindowPos(ClampToDisplay(savedPos.Pos * s, savedPos.Size * s), ImGuiCond.Always);
                placed = true;
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
                    {
                        _pendingDragDelta = Vector2.Zero;
                        entry.Instance?.Draw();
                    }
                    if (_pendingDragDelta != Vector2.Zero)
                    {
                        ImGui.SetWindowPos(ImGui.GetWindowPos() + _pendingDragDelta);
                        _pendingDragDelta = Vector2.Zero;
                    }
                    TrackPlacement(key, entry, placed, s);
                }
            }
            catch (DllNotFoundException error)
            {
                // RynthCore's chat calls the Win32 clipboard directly; off Windows that
                // library is missing. The window keeps working without it.
                if (!entry.MissingLibraryLogged)
                {
                    entry.MissingLibraryLogged = true;
                    Log?.Invoke($"[ScriptWindows] {entry.Title}: {error.GetType().Name}: {error.Message} (not available on this system)");
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

    /// <summary>
    /// Saves the window's rectangle when the player moved or resized it: it
    /// differs from the last one seen and the left mouse button is up, so once
    /// per finished drag. A frame that placed the window only takes the new
    /// rectangle as its baseline. Inside the window, after its body.
    /// </summary>
    private static void TrackPlacement(string key, Entry entry, bool placed, float s)
    {
        var now = new PanelRect(ImGui.GetWindowPos() / s, ImGui.GetWindowSize() / s);
        if (placed || entry.Baseline is null)
        {
            entry.Baseline = now;
            return;
        }

        if (now == entry.Baseline || ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;
        entry.Baseline = now;
        Layout?.SetRect(key, now);
    }

    /// <summary>Keeps a window of this size wholly on the display (when the display is known).</summary>
    private static Vector2 ClampToDisplay(Vector2 pos, Vector2 size)
    {
        Vector2 display = ImGui.GetIO().DisplaySize;
        if (display.X <= 1 || display.Y <= 1)
            return pos;
        return Vector2.Clamp(pos, Vector2.Zero, Vector2.Max(Vector2.Zero, display - size));
    }
}
