using System.Numerics;

namespace RynthCore.Engine.ImGuiBackend;

/// <summary>A window's rectangle, in logical units.</summary>
internal readonly record struct PanelRect(Vector2 Pos, Vector2 Size);

/// <summary>What is kept for one window: its rectangle once the player placed it (null until then) and whether it was left open.</summary>
internal readonly record struct PanelPlacement(PanelRect? Rect, bool Open);

/// <summary>Where the panel host keeps window placement between sessions.</summary>
internal interface IPanelLayout
{
    /// <summary>The saved placement, or null when nothing is saved for the key.</summary>
    PanelPlacement? Get(string key);

    /// <summary>Records a rectangle the player gave the window (keeps its open flag).</summary>
    void SetRect(string key, PanelRect rect);

    /// <summary>Records whether the window is open (keeps its rectangle).</summary>
    void SetOpen(string key, bool open);
}
