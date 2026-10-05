namespace AcDream.App.UI;

/// <summary>
/// A built markup window and what mounting it needs: the frame to add to
/// the root, the element that holds its authored content (the frame itself
/// unless the window has chrome), its title bar if it has one, and the
/// revision its saved size is kept under.
/// </summary>
internal sealed class MarkupWindow(
    UiNineSlicePanel frame, UiElement contentRoot, PluginTitleBar? titleBar, int authoredGeometryRevision)
{
    private RetailWindowManager? _manager;
    private string? _name;

    public UiNineSlicePanel Frame { get; } = frame;
    public UiElement ContentRoot { get; } = contentRoot;
    public PluginTitleBar? TitleBar { get; } = titleBar;
    public int AuthoredGeometryRevision { get; } = authoredGeometryRevision;

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
    /// <see cref="RetailWindowManager.EnforceMinimumSize"/>.
    /// </summary>
    internal void TrackContentMinimum(
        UiPluginContentHost content, float chromeH, float? authoredMinW, float? authoredMinH)
    {
        content.ContentMinimumChanged += minimum =>
        {
            Frame.MinWidth = PluginWindowChrome.FrameMinimumWidth(authoredMinW ?? minimum.Width, TitleBar is not null);
            Frame.MinHeight = (authoredMinH ?? minimum.Height) + chromeH;
            if (_manager is { } manager && _name is { } name)
            {
                manager.EnforceMinimumSize(name);
                return;
            }
            Frame.Width = MathF.Max(Frame.Width, Frame.MinWidth);
            Frame.Height = MathF.Max(Frame.Height, Frame.MinHeight);
        };
    }
}
