namespace AcDream.App.UI;

/// <summary>
/// A markup <c>&lt;group scroll&gt;</c> without <c>layout</c>: an absolute
/// group whose children, placed by their coordinates and anchors against
/// its viewport, scroll when they reach past it.
/// </summary>
internal sealed class UiScrollPanel : UiPanel, IUiScrollHost
{
    public UiScrollPanel(bool scrollsX, bool scrollsY, Func<uint, (uint, int, int)> resolve) =>
        ScrollArea = new UiScrollArea(this, scrollsX, scrollsY, resolve, static area => area.SettleAbsolute());

    public UiScrollArea ScrollArea { get; }

    UiScrollArea? IUiScrollHost.ScrollArea => ScrollArea;

    private protected override void LayoutChildren() => ScrollArea.SettleAbsolute();

    public override bool OnEvent(in UiEvent e) => ScrollArea.OnEvent(e) || base.OnEvent(e);
}
