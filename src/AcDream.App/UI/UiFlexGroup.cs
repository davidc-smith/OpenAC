namespace AcDream.App.UI;

/// <summary>
/// A markup <c>&lt;group layout="row|column"&gt;</c>: a group that places its
/// children with <see cref="UiFlexBox"/> instead of their coordinates. It
/// draws and themes like any group. As a root (its parent is not a flex
/// container) it lays out its whole tree when drawn; nested in another flex
/// container its children are placed by that container's root. With
/// <c>scroll</c> its items lay out unbounded along the scrolling axes and are
/// seen through a viewport beside its bars.
/// </summary>
internal sealed class UiFlexGroup : UiPanel, IUiScrollHost
{
    public UiFlexBox Flex { get; } = new();

    public UiScrollArea? ScrollArea { get; private set; }

    /// <summary>Makes the group scroll on the given axes.</summary>
    internal void UseScroll(bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve)
    {
        ScrollArea = new UiScrollArea(this, scrollsX, scrollsY, resolve, area => area.ApplyFlex(Flex.Node));
        Flex.Node.ScrollX = scrollsX;
        Flex.Node.ScrollY = scrollsY;
        Flex.Node.ScrollbarSize = UiScrollArea.BarSize;
    }

    private protected override void LayoutChildren()
    {
        Flex.EnsureLayout(Width, Height);
        // A nested scrolling group's node was arranged by its flex root.
        ScrollArea?.ApplyFlex(Flex.Node);
        // Flex items carry no anchors, so this only reaches non-flex children (none in markup).
        base.LayoutChildren();
    }

    public override bool OnEvent(in UiEvent e) => ScrollArea?.OnEvent(e) == true || base.OnEvent(e);
}
