namespace AcDream.App.UI;

/// <summary>
/// A built markup window and what mounting it needs: the frame to add to
/// the root, the element that holds its authored content (the frame itself
/// unless the window has chrome), its title bar if it has one, the
/// revision its saved size is kept under, and what the build ignored.
/// </summary>
internal sealed class MarkupWindow(
    UiNineSlicePanel frame, UiElement contentRoot, PluginTitleBar? titleBar, int authoredGeometryRevision,
    IReadOnlyList<string>? warnings = null)
{
    private RetailWindowManager? _manager;
    private string? _name;

    // The size the window had before its content grew it, and the size it grew
    // to; cleared once the player (or a restored layout) sizes it otherwise.
    private (float Width, float Height)? _beforeGrowth;
    private (float Width, float Height) _grownTo;

    public UiNineSlicePanel Frame { get; } = frame;
    public UiElement ContentRoot { get; } = contentRoot;
    public PluginTitleBar? TitleBar { get; } = titleBar;
    public int AuthoredGeometryRevision { get; } = authoredGeometryRevision;

    /// <summary>The window's <c>&lt;canvas&gt;</c> elements in document order; each hosts the plugin canvas with its id.</summary>
    public IReadOnlyList<UiMarkupCanvas> Canvases { get; init; } = [];

    /// <summary>
    /// Attributes the build ignored, one line each: flex attributes on markup
    /// that does not use flex, which older builds ignored silently.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; } = warnings ?? [];

    /// <summary>
    /// Registers the mounted frame under <paramref name="name"/>, with the
    /// content root and the authored revision, and connects the close button
    /// to <see cref="RetailWindowManager.Close"/>: the path a dock slot takes,
    /// so the player's request is cleared and Hidden/Closed fire as usual.
    /// The frame must already be a direct child of the manager's root.
    /// </summary>
    internal RetailWindowHandle Register(
        RetailWindowManager manager, string name, IRetainedPanelController controller)
    {
        RetailWindowHandle handle = manager.Register(
            name, Frame, ContentRoot, controller, authoredGeometryRevision: AuthoredGeometryRevision);
        if (TitleBar is { } bar)
            bar.CloseRequested += () => manager.Close(name);
        _manager = manager;
        _name = name;
        return handle;
    }

    /// <summary>
    /// Keeps the frame's minimum on the content's: when a flex content area
    /// finds its content needs more room (a longer caption), the frame's
    /// minimum becomes that plus the chrome, unless the markup states its own
    /// <c>minw</c>/<c>minh</c>, and a registered window is grown to it through
    /// <see cref="RetailWindowManager.EnforceMinimumSize(string, float, float)"/>.
    /// When the content needs less again, a window it grew goes back toward
    /// the size it had before, unless it was sized otherwise in between.
    /// </summary>
    internal void TrackContentMinimum(
        UiPluginContentHost content, float chromeH, float? authoredMinW, float? authoredMinH)
    {
        content.ContentMinimumChanged += minimum =>
        {
            Frame.MinWidth = PluginWindowChrome.FrameMinimumWidth(authoredMinW ?? minimum.Width, TitleBar is not null);
            Frame.MinHeight = (authoredMinH ?? minimum.Height) + chromeH;

            (float Width, float Height) current = (Frame.Width, Frame.Height);
            if (_beforeGrowth is not null && current != _grownTo) _beforeGrowth = null;
            (float Width, float Height) from = _beforeGrowth ?? current;
            if (_manager is { } manager && _name is { } name)
                manager.EnforceMinimumSize(name, from.Width, from.Height);
            else
            {
                Frame.Width = MathF.Max(from.Width, Frame.MinWidth);
                Frame.Height = MathF.Max(from.Height, Frame.MinHeight);
            }

            (float Width, float Height) now = (Frame.Width, Frame.Height);
            if (now == from) _beforeGrowth = null;
            else
            {
                _beforeGrowth = from;
                _grownTo = now;
            }
        };
    }
}
