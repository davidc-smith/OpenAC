using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Scrolling containers that reserve room for their bars
/// (<see cref="FlexNode.ScrollbarSize"/>): which bars settle, the viewport
/// their items lay out in, and a scrolling grid laid across a column.
/// </summary>
public sealed class FlexLayoutScrollbarTests
{
    private static FlexNode[] Items(int count, float width, float height)
    {
        var items = new FlexNode[count];
        for (int i = 0; i < count; i++) items[i] = Leaf(width, height);
        return items;
    }

    private static FlexNode Scrolling(FlexNode node, bool x, bool y)
    {
        node.ScrollX = x;
        node.ScrollY = y;
        node.ScrollbarSize = 16f;
        return node;
    }

    [Fact]
    public void Content_that_fits_shows_no_bars_and_uses_the_whole_rect()
    {
        FlexNode a = Leaf(50f, 30f);
        FlexNode column = Scrolling(Column(a), x: false, y: true);

        Arrange(column, 100f, 100f);

        Assert.False(column.ScrollbarY);
        Assert.False(column.ScrollbarX);
        Assert.Equal(new FlexSize(100f, 100f), column.Viewport);
        Assert.Equal(new FlexRect(0f, 0f, 100f, 30f), a.Rect);
    }

    [Fact]
    public void A_vertical_bar_narrows_the_viewport_the_items_stretch_across()
    {
        FlexNode a = Leaf(50f, 80f), b = Leaf(50f, 80f);
        FlexNode column = Scrolling(Column(a, b), x: false, y: true);

        Arrange(column, 100f, 100f);

        Assert.True(column.ScrollbarY);
        Assert.False(column.ScrollbarX);
        Assert.Equal(new FlexSize(84f, 100f), column.Viewport);
        Assert.Equal(new FlexRect(0f, 80f, 84f, 80f), b.Rect);
        Assert.Equal(new FlexSize(84f, 160f), column.ContentSize);
    }

    [Fact]
    public void A_wrapping_grid_rebreaks_its_lines_beside_the_vertical_bar()
    {
        FlexNode[] items = Items(7, 30f, 30f);
        FlexNode row = Scrolling(Row(items), x: false, y: true);
        row.Wrap = true;
        row.Gap = 5f;

        Arrange(row, 100f, 50f);

        // At 100 three fit a line (3 lines, 100 tall: overflows); at 84 two do.
        Assert.True(row.ScrollbarY);
        Assert.Equal(new FlexRect(0f, 105f, 30f, 30f), items[6].Rect);
        Assert.Equal(new FlexSize(65f, 135f), row.ContentSize);
    }

    [Fact]
    public void A_vertical_bar_that_makes_the_content_too_wide_brings_the_horizontal_bar()
    {
        // 90 wide fits 100, but not the 84 left beside the vertical bar.
        FlexNode a = Leaf(90f, 80f), b = Leaf(90f, 80f);
        FlexNode column = Scrolling(Column(a, b), x: true, y: true);

        Arrange(column, 100f, 100f);

        Assert.True(column.ScrollbarY);
        Assert.True(column.ScrollbarX);
        Assert.Equal(new FlexSize(84f, 84f), column.Viewport);
    }

    [Fact]
    public void A_horizontal_bar_that_makes_the_content_too_tall_brings_the_vertical_bar()
    {
        // 95 tall fits 100, but not the 84 left above the horizontal bar.
        FlexNode a = Leaf(80f, 95f), b = Leaf(80f, 95f);
        FlexNode row = Scrolling(Row(a, b), x: true, y: true);
        row.Align = FlexAlign.Start;

        Arrange(row, 100f, 100f);

        Assert.True(row.ScrollbarX);
        Assert.True(row.ScrollbarY);
        Assert.Equal(new FlexSize(84f, 84f), row.Viewport);
    }

    [Fact]
    public void A_horizontal_scroller_whose_content_fits_beside_no_bar_shows_none()
    {
        FlexNode a = Leaf(40f, 20f), b = Leaf(40f, 20f);
        FlexNode row = Scrolling(Row(a, b), x: true, y: false);

        Arrange(row, 100f, 50f);

        Assert.False(row.ScrollbarX);
        Assert.Equal(new FlexSize(100f, 50f), row.Viewport);
        Assert.Equal(new FlexRect(40f, 0f, 40f, 50f), b.Rect);
    }

    [Fact]
    public void Without_a_scrollbar_size_nothing_is_reserved()
    {
        FlexNode a = Leaf(50f, 80f), b = Leaf(50f, 80f);
        FlexNode column = Column(a, b);
        column.ScrollY = true;

        Arrange(column, 100f, 100f);

        Assert.False(column.ScrollbarY);
        Assert.Equal(new FlexSize(100f, 100f), column.Viewport);
        Assert.Equal(100f, b.Rect.Width);
    }

    [Fact]
    public void A_nested_scroller_settles_its_own_bars_in_the_rect_its_parent_gives_it()
    {
        FlexNode header = Leaf(50f, 20f);
        FlexNode[] rows = Items(5, 50f, 30f);
        FlexNode list = Scrolling(Column(rows), x: false, y: true);
        list.Grow = 1f;
        FlexNode root = Column(header, list);

        Arrange(root, 120f, 100f);

        Assert.Equal(new FlexRect(0f, 20f, 120f, 80f), list.Rect);
        Assert.True(list.ScrollbarY);
        Assert.Equal(new FlexSize(104f, 80f), list.Viewport);
        Assert.Equal(104f, rows[0].Rect.Width);
    }

    [Fact]
    public void A_scrolling_grid_in_a_column_shows_all_its_lines_when_there_is_room()
    {
        FlexNode[] items = Items(6, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        FlexNode footer = Leaf(50f, 20f);
        FlexNode root = Column(grid, footer);
        root.Align = FlexAlign.Stretch;

        Arrange(root, 90f, 200f);

        // Three a line at 90: two lines, 60 tall, without a bar.
        Assert.Equal(new FlexRect(0f, 0f, 90f, 60f), grid.Rect);
        Assert.False(grid.ScrollbarY);
        Assert.Equal(new FlexRect(0f, 60f, 90f, 20f), footer.Rect);
    }

    [Fact]
    public void A_scrolling_grid_in_a_column_shrinks_to_scroll_when_there_is_not()
    {
        FlexNode[] items = Items(6, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        FlexNode footer = Leaf(50f, 20f);
        FlexNode root = Column(grid, footer);

        Arrange(root, 90f, 70f);

        Assert.Equal(new FlexRect(0f, 0f, 90f, 50f), grid.Rect);
        Assert.True(grid.ScrollbarY);
        Assert.Equal(new FlexRect(0f, 50f, 90f, 20f), footer.Rect);
    }

    [Fact]
    public void A_scrolling_grid_in_a_column_never_shrinks_below_its_viewport_minimum()
    {
        FlexNode[] items = Items(6, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        FlexNode footer = Leaf(50f, 20f);
        FlexNode root = Column(grid, footer);

        Arrange(root, 90f, 30f);

        Assert.Equal(FlexLayout.MinimumScrollViewport, grid.Rect.Height);
    }

    [Fact]
    public void The_fitted_minimum_of_a_vertical_scroller_leaves_room_for_its_bar()
    {
        FlexNode a = Leaf(50f, 80f), b = Leaf(50f, 80f);
        FlexNode root = Scrolling(Column(a, b), x: false, y: true);

        FlexSize fitted = FlexFit.Minimum(root);

        Assert.Equal(new FlexSize(66f, FlexLayout.MinimumScrollViewport), fitted);
        Assert.True(root.ScrollbarY);
        Assert.Equal(50f, a.Rect.Width);
    }

    [Fact]
    public void Settling_the_bars_allocates_nothing_once_warm()
    {
        FlexNode[] items = Items(60, 30f, 30f);
        FlexNode grid = Scrolling(Row(items), x: false, y: true);
        grid.Wrap = true;
        grid.Gap = 4f;
        for (int width = 100; width < 200; width++)
            FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 120f));

        // The least of a few rounds: the runtime's own tiering work can land in
        // one, but an allocation by the layout would show in every round.
        long least = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int width = 100; width < 200; width++)
                FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 120f));
            least = Math.Min(least, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.True(grid.ScrollbarY);
        Assert.Equal(0L, least);
    }
}
