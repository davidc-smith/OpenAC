using AcDream.App.UI.Layout;

namespace AcDream.App.UI;

/// <summary>
/// A markup <c>&lt;canvas id="..."&gt;</c>: an empty rectangle the window's
/// layout places and sizes, which hosts the plugin canvas registered under
/// the same id with <c>InMarkup</c>. It draws nothing itself and lets clicks
/// through; the hosted canvas element fills it and answers for itself. It
/// clips, so a canvas whose host has no room shows nothing.
/// </summary>
internal sealed class UiMarkupCanvas : UiElement
{
    internal UiMarkupCanvas(string canvasId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canvasId);
        CanvasId = canvasId;
        ClickThrough = true;
    }

    /// <summary>The plugin's canvas id this element hosts.</summary>
    internal string CanvasId { get; }

    /// <summary>The canvas element shown here, or null while no canvas is hosted.</summary>
    internal PluginCanvasElement? Hosted { get; private set; }

    protected override bool ClipsChildren => true;

    /// <summary>Shows <paramref name="canvas"/> here, sized to this element.</summary>
    internal void Host(PluginCanvasElement canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (Hosted is not null)
            throw new InvalidOperationException($"<canvas id=\"{CanvasId}\"> already hosts a canvas");
        Hosted = canvas;
        AddChild(canvas);
        canvas.FillHost(Width, Height);
    }

    /// <summary>
    /// Takes the hosted element out of the tree and returns it, or null when
    /// none was hosted. The caller gives its targets back.
    /// </summary>
    internal PluginCanvasElement? Unhost()
    {
        PluginCanvasElement? canvas = Hosted;
        if (canvas is null) return null;
        Hosted = null;
        RemoveChild(canvas);
        return canvas;
    }

    // Children are laid out just before they are drawn, after this element's
    // own layout placed it; the canvas follows the size it was given.
    private protected override void LayoutChildren() => Hosted?.FillHost(Width, Height);
}
