using System;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Cost, counted rather than timed: one arrange measures each leaf exactly
/// once however deep the tree, and a repeated arrange of the same tree
/// allocates nothing, so relaying out every frame during a resize is cheap.
/// </summary>
public sealed class FlexLayoutScalingTests
{
    private static readonly FlexMeasurement Icon = new(new FlexSize(32f, 32f), new FlexSize(32f, 32f));

    private static FlexMeasurement MeasureIcon(float? availableWidth) => Icon;

    private static FlexNode Grid(int count, FlexMeasure measure)
    {
        var grid = new FlexNode { Direction = FlexDirection.Row, Wrap = true, Gap = 4f, ScrollY = true };
        for (int i = 0; i < count; i++)
            grid.Children.Add(new FlexNode { Measure = measure });
        return grid;
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Each_leaf_is_measured_once_per_arrange(int count)
    {
        int calls = 0;
        FlexNode grid = Grid(count, _ => { calls++; return Icon; });
        // Nest the grid two containers deep: depth must not multiply the calls.
        var inner = new FlexNode { Direction = FlexDirection.Column };
        inner.Children.Add(grid);
        var root = new FlexNode { Direction = FlexDirection.Row };
        root.Children.Add(inner);

        FlexLayout.Arrange(root, new FlexRect(0f, 0f, 300f, 400f));

        Assert.Equal(count, calls);
    }

    [Fact]
    public void A_repeated_arrange_allocates_nothing()
    {
        FlexNode grid = Grid(200, MeasureIcon);
        // Warm up over the same widths: the narrowest makes the most lines, so the
        // line buffer reaches its final capacity here.
        for (int width = 200; width < 300; width++)
            FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 400f));

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int width = 200; width < 300; width++)
            FlexLayout.Arrange(grid, new FlexRect(0f, 0f, width, 400f));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0L, allocated);
    }
}
