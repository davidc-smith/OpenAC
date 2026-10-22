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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void InsertChild_places_the_child_at_the_index(int index)
    {
        var parent = Parent(out var kids);
        var added = new UiPanel();

        parent.InsertChild(index, added);

        var expected = kids.ToList();
        expected.Insert(index, added);
        Assert.Equal(expected, parent.Children);
        Assert.Same(parent, added.Parent);
    }

    [Fact]
    public void InsertChild_reparents_from_the_old_parent()
    {
        var parent = Parent(out _);
        var other = Parent(out var otherKids, 1);

        parent.InsertChild(0, otherKids[0]);

        Assert.Empty(other.Children);
        Assert.Same(parent, otherKids[0].Parent);
    }

    [Fact]
    public void MoveChild_forward_and_backward()
    {
        var parent = Parent(out var k);

        parent.MoveChild(k[0], 2);
        Assert.Equal(new[] { k[1], k[2], k[0] }, parent.Children);

        parent.MoveChild(k[0], 0);
        Assert.Equal(new[] { k[0], k[1], k[2] }, parent.Children);
    }

    [Fact]
    public void MoveChild_rejects_a_stranger()
    {
        var parent = Parent(out _);
        Assert.Throws<ArgumentException>(() => parent.MoveChild(new UiPanel(), 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void MoveChild_to_an_out_of_range_index_throws_and_leaves_the_children(int index)
    {
        var parent = Parent(out var k);

        Assert.Throws<ArgumentOutOfRangeException>(() => parent.MoveChild(k[0], index));

        Assert.Equal(k, parent.Children);
    }

    [Fact]
    public void MoveChild_keeps_keyboard_focus()
    {
        var root = new UiRoot();
        var a = new UiPanel { AcceptsFocus = true };
        var b = new UiPanel { AcceptsFocus = true };
        root.AddChild(a);
        root.AddChild(b);
        root.SetKeyboardFocus(a);

        root.MoveChild(a, 1);

        Assert.Same(a, root.KeyboardFocus);
    }

    [Fact]
    public void MoveChild_changes_the_tick_order()
    {
        var parent = new UiPanel();
        var order = new List<Counted>();
        var a = new Counted { OnTicked = order.Add };
        var b = new Counted { OnTicked = order.Add };
        parent.AddChild(a);
        parent.AddChild(b);
        parent.TickSelfAndChildren(0.01);
        parent.MoveChild(a, 1);
        order.Clear();

        parent.TickSelfAndChildren(0.01);

        Assert.Equal(new[] { b, a }, order);
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
            parent.InsertChild(0, before);          // before the cursor
            parent.InsertChild(4, after);           // after the cursor
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
    public void InsertChild_and_MoveChild_invalidate_the_traversal_snapshots()
    {
        var parent = new UiPanel();
        var a = new UiPanel();
        var b = new UiPanel();
        parent.AddChild(a);
        parent.AddChild(b);
        UiElement[] beforeInsert = parent.ChildrenBackToFrontSnapshot();

        var c = new UiPanel();
        parent.InsertChild(0, c);
        UiElement[] afterInsert = parent.ChildrenBackToFrontSnapshot();
        parent.MoveChild(c, 2);
        UiElement[] afterMove = parent.ChildrenBackToFrontSnapshot();

        Assert.NotSame(beforeInsert, afterInsert);
        Assert.NotSame(afterInsert, afterMove);
        Assert.Equal(new[] { a, b, c }, afterMove);
    }

    [Fact]
    public void InsertChild_after_a_tick_ticks_the_new_child()
    {
        var parent = new UiPanel();
        parent.AddChild(new Counted());
        parent.TickSelfAndChildren(0.01);
        var added = new Counted();

        parent.InsertChild(0, added);
        parent.TickSelfAndChildren(0.01);

        Assert.Equal(1, added.Ticks);
    }

    private sealed class TestElement : UiElement;
}
