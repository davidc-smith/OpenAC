namespace AcDream.ClientExtensions;

/// <summary>
/// The client's Dear ImGui overlay, drawn over everything else. An extension
/// adds a callback that makes ImGui.NET calls; the client runs it once per
/// rendered frame between ImGui.NewFrame and ImGui.Render, on the game thread.
/// </summary>
public interface IClientOverlay
{
    /// <summary>Adds a per-frame draw callback. Dispose the result to remove it.</summary>
    IDisposable AddFrameCallback(Action draw);

    /// <summary>True while ImGui uses the mouse (over a window, dragging).</summary>
    bool WantsMouse { get; }

    /// <summary>True while an ImGui text field has the keyboard.</summary>
    bool WantsKeyboard { get; }
}
