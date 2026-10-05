using AcDream.App.UI.Layout.Flex;
using static AcDream.App.Tests.UI.Layout.Flex.FlexTestNodes;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// The fitted minimum: the measured minimum, grown until the content laid out
/// at it no longer reaches past it. Exact where the measurement is; larger
/// where wrapped lines break on preferred sizes.
/// </summary>
public sealed class FlexFitTests
{
    [Fact]
    public void A_plain_column_fits_its_measured_minimum()
    {
        FlexNode root = Column(Leaf(80f, 20f, minWidth: 30f), Leaf(50f, 30f));
        root.Gap = 4f;
        root.Padding = FlexEdges.All(5f);

        Assert.Equal(FlexLayout.Measure(root).Minimum, FlexFit.Minimum(root));
        Assert.Equal(new FlexSize(60f, 64f), FlexFit.Minimum(root));
    }

    [Fact]
    public void Wrapped_lines_that_break_on_a_basis_grow_the_minimum()
    {
        // At its narrowest (60, the widest item) the measured minimum breaks
        // lines on the items' minimums: A and B (10 each) share a line, C
        // takes one, 20 tall. Laid out, A and B start at their basis (50) and
        // cannot share 60, so there are three lines: 30 tall.
        FlexNode a = Leaf(10f, 10f), b = Leaf(10f, 10f), c = Leaf(60f, 10f);
        a.Basis = 50f;
        b.Basis = 50f;
        FlexNode root = Row(a, b, c);
        root.Wrap = true;

        Assert.Equal(new FlexSize(60f, 20f), FlexLayout.Measure(root).Minimum);
        Assert.Equal(new FlexSize(60f, 30f), FlexFit.Minimum(root));
    }

    [Fact]
    public void A_scrolling_axis_is_never_grown()
    {
        FlexNode root = Column(Leaf(50f, 100f), Leaf(50f, 100f));
        root.ScrollY = true;

        FlexSize fitted = FlexFit.Minimum(root);

        Assert.Equal(FlexLayout.MinimumScrollViewport, fitted.Height);
        Assert.Equal(50f + FlexLayout.ScrollbarThickness, fitted.Width);
    }

    [Fact]
    public void Fractional_minimums_round_up_to_whole_points()
    {
        FlexNode root = Row(Leaf(10.25f, 7.5f), Leaf(10.25f, 7.5f));

        Assert.Equal(new FlexSize(21f, 8f), FlexFit.Minimum(root));
    }
}
