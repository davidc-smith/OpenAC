using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Several lines and unbounded axes: wrapping breaks greedily by each item's
/// clamped size, spare cross space is shared between lines (CSS's default
/// align-content), and an axis that scrolls lays its items out with no limit
/// and reports how far they reach.
/// </summary>
public sealed class FlexLayoutWrapScrollTests
{
    private static FlexNode[] Items(int count, float width, float height)
    {
        var items = new FlexNode[count];
        for (int i = 0; i < count; i++) items[i] = Leaf(width, height);
        return items;
    }

    [Fact]
    public void Wrap_breaks_items_into_lines_that_fit()
    {
        FlexNode[] items = Items(5, 30f, 10f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.Gap = 5f;
        row.Align = FlexAlign.Start;

        Arrange(row, 100f, 100f);

        // 30 + 5 + 30 + 5 + 30 = 100 fits; lines take (100 - 20 - 5) / 2 = 37.5
        // extra each, so the second line starts at 47.5 + 5 = 52.5, rounded 53.
        Assert.Equal(new FlexRect(0f, 0f, 30f, 10f), items[0].Rect);
        Assert.Equal(new FlexRect(70f, 0f, 30f, 10f), items[2].Rect);
        Assert.Equal(new FlexRect(0f, 53f, 30f, 10f), items[3].Rect);
        Assert.Equal(new FlexRect(35f, 53f, 30f, 10f), items[4].Rect);
    }

    [Fact]
    public void Wrapped_lines_stretch_their_items_over_the_shared_space()
    {
        FlexNode[] items = Items(5, 30f, 10f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.Gap = 5f;

        Arrange(row, 100f, 100f);

        // Lines are 47.5 high: edges 0..47.5 and 52.5..100 round to 0..48 and 53..100.
        Assert.Equal(48f, items[0].Rect.Height);
        Assert.Equal(new FlexRect(0f, 53f, 30f, 47f), items[3].Rect);
    }

    [Fact]
    public void An_item_wider_than_the_line_gets_a_line_of_its_own()
    {
        FlexNode small = Leaf(30f, 10f), wide = Leaf(150f, 10f), after = Leaf(30f, 10f);
        FlexNode row = Row(small, wide, after);
        row.Wrap = true;
        row.Align = FlexAlign.Start;
        row.ScrollY = true;

        Arrange(row, 100f, 100f);

        Assert.Equal(new[] { 0f, 10f, 20f }, new[] { small.Rect.Y, wide.Rect.Y, after.Rect.Y });
        Assert.Equal(150f, wide.Rect.Width);
    }

    [Fact]
    public void Hidden_items_do_not_take_a_place_in_a_line()
    {
        FlexNode a = Leaf(50f, 10f), hidden = Leaf(50f, 10f), b = Leaf(50f, 10f);
        hidden.Hidden = true;
        FlexNode row = Row(a, hidden, b);
        row.Wrap = true;
        row.ScrollY = true;

        Arrange(row, 100f, 100f);

        Assert.Equal(new FlexRect(50f, 0f, 50f, 10f), b.Rect);
    }

    [Fact]
    public void A_scrolling_column_does_not_shrink_its_items()
    {
        FlexNode a = Leaf(50f, 80f, minHeight: 10f), b = Leaf(50f, 80f, minHeight: 10f);
        FlexNode column = Column(a, b);
        column.ScrollY = true;
        column.Gap = 4f;
        column.Padding = FlexEdges.All(2f);

        Arrange(column, 100f, 100f);

        Assert.Equal(new FlexRect(2f, 86f, 96f, 80f), b.Rect);
        Assert.Equal(new FlexSize(100f, 168f), column.ContentSize);
    }

    [Fact]
    public void A_wrapping_row_that_scrolls_vertically_keeps_its_width_and_grows_down()
    {
        FlexNode[] items = Items(7, 30f, 30f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.Gap = 5f;
        row.ScrollY = true;

        Arrange(row, 100f, 50f);

        // Three per line: lines at 0, 35 and 70; nothing stretches the lines.
        Assert.Equal(new FlexRect(0f, 70f, 30f, 30f), items[6].Rect);
        Assert.Equal(new FlexSize(100f, 100f), row.ContentSize);
    }

    [Fact]
    public void Wrap_does_not_break_along_an_axis_that_scrolls()
    {
        FlexNode[] items = Items(5, 30f, 10f);
        FlexNode row = Row(items);
        row.Wrap = true;
        row.ScrollX = true;

        Arrange(row, 100f, 10f);

        Assert.Equal(new FlexRect(120f, 0f, 30f, 10f), items[4].Rect);
        Assert.Equal(new FlexSize(150f, 10f), row.ContentSize);
    }
}
