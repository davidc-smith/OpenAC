using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class UiMarkupListViewMapTests
{
    private static int[] Order(UiMarkupListViewMap map) =>
        Enumerable.Range(0, map.Count).Select(map.PluginRow).ToArray();

    [Fact]
    public void IdentityMapsEachRowToItself()
    {
        var map = new UiMarkupListViewMap();
        map.SetIdentity(3);
        Assert.Equal(new[] { 0, 1, 2 }, Order(map));
        Assert.Equal(2, map.ViewRow(2));
        Assert.Equal(-1, map.PluginRow(3));
        Assert.Equal(-1, map.PluginRow(-1));
        Assert.Equal(-1, map.ViewRow(3));
    }

    [Fact]
    public void TextSortIgnoresCaseAndPutsMissingKeysLast()
    {
        var map = new UiMarkupListViewMap();
        string?[] keys = { "mosswart", "Drudge", null, "rat", "Banderling" };
        map.SortByText(keys, 5, descending: false);
        Assert.Equal(new[] { 4, 1, 0, 3, 2 }, Order(map));
        map.SortByText(keys, 5, descending: true);
        Assert.Equal(new[] { 3, 0, 1, 4, 2 }, Order(map));
    }

    [Fact]
    public void NumberSortPutsNaNLastBothWays()
    {
        var map = new UiMarkupListViewMap();
        double[] keys = { 10, double.NaN, 2, 33 };
        map.SortByNumber(keys, 4, descending: false);
        Assert.Equal(new[] { 2, 0, 3, 1 }, Order(map));
        map.SortByNumber(keys, 4, descending: true);
        Assert.Equal(new[] { 3, 0, 2, 1 }, Order(map));
    }

    [Fact]
    public void TiesKeepThePluginOrderBothWays()
    {
        var map = new UiMarkupListViewMap();
        double[] keys = { 1, 0, 1, 0 };
        map.SortByNumber(keys, 4, descending: false);
        Assert.Equal(new[] { 1, 3, 0, 2 }, Order(map));
        map.SortByNumber(keys, 4, descending: true);
        Assert.Equal(new[] { 0, 2, 1, 3 }, Order(map));
    }

    [Fact]
    public void ViewRowInvertsPluginRow()
    {
        var map = new UiMarkupListViewMap();
        map.SortByNumber(new double[] { 5, 3, 9, 1 }, 4, descending: false);
        for (int view = 0; view < map.Count; view++)
            Assert.Equal(view, map.ViewRow(map.PluginRow(view)));
    }

    [Fact]
    public void VersionMovesOnlyWhenTheOrderChanges()
    {
        var map = new UiMarkupListViewMap();
        map.SetIdentity(3);
        int v0 = map.Version;
        map.SetIdentity(4);
        Assert.Equal(v0, map.Version);                 // identity growing is not a reorder

        double[] keys = { 3, 1, 2, 0 };
        map.SortByNumber(keys, 4, descending: false);
        int v1 = map.Version;
        Assert.NotEqual(v0, v1);
        map.SortByNumber(keys, 4, descending: false);
        Assert.Equal(v1, map.Version);                 // same keys, same order

        keys[0] = -1;
        map.SortByNumber(keys, 4, descending: false);
        int v2 = map.Version;
        Assert.NotEqual(v1, v2);

        map.SortByNumber(keys, 3, descending: false);
        int v3 = map.Version;
        Assert.NotEqual(v2, v3);                       // a sorted map that lost a row

        map.SetIdentity(3);
        Assert.NotEqual(v3, map.Version);              // the sort was cleared
    }
}
