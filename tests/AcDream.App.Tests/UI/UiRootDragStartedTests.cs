using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiRootDragStartedTests
{
    private const uint Item = 0x1001;

    /// <summary>A plain drag source, as the retail inventory, a corpse window or the shortcut bar is.</summary>
    private sealed class Source(object payload) : UiPanel
    {
        public override bool IsDragSource => true;
        public override object? GetDragPayload() => payload;
    }

    [Fact]
    public void ADragFromAnyElement_RaisesDragStarted_OnceItBegins()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var payload = new ItemDragPayload(Item, ItemDragSource.Inventory, 0, SourceCell: null);
        root.AddChild(new Source(payload) { Left = 10, Top = 10, Width = 40, Height = 40 });
        var started = new List<object>();
        root.DragStarted += started.Add;

        root.OnMouseDown(UiMouseButton.Left, 20, 20);
        root.OnMouseMove(21, 21);
        Assert.Empty(started);                                 // inside the drag distance
        root.OnMouseMove(60, 60);

        Assert.Same(payload, Assert.Single(started));
    }

    [Fact]
    public void ALiftFromTheWorld_RaisesDragStarted()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var payload = new ItemDragPayload(Item, ItemDragSource.Ground, 0, SourceCell: null);
        root.WorldDragRequested = (_, _) => new WorldDragStart(payload, null);
        var started = new List<object>();
        root.DragStarted += started.Add;

        root.OnMouseDown(UiMouseButton.Left, 300, 300);
        root.OnMouseMove(350, 350);

        Assert.Same(payload, Assert.Single(started));
    }

    private sealed class StackSource(object payload) : UiPanel
    {
        public override bool IsDragSource => true;
        public override object? GetDragPayload() => payload;
        public override IReadOnlyList<uint>? GetDragGhostStack() => [0x11u, 0x22u];
    }

    [Fact]
    public void TheGhostStack_IsCapturedAtDragBegin_AndClearedOnRelease()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var payload = new ItemDragPayload(Item, ItemDragSource.Inventory, 0, SourceCell: null);
        root.AddChild(new StackSource(payload) { Left = 10, Top = 10, Width = 40, Height = 40 });

        root.OnMouseDown(UiMouseButton.Left, 20, 20);
        root.OnMouseMove(60, 60);
        Assert.Equal([0x11u, 0x22u], root.DragGhostStackForTest);

        root.OnMouseUp(UiMouseButton.Left, 60, 60);
        Assert.Null(root.DragGhostStackForTest);
    }

    [Fact]
    public void ADragWithoutAStack_HasNone()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(new Source(new ItemDragPayload(Item, ItemDragSource.Inventory, 0, SourceCell: null)) { Left = 10, Top = 10, Width = 40, Height = 40 });
        root.OnMouseDown(UiMouseButton.Left, 20, 20);
        root.OnMouseMove(60, 60);
        Assert.Null(root.DragGhostStackForTest);
    }
}
