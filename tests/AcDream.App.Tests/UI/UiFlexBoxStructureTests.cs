using AcDream.App.UI;
using AcDream.App.UI.Layout.Flex;
using Xunit;

namespace AcDream.App.Tests.UI;

public sealed class UiFlexBoxStructureTests
{
    private static FlexNode Fixed() => new() { Width = 20f, Height = 20f, Shrink = 0f };

    private static (UiFlexBox box, UiPanel[] kids, UiFlexBox root) Build(bool nested)
    {
        var root = new UiFlexBox();
        root.Node.Direction = FlexDirection.Row;
        var box = root;
        if (nested)
        {
            box = new UiFlexBox();
            box.Node.Direction = FlexDirection.Row;
            root.AddContainer(new UiPanel(), box);
        }
        var kids = new UiPanel[3];
        for (int i = 0; i < 3; i++) box.AddLeaf(kids[i] = new UiPanel(), Fixed());
        Assert.True(root.EnsureLayout(200f, 40f));
        return (box, kids, root);
    }

    private static void AssertOrder(UiFlexBox box, params UiElement[] expected)
    {
        Assert.Equal(expected, box.Items.Select(i => i.Element));
        Assert.Equal(box.Items.Select(i => i.Node), box.Node.Children);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_item_added_after_layout_relayouts_in_the_new_order(bool nested)
    {
        var (box, k, root) = Build(nested);
        var added = new UiPanel();

        box.AddLeaf(added, Fixed());
        box.Move(added, 1);

        AssertOrder(box, k[0], added, k[1], k[2]);
        Assert.True(root.EnsureLayout(200f, 40f));
        Assert.Equal(new[] { 0f, 20f, 40f, 60f }, new[] { k[0], added, k[1], k[2] }.Select(e => e.Left));
        Assert.False(root.EnsureLayout(200f, 40f));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Remove_relayouts_without_the_element(bool nested)
    {
        var (box, k, root) = Build(nested);

        Assert.True(box.Remove(k[0]));
        Assert.False(box.Remove(k[0]));

        AssertOrder(box, k[1], k[2]);
        Assert.True(root.EnsureLayout(200f, 40f));
        Assert.Equal(new[] { 0f, 20f }, new[] { k[1], k[2] }.Select(e => e.Left));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Move_relayouts_in_the_new_order(bool nested)
    {
        var (box, k, root) = Build(nested);

        box.Move(k[0], 2);

        AssertOrder(box, k[1], k[2], k[0]);
        Assert.True(root.EnsureLayout(200f, 40f));
        Assert.Equal(new[] { 0f, 20f, 40f }, new[] { k[1], k[2], k[0] }.Select(e => e.Left));
        Assert.False(root.EnsureLayout(200f, 40f));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Move_to_an_out_of_range_index_throws_and_leaves_the_order(int index)
    {
        var (box, k, root) = Build(false);

        Assert.Throws<ArgumentOutOfRangeException>(() => box.Move(k[0], index));

        AssertOrder(box, k[0], k[1], k[2]);
        Assert.False(root.EnsureLayout(200f, 40f));
    }
}
