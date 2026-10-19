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

    /// <summary>The client's icons as ImGui texture ids. Never null; all zero until the interface is up.</summary>
    IClientOverlayIcons Icons => NoOverlayIcons.Instance;

    /// <summary>
    /// Merges <paramref name="glyphs"/> (BMP code points) from a TrueType
    /// font into the overlay's font at <paramref name="scale"/> times its
    /// size, from the next frame on. The overlay keeps a copy of the bytes.
    /// </summary>
    void AddMergedFont(byte[] ttf, string glyphs, float scale)
    {
    }

    /// <summary>
    /// The overlay's font (with the merged fonts) at <paramref name="points"/>,
    /// as an ImFont pointer for this frame only. A size not baked yet is zero and
    /// is baked from the next frame on.
    /// </summary>
    IntPtr Font(float points) => IntPtr.Zero;

    /// <summary>Adds a keyboard sink. Dispose the result to remove it.</summary>
    IDisposable AddKeyboardSink(IOverlayKeyboardSink sink) => NoKeyboardSink.Instance;

    /// <summary>The system clipboard's text; "" when there is none or no keyboard.</summary>
    string ClipboardText
    {
        get => string.Empty;
        set { }
    }
}

/// <summary>The registration an overlay without keyboard sinks returns.</summary>
internal sealed class NoKeyboardSink : IDisposable
{
    public static readonly NoKeyboardSink Instance = new();

    public void Dispose()
    {
    }
}

/// <summary>An overlay without icons: every lookup is zero.</summary>
internal sealed class NoOverlayIcons : IClientOverlayIcons
{
    public static readonly NoOverlayIcons Instance = new();

    public IntPtr Icon(uint iconId) => IntPtr.Zero;

    public IntPtr SpellIcon(uint spellId) => IntPtr.Zero;

    public IntPtr ObjectIcon(uint objectId) => IntPtr.Zero;

    public IntPtr Upload(byte[] rgba, int width, int height, string name) => IntPtr.Zero;

    public bool Release(IntPtr texture) => false;
}
