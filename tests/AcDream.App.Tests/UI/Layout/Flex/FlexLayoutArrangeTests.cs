using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// One line: free space is shared by grow, overflow taken back by shrink
/// weighted by basis, limits freeze items and the rest re-flex, justify and
/// align place the items, and edges snap to whole points without drifting.
/// Every expected value is worked by hand from the CSS flexbox algorithm.
/// </summary>
public sealed class FlexLayoutArrangeTests
{
    [Fact]
    public void Root_keeps_the_bounds_it_is_given()
    {
        FlexNode row = Row(Leaf(10f, 10f));

        FlexLayout.Arrange(row, new FlexRect(5.5f, 7f, 100f, 30f));

        Assert.Equal(new FlexRect(5.5f, 7f, 100f, 30f), row.Rect);
    }

    [Fact]
    public void Grow_shares_free_space_by_factor()
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(50f, 10f);
        a.Grow = 1f;
        b.Grow = 2f;

        Arrange(Row(a, b), 300f, 10f);

        // 200 free: a gets 66.67, b 133.33; the shared edge 116.67 rounds to 117.
        Assert.Equal(new FlexRect(0f, 0f, 117f, 10f), a.Rect);
        Assert.Equal(new FlexRect(117f, 0f, 183f, 10f), b.Rect);
    }

    [Fact]
    public void No_grow_leaves_items_at_their_basis()
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(30f, 10f);

        Arrange(Row(a, b), 300f, 10f);

        Assert.Equal(new FlexRect(0f, 0f, 50f, 10f), a.Rect);
        Assert.Equal(new FlexRect(50f, 0f, 30f, 10f), b.Rect);
    }

    [Fact]
    public void Factors_summing_below_one_take_that_share_of_the_initial_free_space()
    {
        FlexNode a = Leaf(50f, 10f, minWidth: 0f), b = Leaf(50f, 10f, minWidth: 0f);
        a.Grow = 0.5f;
        a.MaxWidth = 100f;
        b.Grow = 0.25f;

        Arrange(Row(a, b), 300f, 10f);

        // Initial free space 300 - 100 = 200; factors sum to 0.75, so 150 is
        // shared: a 50 + 100 = 150 clamps to 100 and freezes. Then b alone has
        // factor 0.25: 0.25 x 200 (initial) = 50 is less than the 150 left, so
        // b gets 50 + 50 = 100 (not 0.25 x 150 = 37.5 of the remaining space).
        Assert.Equal(new FlexRect(0f, 0f, 100f, 10f), a.Rect);
        Assert.Equal(new FlexRect(100f, 0f, 100f, 10f), b.Rect);
    }

    [Fact]
    public void Shrink_is_weighted_by_basis()
    {
        FlexNode a = Leaf(100f, 10f, minWidth: 0f), b = Leaf(50f, 10f, minWidth: 0f);

        Arrange(Row(a, b), 100f, 10f);

        // 50 over: a gives back 50 * 100/150, b 50 * 50/150.
        Assert.Equal(new FlexRect(0f, 0f, 67f, 10f), a.Rect);
        Assert.Equal(new FlexRect(67f, 0f, 33f, 10f), b.Rect);
    }

    [Fact]
    public void An_item_shrunk_to_its_minimum_freezes_and_the_rest_take_the_remainder()
    {
        FlexNode a = Leaf(100f, 10f, minWidth: 80f), b = Leaf(100f, 10f, minWidth: 0f);

        Arrange(Row(a, b), 100f, 10f);

        // First pass: both 50, a clamps to 80. Second pass: b alone gives back 80.
        Assert.Equal(80f, a.Rect.Width);
        Assert.Equal(new FlexRect(80f, 0f, 20f, 10f), b.Rect);
    }

    [Fact]
    public void An_item_grown_to_its_maximum_freezes_and_the_rest_take_the_remainder()
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(50f, 10f);
        a.Grow = 1f;
        a.MaxWidth = 100f;
        b.Grow = 1f;

        Arrange(Row(a, b), 300f, 10f);

        Assert.Equal(100f, a.Rect.Width);
        Assert.Equal(new FlexRect(100f, 0f, 200f, 10f), b.Rect);
    }

    [Fact]
    public void Grow_factors_summing_below_one_take_only_that_share()
    {
        FlexNode a = Leaf(100f, 10f);
        a.Grow = 0.5f;

        Arrange(Row(a), 200f, 10f);

        Assert.Equal(150f, a.Rect.Width);
    }

    [Fact]
    public void Basis_replaces_the_measured_size_as_the_starting_point()
    {
        FlexNode a = Leaf(10f, 10f, minWidth: 0f), b = Leaf(10f, 10f, minWidth: 0f);
        a.Basis = 0f;
        a.Grow = 1f;
        b.Basis = 0f;
        b.Grow = 1f;

        Arrange(Row(a, b), 100f, 10f);

        Assert.Equal(50f, a.Rect.Width);
        Assert.Equal(50f, b.Rect.Width);
    }

    [Theory]
    [InlineData(FlexJustify.Start, 0f, 50f, 100f)]
    [InlineData(FlexJustify.Center, 75f, 125f, 175f)]
    [InlineData(FlexJustify.End, 150f, 200f, 250f)]
    [InlineData(FlexJustify.SpaceBetween, 0f, 125f, 250f)]
    public void Justify_places_items_along_the_main_axis(FlexJustify justify, float x0, float x1, float x2)
    {
        FlexNode a = Leaf(50f, 10f), b = Leaf(50f, 10f), c = Leaf(50f, 10f);
        FlexNode row = Row(a, b, c);
        row.Justify = justify;

        Arrange(row, 300f, 10f);

        Assert.Equal(new[] { x0, x1, x2 }, new[] { a.Rect.X, b.Rect.X, c.Rect.X });
    }

    [Fact]
    public void Justify_does_not_move_items_that_overflow()
    {
        FlexNode a = Leaf(40f, 10f), b = Leaf(40f, 10f);
        FlexNode row = Row(a, b);
        row.Justify = FlexJustify.Center;

        Arrange(row, 50f, 10f);

        Assert.Equal(new FlexRect(0f, 0f, 40f, 10f), a.Rect);
        Assert.Equal(new FlexRect(40f, 0f, 40f, 10f), b.Rect);
        Assert.Equal(new FlexSize(80f, 10f), row.ContentSize);
    }

    [Fact]
    public void Padding_and_gap_offset_the_items()
    {
        FlexNode a = Leaf(20f, 10f), b = Leaf(20f, 10f);
        FlexNode row = Row(a, b);
        row.Padding = FlexEdges.All(10f);
        row.Gap = 5f;

        Arrange(row, 200f, 50f);

        Assert.Equal(new FlexRect(10f, 10f, 20f, 30f), a.Rect);
        Assert.Equal(new FlexRect(35f, 10f, 20f, 30f), b.Rect);
    }

    [Theory]
    [InlineData(FlexAlign.Start, 0f, 10f)]
    [InlineData(FlexAlign.Center, 20f, 10f)]
    [InlineData(FlexAlign.End, 40f, 10f)]
    [InlineData(FlexAlign.Stretch, 0f, 50f)]
    public void Align_places_items_across_the_line(FlexAlign align, float y, float height)
    {
        FlexNode a = Leaf(20f, 10f);
        FlexNode row = Row(a);
        row.Align = align;

        Arrange(row, 100f, 50f);

        Assert.Equal(new FlexRect(0f, y, 20f, height), a.Rect);
    }

    [Fact]
    public void AlignSelf_overrides_the_container()
    {
        FlexNode a = Leaf(20f, 10f), b = Leaf(20f, 10f);
        b.AlignSelf = FlexAlign.End;

        Arrange(Row(a, b), 100f, 50f);

        Assert.Equal(50f, a.Rect.Height);
        Assert.Equal(new FlexRect(20f, 40f, 20f, 10f), b.Rect);
    }

    [Fact]
    public void Stretch_keeps_an_explicit_cross_size_and_respects_the_maximum()
    {
        FlexNode fixedHeight = Leaf(20f, 10f), capped = Leaf(20f, 10f);
        fixedHeight.Height = 12f;
        capped.MaxHeight = 30f;

        Arrange(Row(fixedHeight, capped), 100f, 50f);

        Assert.Equal(12f, fixedHeight.Rect.Height);
        Assert.Equal(30f, capped.Rect.Height);
    }

    [Fact]
    public void Hidden_items_take_no_space()
    {
        FlexNode a = Leaf(20f, 10f), hidden = Leaf(500f, 10f), b = Leaf(20f, 10f);
        hidden.Hidden = true;
        FlexNode row = Row(a, hidden, b);
        row.Gap = 4f;

        Arrange(row, 100f, 10f);

        Assert.Equal(24f, b.Rect.X);
        Assert.Equal(default, hidden.Rect);
    }

    [Fact]
    public void Column_lays_out_vertically()
    {
        FlexNode a = Leaf(20f, 50f), b = Leaf(30f, 50f);
        b.Grow = 1f;
        FlexNode column = Column(a, b);
        column.Align = FlexAlign.Start;

        Arrange(column, 100f, 300f);

        Assert.Equal(new FlexRect(0f, 0f, 20f, 50f), a.Rect);
        Assert.Equal(new FlexRect(0f, 50f, 30f, 250f), b.Rect);
    }

    [Fact]
    public void Snapping_spreads_rounding_across_the_line()
    {
        FlexNode a = Leaf(0f, 10f), b = Leaf(0f, 10f), c = Leaf(0f, 10f);
        a.Grow = b.Grow = c.Grow = 1f;

        Arrange(Row(a, b, c), 100f, 10f);

        // Edges 0, 33.33, 66.67, 100 round to 0, 33, 67, 100.
        Assert.Equal(new[] { 33f, 34f, 33f }, new[] { a.Rect.Width, b.Rect.Width, c.Rect.Width });
        Assert.Equal(100f, c.Rect.X + c.Rect.Width);
    }

    [Fact]
    public void Snapping_a_fractional_gap_keeps_item_sizes()
    {
        FlexNode a = Leaf(10f, 10f), b = Leaf(10f, 10f), c = Leaf(10f, 10f);
        FlexNode row = Row(a, b, c);
        row.Gap = 2.5f;

        Arrange(row, 100f, 10f);

        Assert.Equal(new[] { 0f, 13f, 25f }, new[] { a.Rect.X, b.Rect.X, c.Rect.X });
        Assert.Equal(new[] { 10f, 10f, 10f }, new[] { a.Rect.Width, b.Rect.Width, c.Rect.Width });
    }

    [Fact]
    public void Space_between_with_a_remainder_ends_exactly_at_the_edge()
    {
        FlexNode a = Leaf(10f, 10f), b = Leaf(10f, 10f), c = Leaf(10f, 10f), d = Leaf(10f, 10f);
        FlexNode row = Row(a, b, c, d);
        row.Justify = FlexJustify.SpaceBetween;

        Arrange(row, 101f, 10f);

        // Starts 0, 30.33, 60.67, 91 round to 0, 30, 61, 91.
        Assert.Equal(new[] { 0f, 30f, 61f, 91f }, new[] { a.Rect.X, b.Rect.X, c.Rect.X, d.Rect.X });
        Assert.Equal(101f, d.Rect.X + d.Rect.Width);
    }
}
