using System.Numerics;
using ImGuiNET;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.UI.ScriptWindows;

namespace AcDream.Rynth.Host;

/// <summary>
/// The launcher bar: a small draggable strip over the game with a button per
/// script window that asked for one (BarButton) and a "Scripts" menu for the
/// other ShowInBar windows (RynthOracle's). Buttons show RynthCore's Phosphor
/// icon for a known title, else its first letter. A click opens or closes the window
/// the way RynthCore's bar does, so the plugin hears of it. Drawn only while
/// something is on it; its position is kept in the layout store. Game thread.
/// </summary>
internal sealed class RynthBar
{
    internal static readonly Vector2 DefaultPos = new(12, 40);
    private const string WindowName = "##RynthBar";
    private const string MenuName = "##RynthBarScripts";
    private const int MaxFailures = 3;
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize
        | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoNav;

    private readonly RynthLayoutStore _layout;
    private readonly Action<string> _log;
    private Vector2? _placedOn;
    private Vector2? _baseline;
    private int _failures;

    public RynthBar(RynthLayoutStore layout, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(log);
        _layout = layout;
        _log = log;
    }

    /// <summary>Runs inside the bar before its contents (tests make the body fail with it).</summary>
    internal Action? ContentsHook { get; init; }

    /// <summary>Test seam: replaces the registry's bar buttons.</summary>
    internal Func<ScriptBarItem[]>? ButtonSource { get; init; }

    /// <summary>Test seam: replaces <see cref="Toggle"/> for a click, so a test can make it throw.</summary>
    internal Action<ScriptBarItem>? ToggleAction { get; init; }

    /// <summary>RynthCore panels with a bar button (the chat window), by panel title. Only registered ones are drawn.</summary>
    internal Func<IReadOnlyList<string>>? PanelButtons { get; init; }

    private IEnumerable<string> RegisteredPanels =>
        (PanelButtons?.Invoke() ?? []).Where(ImGuiPanelHost.HasFace);

    /// <summary>True when anything is on the bar: a panel button or a script window.</summary>
    internal bool HasAnyContent => RegisteredPanels.Any() || HasContent;

    /// <summary>Opens or closes a panel, as a click on its button does.</summary>
    internal void ClickPanel(string title) => ImGuiPanelHost.Toggle(title);

    /// <summary>True when any window is on the bar.</summary>
    internal static bool HasContent =>
        ScriptWindowRegistry.BarButtons.Length > 0 || ScriptWindowRegistry.BarItems.Length > 0;

    /// <summary>Opens a closed window or closes an open one, as the player asked. Returns whether it is now open.</summary>
    internal static bool Toggle(ScriptBarItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        bool open = ImGuiPanelHost.IsOpen(item.PanelKey);
        ScriptWindowRegistry.PlayerSetVisible(item.Window, !open);
        return !open;
    }

    /// <summary>Draws the bar. Call between ImGui.NewFrame and ImGui.Render, after ImGuiPanelHost.DrawAll.</summary>
    public void Draw()
    {
        if (_failures >= MaxFailures || !HasAnyContent)
            return;
        // Placed when first drawn and on every new display size (logging out to the
        // character list, maximising the client): where the player left it at that size.
        Vector2 display = ImGui.GetIO().DisplaySize;
        if (_placedOn != display)
        {
            Vector2 logical = display / EngineFrameController.FontScale;
            ImGui.SetNextWindowPos(ClampToDisplay((_layout.BarPositionFor(logical) ?? DefaultPos) * EngineFrameController.FontScale), ImGuiCond.Always);
            _placedOn = display;
            _baseline = null;   // whatever the new size does to the bar is not the player's doing
        }

        ImGui.Begin(WindowName, Flags);
        try
        {
            ContentsHook?.Invoke();
            DrawContents();
            Track();
        }
        catch (Exception error)
        {
            _failures++;
            string tail = _failures >= MaxFailures ? " (stopped after repeated failures)" : "";
            _log($"[Rynth] bar: {error.GetType().Name}: {error.Message}{tail}");
        }
        finally
        {
            ImGui.End();
        }
    }

    private void DrawContents()
    {
        ImGui.Button(RynthBarIcons.InFont(PhosphorIcons.DotsSixVertical) ? PhosphorIcons.DotsSixVertical : "::");
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, 0f))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        Tooltip("Rynth: drag to move");

        foreach (string title in RegisteredPanels)
        {
            ImGui.SameLine();
            bool open = ImGuiPanelHost.IsOpen(title);
            if (open)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            bool clicked = ImGui.Button(RynthBarIcons.Label(title) + "##panel" + title);
            if (open)
                ImGui.PopStyleColor();
            if (clicked)
                ClickPanel(title);
            Tooltip(title);
        }

        foreach (ScriptBarItem item in ButtonSource?.Invoke() ?? ScriptWindowRegistry.BarButtons)
        {
            ImGui.SameLine();
            bool open = ImGuiPanelHost.IsOpen(item.PanelKey);
            if (open)
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            bool clicked = ImGui.Button(RynthBarIcons.Label(item.Window.Title) + "##" + item.MenuLabel);
            if (open)
                ImGui.PopStyleColor();
            // Only after the pop: the toggle calls into the plugin and may throw.
            if (clicked)
                Click(item);
            Tooltip(item.Window.Title);
        }

        ScriptBarItem[] menu = ScriptWindowRegistry.BarItems;
        if (menu.Length == 0)
            return;
        ImGui.SameLine();
        if (ImGui.Button("Scripts"))
            ImGui.OpenPopup(MenuName);
        if (!ImGui.BeginPopup(MenuName))
            return;
        try
        {
            foreach (ScriptBarItem item in menu)
            {
                if (item.GroupStart)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                    ImGui.TextUnformatted(item.Group);
                    ImGui.PopStyleColor();
                }

                string? icon = RynthBarIcons.Icon(item.Window.Title);
                string label = icon is not null && RynthBarIcons.InFont(icon) ? icon + " " + item.MenuLabel : item.MenuLabel;
                if (ImGui.MenuItem(label, string.Empty, ImGuiPanelHost.IsOpen(item.PanelKey)))
                    Click(item);
            }
        }
        finally
        {
            ImGui.EndPopup();
        }
    }

    private void Click(ScriptBarItem item)
    {
        if (ToggleAction is { } action)
            action(item);
        else
            Toggle(item);
    }

    /// <summary>Saves the bar's position once the player has moved it and let go.</summary>
    private void Track()
    {
        Vector2 pos = ImGui.GetWindowPos();
        if (_baseline is null)
        {
            _baseline = pos;
            return;
        }

        if (pos == _baseline || ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;
        _baseline = pos;
        _layout.SetBarPosition(pos / EngineFrameController.FontScale, ImGui.GetIO().DisplaySize / EngineFrameController.FontScale);
    }

    /// <summary>A hover tooltip for the last item; the text is never a format string.</summary>
    private static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered())
            return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }

    private static Vector2 ClampToDisplay(Vector2 pos)
    {
        Vector2 display = ImGui.GetIO().DisplaySize;
        if (display.X <= 1 || display.Y <= 1)
            return pos;
        return Vector2.Clamp(pos, Vector2.Zero, Vector2.Max(Vector2.Zero, display - new Vector2(40, 24)));
    }
}
