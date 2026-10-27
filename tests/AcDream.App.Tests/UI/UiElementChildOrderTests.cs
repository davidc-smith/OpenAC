using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiElementChildOrderTests
{
    [Fact]
    public void StableTree_ReusesBothTraversalSnapshots()
    {
        var parent = new TestElement();
        parent.AddChild(new TestElement { ZOrder = 2 });
        parent.AddChild(new TestElement { ZOrder = 1 });

        UiElement[] drawFirst = parent.ChildrenBackToFrontSnapshot();
        UiElement[] drawSecond = parent.ChildrenBackToFrontSnapshot();
        UiElement[] hitFirst = parent.ChildrenFrontToBackSnapshot();
        UiElement[] hitSecond = parent.ChildrenFrontToBackSnapshot();

        Assert.Same(drawFirst, drawSecond);
        Assert.Same(hitFirst, hitSecond);
        Assert.Equal([1, 2], drawFirst.Select(child => child.ZOrder).ToArray());
        Assert.Equal([2, 1], hitFirst.Select(child => child.ZOrder).ToArray());
    }

    [Fact]
    public void ChildZOrderChange_InvalidatesBothTraversalSnapshots()
    {
        var parent = new TestElement();
        var first = new TestElement { ZOrder = 1 };
        var second = new TestElement { ZOrder = 2 };
        parent.AddChild(first);
        parent.AddChild(second);
        UiElement[] oldDraw = parent.ChildrenBackToFrontSnapshot();
        UiElement[] oldHit = parent.ChildrenFrontToBackSnapshot();

        first.ZOrder = 3;
        UiElement[] newDraw = parent.ChildrenBackToFrontSnapshot();
        UiElement[] newHit = parent.ChildrenFrontToBackSnapshot();

        Assert.NotSame(oldDraw, newDraw);
        Assert.NotSame(oldHit, newHit);
        Assert.Same(second, newDraw[0]);
        Assert.Same(first, newDraw[1]);
        Assert.Same(first, newHit[0]);
        Assert.Same(second, newHit[1]);
    }

    [Fact]
    public void MembershipChange_InvalidatesSnapshotsWithoutMutatingPriorWalk()
    {
        var parent = new TestElement();
        var first = new TestElement { ZOrder = 1 };
        var second = new TestElement { ZOrder = 2 };
        parent.AddChild(first);
        UiElement[] prior = parent.ChildrenBackToFrontSnapshot();

        parent.AddChild(second);
        UiElement[] current = parent.ChildrenBackToFrontSnapshot();

        Assert.Single(prior);
        Assert.Same(first, prior[0]);
        Assert.Equal(2, current.Length);
        Assert.NotSame(prior, current);
    }

    private sealed class Counted : UiElement
    {
        public int Ticks;
        public Action<Counted>? OnTicked;
        protected override void OnTick(double dt) { Ticks++; OnTicked?.Invoke(this); }
    }

    private static UiPanel Parent(out UiElement[] kids, int n = 3)
    {
        var parent = new UiPanel();
        kids = new UiElement[n];
        for (int i = 0; i < n; i++) parent.AddChild(kids[i] = new UiPanel());
        return parent;
    }

    [Fact]
    public void A_child_changing_its_siblings_during_a_tick_ticks_each_remaining_snapshot_child_once()
    {
        var parent = new UiPanel();
        var a = new Counted();
        var b = new Counted();
        var c = new Counted();
        var d = new Counted();
        var before = new Counted();
        var after = new Counted();
        b.OnTicked = self =>
        {
            if (self.Ticks > 1) return;
            parent.AddChild(before);
            parent.MoveChild(before, 0);            // before the cursor
            parent.AddChild(after);
            parent.MoveChild(after, 4);             // after the cursor
            parent.RemoveChild(a);                  // already ticked
            parent.RemoveChild(c);                  // not yet ticked, but in the snapshot
        };
        foreach (var e in new[] { a, b, c, d }) parent.AddChild(e);

        parent.TickSelfAndChildren(0.01);

        Assert.Equal(1, a.Ticks);
        Assert.Equal(1, b.Ticks);
        Assert.Equal(0, c.Ticks);
        Assert.Equal(1, d.Ticks);
        Assert.Equal(0, before.Ticks);
        Assert.Equal(0, after.Ticks);
    }

    [Fact]
    public void A_steady_tick_allocates_nothing()
    {
        var parent = Parent(out _, 5);
        parent.TickSelfAndChildren(0.01);   // builds the snapshot

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) parent.TickSelfAndChildren(0.01);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void MoveChild_invalidates_the_traversal_snapshots()
    {
        var parent = new UiPanel();
        var a = new UiPanel();
        var b = new UiPanel();
        var c = new UiPanel();
        parent.AddChild(c);
        parent.AddChild(a);
        parent.AddChild(b);
        UiElement[] beforeMove = parent.ChildrenBackToFrontSnapshot();

        parent.MoveChild(c, 2);
        UiElement[] afterMove = parent.ChildrenBackToFrontSnapshot();

        Assert.NotSame(beforeMove, afterMove);
        Assert.Equal(new[] { a, b, c }, afterMove);
    }

    [Fact]
    public void A_child_moved_in_after_a_tick_ticks_on_the_next()
    {
        var parent = new UiPanel();
        parent.AddChild(new Counted());
        parent.TickSelfAndChildren(0.01);
        var added = new Counted();

        parent.AddChild(added);
        parent.MoveChild(added, 0);
        parent.TickSelfAndChildren(0.01);

        Assert.Equal(1, added.Ticks);
    }

    [Fact]
    public void RemoveChildren_removes_the_set_in_one_pass_keeping_the_order_of_the_rest()
    {
        var parent = Parent(out var kids, 5);
        UiElement[] before = parent.ChildrenBackToFrontSnapshot();

        parent.RemoveChildren(new HashSet<UiElement> { kids[0], kids[2], kids[4] });

        Assert.Equal(new[] { kids[1], kids[3] }, parent.Children);
        Assert.Null(kids[0].Parent);
        Assert.Null(kids[4].Parent);
        Assert.Same(parent, kids[1].Parent);
        Assert.NotSame(before, parent.ChildrenBackToFrontSnapshot());
    }

    [Fact]
    public void RemoveChildren_drops_focus_inside_a_removed_child()
    {
        var root = new UiRoot { Width = 100, Height = 100 };
        var parent = new UiPanel();
        var field = new UiField { Width = 50, Height = 20 };
        parent.AddChild(field);
        root.AddChild(parent);
        root.SetKeyboardFocus(field);
        Assert.Same(field, root.KeyboardFocus);

        parent.RemoveChildren(new HashSet<UiElement> { field });

        Assert.Null(root.KeyboardFocus);
    }

    private sealed class TestElement : UiElement;
}
