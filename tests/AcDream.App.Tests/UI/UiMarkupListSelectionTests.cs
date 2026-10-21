using System;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupListSelectionTests
{
    private static UiMarkupListViewMap Identity(int count)
    {
        var map = new UiMarkupListViewMap();
        map.SetIdentity(count);
        return map;
    }

    // Keys 30,10,20,40 ascending: view order is plugin rows 1, 2, 0, 3.
    private static UiMarkupListViewMap Sorted()
    {
        var map = new UiMarkupListViewMap();
        map.SortByNumber(new[] { 30.0, 10.0, 20.0, 40.0 }, 4, descending: false);
        return map;
    }

    [Fact]
    public void Click_Plain_SelectsOnlyThatRow() =>
        Assert.Equal(new[] { 2 }, UiMarkupListSelection.Click(new[] { 0, 3 }, 2, false, false, -1, Identity(4)));

    [Fact]
    public void Click_Add_TogglesTheRow()
    {
        var map = Identity(4);
        Assert.Equal(new[] { 0, 2, 3 }, UiMarkupListSelection.Click(new[] { 3, 0 }, 2, true, false, -1, map));
        Assert.Equal(new[] { 0 }, UiMarkupListSelection.Click(new[] { 3, 0 }, 3, true, false, -1, map));
    }

    [Fact]
    public void Click_Range_RunsInViewOrderBothWays()
    {
        var map = Sorted();
        // Anchor plugin 1 (view 0) to plugin 0 (view 2): views 0..2 = plugins 1, 2, 0.
        Assert.Equal(new[] { 0, 1, 2 }, UiMarkupListSelection.Click(new[] { 3 }, 0, false, true, 1, map));
        // Anchor plugin 3 (view 3) up to plugin 0 (view 2): plugins 0, 3, not 0..3.
        Assert.Equal(new[] { 0, 3 }, UiMarkupListSelection.Click(Array.Empty<int>(), 0, false, true, 3, map));
    }

    [Fact]
    public void Click_AddRange_UnionsWithCurrent() =>
        Assert.Equal(new[] { 0, 2, 3 },
            UiMarkupListSelection.Click(new[] { 3 }, 0, true, true, 2, Sorted()));

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void Click_RangeWithoutAnchor_ActsAsClickWithoutShift(int anchor)
    {
        var map = Identity(4);
        Assert.Equal(new[] { 1 }, UiMarkupListSelection.Click(new[] { 3 }, 1, false, true, anchor, map));
        Assert.Equal(new[] { 1, 3 }, UiMarkupListSelection.Click(new[] { 3 }, 1, true, true, anchor, map));
    }

    [Fact]
    public void Click_DropsStaleAndDuplicateRows() =>
        Assert.Equal(new[] { 1, 2 },
            UiMarkupListSelection.Click(new[] { 2, 2, 7, -1 }, 1, true, false, -1, Identity(4)));

    [Fact]
    public void MovesAnchor_OnlyARangeClickWithAValidAnchorKeepsIt()
    {
        Assert.False(UiMarkupListSelection.MovesAnchor(range: true, anchor: 1, count: 4));
        Assert.True(UiMarkupListSelection.MovesAnchor(range: true, anchor: -1, count: 4));
        Assert.True(UiMarkupListSelection.MovesAnchor(range: true, anchor: 4, count: 4));
        Assert.True(UiMarkupListSelection.MovesAnchor(range: false, anchor: 1, count: 4));
    }

    [Fact]
    public void Range_InvalidEnds()
    {
        var map = Sorted();
        Assert.Equal(new[] { 2 }, UiMarkupListSelection.Range(-1, 2, map));
        Assert.Empty(UiMarkupListSelection.Range(1, 9, map));
    }

    [Fact]
    public void All_IsEveryRow()
    {
        Assert.Equal(new[] { 0, 1, 2 }, UiMarkupListSelection.All(3));
        Assert.Empty(UiMarkupListSelection.All(0));
    }

    [Fact]
    public void Step_MovesInViewOrderAndClamps()
    {
        var map = Sorted();                                      // views: 1, 2, 0, 3
        Assert.Equal(0, UiMarkupListSelection.Step(2, 1, map));   // view 1 → view 2
        Assert.Equal(1, UiMarkupListSelection.Step(1, -1, map));  // top stays
        Assert.Equal(3, UiMarkupListSelection.Step(3, 1, map));   // bottom stays
        Assert.Equal(1, UiMarkupListSelection.Step(-1, 1, map));  // no lead → first view row
        Assert.Equal(1, UiMarkupListSelection.Step(8, -1, map));
        Assert.Equal(-1, UiMarkupListSelection.Step(0, 1, Identity(0)));
    }
}
