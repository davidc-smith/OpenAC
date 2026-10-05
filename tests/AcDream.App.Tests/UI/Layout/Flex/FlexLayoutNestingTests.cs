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
}
