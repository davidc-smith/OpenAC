using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiRootWindowDragHookTests
{
    private sealed class SnappingWindow : UiPanel
    {
        public readonly List<(float Left, float Top, int X, int Y)> Calls = [];

        internal override void ConstrainWindowDrag(ref float left, ref float top, int pointerX, int pointerY)
        {
            Calls.Add((left, top, pointerX, pointerY));
            if (left < 20f) left = 0f;
        }
    }

    private static (UiRoot Root, T Window) Mount<T>(T window) where T : UiPanel
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        window.Left = 100f;
        window.Top = 100f;
        window.Width = 120f;
        window.Height = 80f;
        window.AddChild(new UiPanel { Width = 120f, Height = 12f, WindowMoveHandle = true });
        root.AddChild(window);
        return (root, window);
    }

    [Fact]
    public void TheDraggedWindowSeesTheClampedPositionAndThePointer_AndCanMoveIt()
    {
        var (root, window) = Mount(new SnappingWindow());

        root.OnMouseDown(UiMouseButton.Left, 110, 105);
        root.OnMouseMove(25, 205);
        root.OnMouseUp(UiMouseButton.Left, 25, 205);

        Assert.Equal((15f, 200f, 25, 205), Assert.Single(window.Calls));
        Assert.Equal(0f, window.Left);
        Assert.Equal(200f, window.Top);
    }

    [Fact]
    public void AnyOtherWindowMovesExactlyAsBefore()
    {
        var (root, window) = Mount(new UiPanel());

        root.OnMouseDown(UiMouseButton.Left, 110, 105);
        root.OnMouseMove(25, 205);
        root.OnMouseMove(-50, 900);
        root.OnMouseUp(UiMouseButton.Left, -50, 900);

        Assert.Equal(0f, window.Left);
        Assert.Equal(520f, window.Top);
    }
}
