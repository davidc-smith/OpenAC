using System.Numerics;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public class UiRootDragReleaseInterceptorTests
{
    private sealed class DropSpy : UiPanel
    {
        public int Drops;
        public override bool OnEvent(in UiEvent e)
        {
            if (e.Type == UiEventType.DropReleased) Drops++;
            return base.OnEvent(in e);
        }
    }

    private static (UiRoot Root, UiItemSlot Source, DropSpy Target) Scene()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var source = new UiItemSlot { Left = 10, Top = 10, Width = 32, Height = 32, SourceKind = ItemDragSource.Inventory };
        source.SetItem(0x5001u, 0x99u);
        var target = new DropSpy { Left = 400, Top = 300, Width = 100, Height = 100 };
        root.AddChild(source);
        root.AddChild(target);
        return (root, source, target);
    }

    private static void DragTo(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, 20, 20);
        root.OnMouseMove(30, 30);      // past the 3-point drag threshold
        root.OnMouseMove(x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void AClaimingInterceptorEndsTheDragWithNoDrop()
    {
        var (root, _, target) = Scene();
        object? seen = null;
        root.DragReleaseInterceptor = (payload, _) => { seen = payload; return true; };
        int outside = 0;
        root.DragReleasedOutsideUi += (_, _, _) => outside++;

        DragTo(root, 450, 350);

        Assert.IsType<ItemDragPayload>(seen);
        Assert.Equal(0, target.Drops);
        Assert.Equal(0, outside);
        Assert.Null(root.DragSource);
        Assert.Null(root.DragPayload);
    }

    [Fact]
    public void ADecliningInterceptorLeavesTheDropAsToday()
    {
        var (root, _, target) = Scene();
        root.DragReleaseInterceptor = (_, _) => false;
        int outside = 0;
        root.DragReleasedOutsideUi += (_, _, _) => outside++;

        DragTo(root, 450, 350);
        Assert.Equal(1, target.Drops);

        DragTo(root, 700, 50);
        Assert.Equal(1, outside);
    }

    [Fact]
    public void TheInterceptorGetsTheWindowPointNotTheCanvasPoint()
    {
        var (root, _, _) = Scene();
        root.FixedCanvasSize = new Vector2(400, 300); // CanvasScale = (2,2)
        Assert.Equal(new Vector2(2, 2), root.CanvasScale);
        Vector2 point = default;
        root.DragReleaseInterceptor = (_, p) => { point = p; return true; };

        root.OnMouseDown(UiMouseButton.Left, 40, 40);  // canvas 20,20: on the slot
        root.OnMouseMove(60, 60);
        root.OnMouseUp(UiMouseButton.Left, 300, 200);

        Assert.Equal(new Vector2(300, 200), point);
    }

    [Fact]
    public void APlainClickNeverAsks()
    {
        var (root, _, _) = Scene();
        int asked = 0;
        root.DragReleaseInterceptor = (_, _) => { asked++; return true; };

        root.OnMouseDown(UiMouseButton.Left, 20, 20);
        root.OnMouseUp(UiMouseButton.Left, 20, 20);

        Assert.Equal(0, asked);
    }
}
