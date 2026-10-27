using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Measuring: a leaf reports its content, overrides and limits adjust it the
/// way CSS does (min-width:auto never exceeds an explicit size or the
/// maximum), and a container adds up its visible items, gaps and padding.
/// </summary>
public sealed class FlexLayoutMeasureTests
{
    [Fact]
    public void Leaf_reports_its_content_measurement()
    {
        FlexMeasurement m = FlexLayout.Measure(Leaf(120f, 20f, minWidth: 40f));

        Assert.Equal(new FlexSize(120f, 20f), m.Preferred);
        Assert.Equal(new FlexSize(40f, 20f), m.Minimum);
    }

    [Fact]
    public void Explicit_size_overrides_preferred_and_caps_the_automatic_minimum()
    {
        FlexNode leaf = Leaf(120f, 20f, minWidth: 80f);
        leaf.Width = 50f;

        FlexMeasurement m = FlexLayout.Measure(leaf);

        Assert.Equal(new FlexSize(50f, 20f), m.Preferred);
        Assert.Equal(50f, m.Minimum.Width);
    }

    [Fact]
    public void Explicit_minimum_lowers_the_floor_and_maximum_clamps_both()
    {
        FlexNode lowered = Leaf(120f, 20f, minWidth: 80f);
        lowered.MinWidth = 10f;
        FlexNode capped = Leaf(120f, 20f, minWidth: 80f);
        capped.MaxWidth = 60f;

        Assert.Equal(10f, FlexLayout.Measure(lowered).Minimum.Width);
        FlexMeasurement m = FlexLayout.Measure(capped);
        Assert.Equal(60f, m.Preferred.Width);
        Assert.Equal(60f, m.Minimum.Width);
    }

    [Fact]
    public void Row_adds_items_gaps_and_padding()
    {
        FlexNode row = Row(Leaf(50f, 10f, minWidth: 20f), Leaf(30f, 24f), Leaf(40f, 12f, minWidth: 10f));
        row.Gap = 4f;
        row.Padding = new FlexEdges(1f, 2f, 3f, 4f);

        FlexMeasurement m = FlexLayout.Measure(row);

        Assert.Equal(new FlexSize(50f + 30f + 40f + 8f + 6f, 24f + 4f), m.Preferred);
        Assert.Equal(new FlexSize(20f + 30f + 10f + 8f + 6f, 24f + 4f), m.Minimum);
    }

    [Fact]
    public void Column_swaps_the_axes()
    {
        FlexNode column = Column(Leaf(50f, 10f), Leaf(30f, 24f, minWidth: 5f, minHeight: 6f));
        column.Gap = 2f;

        FlexMeasurement m = FlexLayout.Measure(column);

        Assert.Equal(new FlexSize(50f, 36f), m.Preferred);
        Assert.Equal(new FlexSize(50f, 18f), m.Minimum);
    }

    [Fact]
    public void Hidden_items_are_not_counted()
    {
        FlexNode hidden = Leaf(500f, 500f);
        hidden.Hidden = true;
        FlexNode row = Row(Leaf(50f, 10f), hidden, Leaf(30f, 10f));
        row.Gap = 4f;

        Assert.Equal(new FlexSize(84f, 10f), FlexLayout.Measure(row).Preferred);
    }

    [Fact]
    public void Wrapping_minimum_is_the_widest_item_and_the_lines_it_makes()
    {
        // At 40 wide: [40], [20 + 4 + 10], [30] -> three lines of 10, 12 and 8 high.
        FlexNode row = Row(Leaf(40f, 10f), Leaf(20f, 12f), Leaf(10f, 6f), Leaf(30f, 8f));
        row.Wrap = true;
        row.Gap = 4f;

        FlexMeasurement m = FlexLayout.Measure(row);

        Assert.Equal(new FlexSize(40f + 20f + 10f + 30f + 12f, 12f), m.Preferred);
        Assert.Equal(new FlexSize(40f, 10f + 12f + 8f + 8f), m.Minimum);
    }

    [Fact]
    public void Preferred_size_adds_each_item_clamped_to_its_limits()
    {
        FlexNode capped = Leaf(200f, 10f);
        capped.MaxWidth = 100f;
        FlexNode row = Row(capped, Leaf(50f, 10f));

        // The capped item contributes its 100 maximum, not its 200 content.
        Assert.Equal(150f, FlexLayout.Measure(row).Preferred.Width);
    }

    [Fact]
    public void A_basis_below_the_content_minimum_counts_as_the_minimum_so_the_preferred_size_shrinks_nothing()
    {
        FlexNode b = Leaf(50f, 10f);
        b.Basis = 10f;
        FlexNode c = Leaf(100f, 10f, minWidth: 20f);
        FlexNode row = Row(b, c);

        FlexMeasurement m = FlexLayout.Measure(row);
        Arrange(row, m.Preferred.Width, 10f);

        // b's basis 10 is lifted to its 50 content minimum, as its hypothetical
        // size is: 50 + 100 = 150. Arranged at 150 nothing has to shrink.
        Assert.Equal(150f, m.Preferred.Width);
        Assert.Equal(new FlexRect(0f, 0f, 50f, 10f), b.Rect);
        Assert.Equal(new FlexRect(50f, 0f, 100f, 10f), c.Rect);
    }

    [Theory]
    [InlineData(false, true, 316f, 40f, 316f, 400f)]
    [InlineData(true, false, 40f, 416f, 300f, 416f)]
    [InlineData(true, true, 56f, 56f, 300f, 400f)]
    public void Scrolling_axis_needs_only_a_small_viewport_and_its_bar_takes_the_other_axis(
        bool scrollX, bool scrollY, float minWidth, float minHeight, float preferredWidth, float preferredHeight)
    {
        // Content is 300 x 400 (two 300 x 200 items stacked). A preferred size is never below the minimum.
        FlexNode column = Column(Leaf(300f, 200f), Leaf(300f, 200f));
        column.ScrollX = scrollX;
        column.ScrollY = scrollY;

        FlexMeasurement m = FlexLayout.Measure(column);

        Assert.Equal(new FlexSize(minWidth, minHeight), m.Minimum);
        Assert.Equal(new FlexSize(preferredWidth, preferredHeight), m.Preferred);
    }
}
