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
}
