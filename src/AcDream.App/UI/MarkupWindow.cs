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
        return handle;
    }
}
