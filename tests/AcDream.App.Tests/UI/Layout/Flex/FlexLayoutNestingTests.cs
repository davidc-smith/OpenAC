using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Trees: a container is measured from its items and then laid out inside
/// the rect its own parent gave it; a leaf's children are its own business;
/// and bad numbers never make the layout throw.
/// </summary>
public sealed class FlexLayoutNestingTests
{
    /// <summary>The spec's example window: a toolbar over a growing list over an OK/Cancel footer.</summary>
    private static (FlexNode root, FlexNode header, FlexNode field, FlexNode refresh, FlexNode list,
        FlexNode footer, FlexNode cancel, FlexNode ok) ToolbarListFooter()
    {
        FlexNode field = Leaf(120f, 20f, minWidth: 40f);
        field.Grow = 1f;
        FlexNode refresh = Leaf(70f, 24f);
        FlexNode header = Row(field, refresh);
        header.Gap = 4f;

        FlexNode list = Leaf(160f, 80f, minWidth: 60f, minHeight: 40f);
        list.Grow = 1f;

        FlexNode cancel = Leaf(70f, 24f), ok = Leaf(70f, 24f);
        FlexNode footer = Row(cancel, ok);
        footer.Gap = 4f;
        footer.Justify = FlexJustify.End;

        FlexNode root = Column(header, list, footer);
        root.Padding = FlexEdges.All(8f);
        root.Gap = 6f;
        return (root, header, field, refresh, list, footer, cancel, ok);
    }

    [Fact]
    public void Nested_containers_lay_out_inside_the_rect_their_parent_gave_them()
    {
        var w = ToolbarListFooter();

        Arrange(w.root, 360f, 260f);

        // Column content 344 x 244; 24 + 80 + 24 + 12 = 140 used, the list takes the other 104.
        Assert.Equal(new FlexRect(8f, 8f, 344f, 24f), w.header.Rect);
        Assert.Equal(new FlexRect(8f, 38f, 344f, 184f), w.list.Rect);
        Assert.Equal(new FlexRect(8f, 228f, 344f, 24f), w.footer.Rect);
        // Inside the header: the field grows by 344 - 120 - 70 - 4 = 150 and stretches to 24 high.
        Assert.Equal(new FlexRect(0f, 0f, 270f, 24f), w.field.Rect);
        Assert.Equal(new FlexRect(274f, 0f, 70f, 24f), w.refresh.Rect);
        // Inside the footer: justify end.
        Assert.Equal(new FlexRect(200f, 0f, 70f, 24f), w.cancel.Rect);
        Assert.Equal(new FlexRect(274f, 0f, 70f, 24f), w.ok.Rect);
    }

    [Fact]
    public void Nested_minimums_add_up_to_the_window_minimum()
    {
        var w = ToolbarListFooter();

        FlexMeasurement m = FlexLayout.Measure(w.root);

        // Widest minimum is the footer, 70 + 4 + 70; heights 24 + 40 + 24 + two gaps.
        Assert.Equal(new FlexSize(144f + 16f, 100f + 16f), m.Minimum);
    }

    private static (FlexNode root, FlexNode toolbar, FlexNode list, FlexNode lastButton) WrappingToolbarOverList()
    {
        FlexNode toolbar = Row(Leaf(60f, 20f), Leaf(60f, 20f), Leaf(60f, 20f), Leaf(60f, 20f));
        toolbar.Wrap = true;
        toolbar.Gap = 4f;
        FlexNode list = Leaf(160f, 80f, minWidth: 60f, minHeight: 40f);
        list.Grow = 1f;
        FlexNode root = Column(toolbar, list);
        return (root, toolbar, list, toolbar.Children[3]);
    }

    [Theory]
    [InlineData(300f, 20f, 0f)]   // 4 x 60 + 3 x 4 = 252 fits on one line
    [InlineData(140f, 44f, 24f)]  // two per line: two lines and a gap
    [InlineData(60f, 92f, 72f)]   // one per line
    public void A_wrapping_row_in_a_column_is_as_tall_as_the_lines_it_makes_at_its_width(
        float width, float toolbarHeight, float lastButtonY)
    {
        var w = WrappingToolbarOverList();

        Arrange(w.root, width, 400f);

        Assert.Equal(new FlexRect(0f, 0f, width, toolbarHeight), w.toolbar.Rect);
        Assert.Equal(new FlexRect(0f, toolbarHeight, width, 400f - toolbarHeight), w.list.Rect);
        Assert.Equal(lastButtonY, w.lastButton.Rect.Y);
    }

    private static FlexNode WrappingToolbar(float itemWidth = 60f)
    {
        FlexNode toolbar = Row(Leaf(itemWidth, 20f), Leaf(itemWidth, 20f), Leaf(itemWidth, 20f), Leaf(itemWidth, 20f));
        toolbar.Wrap = true;
        toolbar.Gap = 4f;
        return toolbar;
    }

    [Fact]
    public void A_wrapping_row_in_a_start_aligned_column_is_no_wider_than_the_column()
    {
        FlexNode toolbar = WrappingToolbar();
        FlexNode root = Column(toolbar);
        root.Align = FlexAlign.Start;

        Arrange(root, 140f, 400f);

        // Its preferred 252 is capped at the column's 140; at 140 it makes two
        // lines of two (60 + 4 + 60 = 124), 20 + 4 + 20 = 44 high.
        Assert.Equal(new FlexRect(0f, 0f, 140f, 44f), toolbar.Rect);
    }

    [Fact]
    public void A_wrapping_row_stretched_in_a_row_takes_the_line_height_it_is_given()
    {
        FlexNode toolbar = WrappingToolbar();

        Arrange(Row(toolbar), 300f, 50f);

        // At its 252 width it makes one 20-high line, so the stretch floor is
        // 20, not its narrowest four lines (92): it fills the 50-high line.
        Assert.Equal(new FlexRect(0f, 0f, 252f, 50f), toolbar.Rect);
    }

    [Fact]
    public void A_wrapping_row_shrunk_in_a_row_is_as_tall_as_the_lines_it_makes()
    {
        FlexNode toolbar = WrappingToolbar();
        FlexNode leaf = Leaf(40f, 20f);
        FlexNode root = Row(toolbar, leaf);
        root.Align = FlexAlign.Start;

        Arrange(root, 150f, 200f);

        // 252 + 40 = 292 overflows 150 by 142. Shrink by basis would take the
        // leaf to 40 - 142 * 40/292 = 20.55, under its 40 minimum, so it
        // freezes and the toolbar gets 150 - 40 = 110. At 110 two items
        // (124) no longer fit: four lines, 4 x 20 + 3 x 4 = 92 high.
        Assert.Equal(new FlexRect(0f, 0f, 110f, 92f), toolbar.Rect);
        Assert.Equal(new FlexRect(110f, 0f, 40f, 20f), leaf.Rect);
    }

    [Fact]
    public void A_wrapping_column_in_a_wrapping_row_that_makes_one_line_keeps_the_height_its_lines_were_made_for()
    {
        FlexNode column = Column(Leaf(30f, 20f), Leaf(30f, 20f), Leaf(30f, 20f), Leaf(30f, 20f));
        column.Wrap = true;
        FlexNode root = Row(column);
        root.Wrap = true;

        Arrange(root, 300f, 50f);

        // Its preferred 80 height is capped at the row's 50; at 50 it makes two
        // columns of two (20 + 20 = 40), 30 + 30 = 60 wide. The single line
        // must not stretch it back to 80.
        Assert.Equal(new FlexRect(0f, 0f, 60f, 50f), column.Rect);
    }

    /// <summary>A wrapping row holding the wrapping toolbar and a 40 x 20 leaf.</summary>
    private static (FlexNode outer, FlexNode toolbar, FlexNode leaf) WrapInWrap()
    {
        FlexNode toolbar = WrappingToolbar();
        FlexNode leaf = Leaf(40f, 20f);
        FlexNode outer = Row(toolbar, leaf);
        outer.Wrap = true;
        return (outer, toolbar, leaf);
    }

    [Fact]
    public void A_wrapping_row_inside_a_wrapping_row_is_as_tall_as_the_lines_its_items_make_at_their_real_sizes()
    {
        var w = WrapInWrap();

        Arrange(Column(w.outer), 150f, 400f);

        // The outer row is 150 wide: the toolbar (252) and the leaf do not share
        // a line. On its own line the toolbar shrinks to 150, where it makes two
        // lines of two (60 + 4 + 60 = 124): 44 high. The leaf's line is 20, so
        // the outer row is 44 + 20 = 64 high, not the 20 + 20 its items'
        // one-line heights would give.
        Assert.Equal(new FlexRect(0f, 0f, 150f, 64f), w.outer.Rect);
        Assert.Equal(new FlexSize(150f, 64f), w.outer.ContentSize);
        Assert.Equal(new FlexRect(0f, 0f, 150f, 44f), w.toolbar.Rect);
        Assert.Equal(new FlexRect(0f, 44f, 40f, 20f), w.leaf.Rect);
    }

    [Fact]
    public void A_padded_column_around_a_wrapping_row_is_as_tall_as_the_lines_at_its_width()
    {
        FlexNode toolbar = WrappingToolbar();
        FlexNode card = Column(toolbar);
        card.Padding = FlexEdges.All(8f);
        FlexNode list = Leaf(160f, 80f, minWidth: 60f, minHeight: 40f);
        list.Grow = 1f;
        FlexNode root = Column(card, list);

        Arrange(root, 140f, 400f);

        // Inside its padding the card is 124 wide, where the toolbar makes two
        // lines of two (124 fits exactly): 44 high, so the card is 44 + 16 = 60,
        // not its narrowest-lines minimum of 92 + 16. The list takes the rest.
        Assert.Equal(new FlexRect(0f, 0f, 140f, 60f), card.Rect);
        Assert.Equal(new FlexRect(8f, 8f, 124f, 44f), toolbar.Rect);
        Assert.Equal(new FlexRect(0f, 60f, 140f, 340f), list.Rect);
    }

    [Fact]
    public void A_column_around_a_wrapping_row_shrunk_in_a_row_is_as_tall_as_the_lines_at_its_width()
    {
        FlexNode toolbar = WrappingToolbar();
        FlexNode column = Column(toolbar);
        FlexNode root = Row(column);
        root.Align = FlexAlign.Start;

        Arrange(root, 150f, 200f);

        // The column's preferred 252 shrinks to the row's 150. At 150 the
        // toolbar makes two lines of two (124; three would be 188): 44 high.
        Assert.Equal(new FlexRect(0f, 0f, 150f, 44f), column.Rect);
        Assert.Equal(new FlexRect(0f, 0f, 150f, 44f), toolbar.Rect);
    }

    /// <summary>
    /// A wrapping column whose greedy lines are not monotonic in its height:
    /// at 117 inside its padding it makes [78, 12] [22, 34] (39 + 3 + 77 wide),
    /// at 118 it makes [78, 12, 22] [34] (47 + 3 + 77 wide).
    /// </summary>
    private static (FlexNode root, FlexNode column) NonMonotonicWrappingColumn()
    {
        FlexNode column = Column(Leaf(39f, 78f), Leaf(32f, 12f), Leaf(47f, 22f), Leaf(77f, 34f));
        column.Wrap = true;
        column.Gap = 3f;
        column.Padding = FlexEdges.All(1f);
        column.Grow = 1f;
        FlexNode root = Column(column);
        root.Align = FlexAlign.Start;
        return (root, column);
    }

    [Fact]
    public void A_wrapping_container_is_sized_for_the_lines_it_makes_at_either_snapped_size()
    {
        var w = NonMonotonicWrappingColumn();

        Arrange(w.root, 300f, 119.6f);

        // Its preferred 146 + 9 + 2 = 157 shrinks to 119.6, which snaps to 119
        // or 120. At 119 (117 inside) its lines are 39 + 3 + 77 + 2 = 121 wide,
        // at 120 (118 inside) 47 + 3 + 77 + 2 = 129: it takes the wider. The
        // rect snaps to 120, where the content is 129 x (1 + 78 + 3 + 12 + 3 + 22 + 1).
        Assert.Equal(new FlexRect(0f, 0f, 129f, 120f), w.column.Rect);
        Assert.Equal(new FlexSize(129f, 120f), w.column.ContentSize);
    }

    /// <summary>Trees whose wrapping containers are sized from their lines; see the theory below.</summary>
    private static (FlexNode root, float width, float height) WrappingTree(string name)
    {
        switch (name)
        {
            case "fractional-column":
            {
                // 60.1 + 4 + 60.1 = 124.2 fits 124.3 but not the 124 the rect snaps to.
                FlexNode root = Column(WrappingToolbar(60.1f), Leaf(40f, 30f));
                return (root, 124.3f, 400f);
            }
            case "one-line-wrapping-parent":
            {
                FlexNode column = Column(Leaf(30f, 20f), Leaf(30f, 20f), Leaf(30f, 20f), Leaf(30f, 20f));
                column.Wrap = true;
                FlexNode root = Row(column, Leaf(40f, 10f));
                root.Wrap = true;
                return (root, 300f, 50f);
            }
            case "start-aligned-column":
            {
                FlexNode root = Column(WrappingToolbar());
                root.Align = FlexAlign.Start;
                return (root, 140f, 400f);
            }
            case "stretched-in-row":
                return (Row(WrappingToolbar()), 300f, 50f);
            case "shrunk-in-row":
            {
                FlexNode root = Row(WrappingToolbar(), Leaf(40f, 20f));
                root.Align = FlexAlign.Start;
                return (root, 150f, 200f);
            }
            case "wrap-in-wrap":
                return (Column(WrapInWrap().outer), 150f, 400f);
            case "padded-column-in-column":
            {
                FlexNode card = Column(WrappingToolbar());
                card.Padding = FlexEdges.All(8f);
                FlexNode list = Leaf(160f, 80f, minWidth: 60f, minHeight: 40f);
                list.Grow = 1f;
                return (Column(card, list), 140f, 400f);
            }
            case "column-shrunk-in-row":
            {
                FlexNode root = Row(Column(WrappingToolbar()));
                root.Align = FlexAlign.Start;
                return (root, 150f, 200f);
            }
            case "non-monotonic-lines":
                return (NonMonotonicWrappingColumn().root, 300f, 119.6f);
            default:
                throw new System.ArgumentException(name, nameof(name));
        }
    }

    [Theory]
    [InlineData("fractional-column")]
    [InlineData("one-line-wrapping-parent")]
    [InlineData("start-aligned-column")]
    [InlineData("stretched-in-row")]
    [InlineData("shrunk-in-row")]
    [InlineData("wrap-in-wrap")]
    [InlineData("padded-column-in-column")]
    [InlineData("column-shrunk-in-row")]
    [InlineData("non-monotonic-lines")]
    public void Every_container_holds_the_lines_it_makes_inside_its_rect(string name)
    {
        var (root, width, height) = WrappingTree(name);

        Arrange(root, width, height);

        AssertContentFits(root);
    }

    private static void AssertContentFits(FlexNode node)
    {
        if (node.Measure is null)
        {
            Assert.True(node.ContentSize.Width <= node.Rect.Width + 0.5f,
                $"content width {node.ContentSize.Width} exceeds rect {node.Rect}");
            Assert.True(node.ContentSize.Height <= node.Rect.Height + 0.5f,
                $"content height {node.ContentSize.Height} exceeds rect {node.Rect}");
        }
        if (node.Measure is not null) return;
        for (int i = 0; i < node.Children.Count; i++) AssertContentFits(node.Children[i]);
    }

    [Fact]
    public void A_wrapping_row_reports_its_narrowest_lines_as_its_minimum()
    {
        var w = WrappingToolbarOverList();

        FlexMeasurement m = FlexLayout.Measure(w.root);

        // At 60 wide the toolbar makes four lines: 4 x 20 + 3 x 4 = 92, over a 40-high list.
        Assert.Equal(new FlexSize(60f, 92f + 40f), m.Minimum);
        Assert.Equal(20f, w.toolbar.Measured.Preferred.Height);
    }

    [Fact]
    public void A_leaf_with_children_is_not_laid_out_by_the_engine()
    {
        FlexNode inner = Leaf(10f, 10f);
        FlexNode absoluteGroup = Leaf(100f, 50f);
        absoluteGroup.Children.Add(inner);
        inner.Rect = new FlexRect(3f, 4f, 5f, 6f);

        Arrange(Row(absoluteGroup), 300f, 50f);

        Assert.Equal(new FlexRect(0f, 0f, 100f, 50f), absoluteGroup.Rect);
        Assert.Equal(new FlexRect(3f, 4f, 5f, 6f), inner.Rect);
    }

    [Fact]
    public void Non_finite_and_negative_numbers_clamp_to_zero_instead_of_throwing()
    {
        FlexNode nanLeaf = new()
        {
            Measure = _ => new FlexMeasurement(new FlexSize(float.NaN, -5f), new FlexSize(float.PositiveInfinity, float.NaN)),
        };
        FlexNode badFactors = Leaf(20f, 10f);
        badFactors.Grow = float.NaN;
        badFactors.Shrink = -1f;
        FlexNode row = Row(nanLeaf, badFactors);
        row.Gap = float.NaN;

        Arrange(row, 100f, float.NaN);

        Assert.Equal(new FlexRect(0f, 0f, 0f, 0f), nanLeaf.Rect);
        Assert.Equal(new FlexRect(0f, 0f, 20f, 10f), badFactors.Rect); // never squashed below its content height
    }

    [Fact]
    public void Content_size_reaches_the_furthest_item_plus_trailing_padding()
    {
        FlexNode a = Leaf(30f, 10f), b = Leaf(40f, 20f);
        FlexNode row = Row(a, b);
        row.Padding = new FlexEdges(1f, 2f, 3f, 4f);
        row.Align = FlexAlign.Start;

        Arrange(row, 200f, 100f);

        Assert.Equal(new FlexSize(4f + 70f + 2f, 1f + 20f + 3f), row.ContentSize);
    }

    [Fact]
    public void An_empty_container_reaches_across_its_padding()
    {
        FlexNode row = Row();
        row.Padding = FlexEdges.All(8f);

        Arrange(row, 100f, 100f);

        // No items: the leading 8 plus the trailing 8 on each axis.
        Assert.Equal(new FlexSize(16f, 16f), row.ContentSize);
    }
}
