using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiRootResizeHookTests
{
    /// <summary>Allows only the right side; snaps height to multiples of 50 on a corner.</summary>
    private sealed class RightOnlyPanel : UiPanel
    {
        internal override ResizeEdges FilterResizeEdges(ResizeEdges hit) => hit switch
        {
            ResizeEdges.Right
                or (ResizeEdges.Right | ResizeEdges.Top)
                or (ResizeEdges.Right | ResizeEdges.Bottom) => hit,
            _ => ResizeEdges.None,
        };

        internal override void ConstrainResize(ResizeEdges edges, ref float width, ref float height)
        {
            if ((edges & (ResizeEdges.Top | ResizeEdges.Bottom)) != 0)
                height = MathF.Round(height / 50f) * 50f;
        }
    }

    private static (UiRoot Root, RightOnlyPanel Window) Make()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var window = new RightOnlyPanel
        {
            Left = 100, Top = 100, Width = 200, Height = 100,
            Draggable = true, Resizable = true, MinWidth = 40, MinHeight = 40,
        };
        root.AddChild(window);
        return (root, window);
    }

    [Fact]
    public void FilteredLeftEdge_ShowsNoResizeAndDoesNotResize()
    {
        var (root, window) = Make();
        root.OnMouseMove(101, 150);
        Assert.Equal(ResizeEdges.None, root.HoverResizeEdges);

        root.OnMouseDown(UiMouseButton.Left, 101, 150);
        root.OnMouseMove(60, 150);
        root.OnMouseUp(UiMouseButton.Left, 60, 150);
        Assert.Equal(200f, window.Width);
    }

    [Fact]
    public void RightEdge_StillResizes()
    {
        var (root, window) = Make();
        root.OnMouseMove(298, 150);
        Assert.Equal(ResizeEdges.Right, root.HoverResizeEdges);

        root.OnMouseDown(UiMouseButton.Left, 298, 150);
        root.OnMouseMove(338, 150);
        root.OnMouseUp(UiMouseButton.Left, 338, 150);
        Assert.Equal(240f, window.Width);
        Assert.Equal(100f, window.Left);
    }

    [Fact]
    public void ConstrainedTopRightDrag_KeepsTheBottomFixed()
    {
        var (root, window) = Make();
        // Top-right corner is (300, 100). Drag up 30: proposed height 130 snaps to 150.
        root.OnMouseDown(UiMouseButton.Left, 299, 101);
        root.OnMouseMove(299, 71);
        Assert.Equal(150f, window.Height);
        Assert.Equal(200f, window.Top + window.Height);
        Assert.Equal(100f, window.Left);
        root.OnMouseUp(UiMouseButton.Left, 299, 71);
    }

    [Fact]
    public void ResizeEdgesFor_FollowsHoverAndActiveResize()
    {
        var (root, window) = Make();
        var other = new UiPanel { Left = 500, Top = 100, Width = 100, Height = 100, Resizable = true };
        root.AddChild(other);

        root.OnMouseMove(298, 150);
        Assert.Equal(ResizeEdges.Right, root.ResizeEdgesFor(window));
        Assert.Equal(ResizeEdges.None, root.ResizeEdgesFor(other));

        root.OnMouseDown(UiMouseButton.Left, 298, 150);
        root.OnMouseMove(400, 300);   // pointer leaves the edge; the resize is still active
        Assert.Equal(ResizeEdges.Right, root.ResizeEdgesFor(window));
        root.OnMouseUp(UiMouseButton.Left, 400, 300);

        root.OnMouseMove(200, 150);
        Assert.Equal(ResizeEdges.None, root.ResizeEdgesFor(window));
    }

    [Fact]
    public void DefaultHooks_ChangeNothing()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var window = new UiPanel { Left = 100, Top = 100, Width = 200, Height = 100,
            Draggable = true, Resizable = true, MinWidth = 40, MinHeight = 40 };
        root.AddChild(window);

        root.OnMouseDown(UiMouseButton.Left, 101, 101);   // top-left corner
        root.OnMouseMove(81, 71);
        root.OnMouseUp(UiMouseButton.Left, 81, 71);
        Assert.Equal((80f, 70f, 220f, 130f), (window.Left, window.Top, window.Width, window.Height));
    }
}
