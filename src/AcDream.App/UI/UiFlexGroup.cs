namespace AcDream.App.UI;

/// <summary>
/// A markup <c>&lt;group layout="row|column"&gt;</c>: a group that places its
/// children with <see cref="UiFlexBox"/> instead of their coordinates. It
/// draws and themes like any group. As a root (its parent is not a flex
/// container) it lays out its whole tree when drawn; nested in another flex
/// container its children are placed by that container's root.
/// </summary>
internal sealed class UiFlexGroup : UiPanel
{
    public UiFlexBox Flex { get; } = new();

    private protected override void LayoutChildren()
    {
        Flex.EnsureLayout(Width, Height);
        // Flex items carry no anchors, so this only reaches non-flex children (none in markup).
        base.LayoutChildren();
    }
}
