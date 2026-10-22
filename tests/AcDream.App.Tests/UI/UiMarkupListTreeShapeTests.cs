using System;
using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupListTreeShapeTests
{
    // A, A1, A2, A2a, B, B1
    private static readonly int[] Levels = { 0, 1, 1, 2, 0, 1 };

    private static UiMarkupListTreeShape Shape(int[]? levels = null, int count = 6)
    {
        var shape = new UiMarkupListTreeShape();
        shape.Load(levels ?? Levels, count);
        return shape;
    }

    private static int[] Order(UiMarkupListViewMap map) =>
        Enumerable.Range(0, map.ViewCount).Select(map.PluginRow).ToArray();

    [Fact]
    public void Load_ClampsLevels()
    {
        var shape = Shape(new[] { 2, 3, 1, -4, 9 }, 6);
        Assert.Equal(new[] { 0, 1, 1, 0, 1, 0 }, Enumerable.Range(0, 6).Select(shape.Level).ToArray());
    }

    [Fact]
    public void Load_DerivesParentsChildrenAndSubtrees()
    {
        var shape = Shape();
        Assert.Equal(new[] { -1, 0, 0, 2, -1, 4 }, Enumerable.Range(0, 6).Select(shape.Parent).ToArray());
        Assert.Equal(new[] { true, false, true, false, true, false },
            Enumerable.Range(0, 6).Select(shape.HasChildren).ToArray());
        Assert.Equal(4, shape.SubtreeEnd(0));
        Assert.Equal(4, shape.SubtreeEnd(2));
        Assert.Equal(6, shape.SubtreeEnd(4));
        Assert.Equal(2, shape.SubtreeEnd(1));
    }

    [Fact]
    public void Load_ClearsCollapsedFlags()
    {
        var shape = Shape();
        shape.SetCollapsed(0, true);
        shape.Load(Levels, 6);
        Assert.False(shape.IsCollapsed(0));
    }

    [Fact]
    public void Identity_LeavesOutCollapsedDescendants()
    {
        var shape = Shape();
        var map = new UiMarkupListViewMap();
        map.SetIdentity(6, shape);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Order(map));

        shape.SetCollapsed(0, true);
        map.SetIdentity(6, shape);
        Assert.Equal(new[] { 0, 4, 5 }, Order(map));
        Assert.Equal((3, 6), (map.ViewCount, map.PluginCount));
        Assert.Equal(-1, map.ViewRow(1));
        Assert.Equal(-1, map.ViewRow(3));
        Assert.Equal(1, map.ViewRow(4));

        shape.SetCollapsed(0, false);
        shape.SetCollapsed(2, true);
        map.SetIdentity(6, shape);
        Assert.Equal(new[] { 0, 1, 2, 4, 5 }, Order(map));
    }

    [Fact]
    public void Sort_SortsSiblingsAndKeepsChildrenUnderTheirParent()
    {
        var map = new UiMarkupListViewMap();
        string?[] names = { "A", "A1", "A2", "A2a", "B", "B1" };
        map.SortByText(names, 6, descending: true, Shape());
        Assert.Equal(new[] { 4, 5, 0, 2, 3, 1 }, Order(map));
        map.SortByText(names, 6, descending: false, Shape());
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, Order(map));
    }

    [Fact]
    public void Sort_IsStableAndPutsMissingKeysLastWithinSiblings()
    {
        var map = new UiMarkupListViewMap();
        // Children of A (rows 1, 2, 3 at level 1): keys NaN, 5, 5.
        int[] levels = { 0, 1, 1, 1 };
        map.SortByNumber(new[] { 0.0, double.NaN, 5.0, 5.0 }, 4, descending: true, Shape(levels, 4));
        Assert.Equal(new[] { 0, 2, 3, 1 }, Order(map));
    }

    [Fact]
    public void Sort_RespectsCollapsedRows()
    {
        var shape = Shape();
        shape.SetCollapsed(4, true);
        var map = new UiMarkupListViewMap();
        map.SortByText(new string?[] { "A", "A1", "A2", "A2a", "B", "B1" }, 6, descending: true, shape);
        Assert.Equal(new[] { 4, 0, 2, 3, 1 }, Order(map));
        Assert.Equal(-1, map.ViewRow(5));
    }

    [Fact]
    public void DeepChain_ArrangesWithoutRecursion()
    {
        int[] levels = Enumerable.Range(0, 2000).ToArray();
        var map = new UiMarkupListViewMap();
        map.SetIdentity(2000, Shape(levels, 2000));
        Assert.Equal(Enumerable.Range(0, 2000).ToArray(), Order(map));
    }
}
