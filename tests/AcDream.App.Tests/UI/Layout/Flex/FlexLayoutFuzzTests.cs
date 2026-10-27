using System;
using AcDream.App.UI.Layout.Flex;

namespace AcDream.App.Tests.UI.Layout.Flex;

/// <summary>
/// Height-for-width at any depth, checked over many random trees: given at
/// least its measured minimum, every container's rect holds the content it
/// lays out, along every axis it does not scroll. The trees are seeded, so a
/// failure names a tree that reproduces every run.
///
/// <para>In each tree the wrapping containers all run one way (rows, or
/// columns), so every size-dependent node depends along the same axis. A
/// wrapping column holding a wrapping row depends on its size both ways,
/// which this engine resolves one way only (see <see cref="FlexLayout"/>);
/// such trees can overflow and are not part of this guarantee. Trees do not
/// scroll: a scrolling item prefers more than its 40-point minimum, and an
/// item that is not stretched keeps its preferred cross size even where
/// that overflows, as in CSS.</para>
/// </summary>
public sealed class FlexLayoutFuzzTests
{
    private const int Seed = 20261005;
    private const int TreeCount = 500;

    /// <summary>
    /// A random container up to <paramref name="depth"/> levels deep. Leaves
    /// prefer their minimum (as v1 controls do), so any overflow comes from
    /// the engine rather than from content asking for more than it needs.
    /// Shrink stays positive: an item that may not shrink is allowed to overflow.
    /// </summary>
    private static FlexNode RandomContainer(Random random, int depth, FlexDirection wrapping)
    {
        var direction = random.Next(2) == 0 ? FlexDirection.Row : FlexDirection.Column;
        var node = new FlexNode
        {
            Direction = direction,
            Wrap = direction == wrapping && random.Next(2) == 0,
            Gap = random.Next(0, 6),
            Padding = FlexEdges.All(random.Next(0, 5)),
            Justify = (FlexJustify)random.Next(4),
            Align = (FlexAlign)random.Next(4),
        };
        RandomItem(random, node);
        int children = random.Next(1, 5);
        for (int i = 0; i < children; i++)
        {
            node.Children.Add(depth > 1 && random.NextDouble() < 0.6
                ? RandomContainer(random, depth - 1, wrapping)
                : RandomLeaf(random));
        }
        return node;
    }

    private static FlexNode RandomLeaf(Random random)
    {
        // Whole points: a fractional item can land a snapped edge up to a point
        // past its container's snapped edge, which is rounding, not sizing.
        float width = random.Next(10, 80);
        float height = random.Next(10, 40);
        var measurement = new FlexMeasurement(new FlexSize(width, height), new FlexSize(width, height));
        var leaf = new FlexNode { Measure = _ => measurement };
        RandomItem(random, leaf);
        return leaf;
    }

    private static void RandomItem(Random random, FlexNode node)
    {
        node.Grow = random.Next(3) == 0 ? random.Next(1, 3) : 0f;
        node.Shrink = random.Next(3) == 0 ? 2f : 1f;
        node.Hidden = random.Next(20) == 0;
    }

    [Fact]
    public void Every_container_holds_its_content_at_any_size_from_its_minimum_up()
    {
        var random = new Random(Seed);
        for (int tree = 0; tree < TreeCount; tree++)
        {
            var wrapping = random.Next(2) == 0 ? FlexDirection.Row : FlexDirection.Column;
            FlexNode root = RandomContainer(random, 3, wrapping);
            root.Hidden = false;
            FlexMeasurement m = FlexLayout.Measure(root);
            float width = m.Minimum.Width + (float)(random.NextDouble() * 200.0);
            float height = m.Minimum.Height + (float)(random.NextDouble() * 200.0);

            FlexLayout.Arrange(root, new FlexRect(0f, 0f, width, height));

            AssertContentFits(root, $"tree {tree} at {width} x {height}: root");
        }
    }

    private static void AssertContentFits(FlexNode node, string path)
    {
        if (node.Measure is not null || node.Hidden) return;
        if (!node.ScrollX)
        {
            Assert.True(node.ContentSize.Width <= node.Rect.Width + 0.5f,
                $"{path}: content width {node.ContentSize.Width} exceeds rect {node.Rect}");
        }
        if (!node.ScrollY)
        {
            Assert.True(node.ContentSize.Height <= node.Rect.Height + 0.5f,
                $"{path}: content height {node.ContentSize.Height} exceeds rect {node.Rect}");
        }
        for (int i = 0; i < node.Children.Count; i++)
            AssertContentFits(node.Children[i], $"{path}/{i}");
    }
}
