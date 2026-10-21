using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListHeaderTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"list-header-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_settingsPath)) File.Delete(_settingsPath);
    }

    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "mosswart", "Drudge", "rat", "Banderling" };
        public List<string> Dists { get; set; } = new() { "12.5 m", "3 m", "40 m", "7 m" };
        public List<double> DistKeys { get; set; } = new() { 12.5, 3, 40, 7 };
        public List<string> Levels { get; set; } = new() { "10", "x", "2", "33" };
        public List<bool> On { get; set; } = new() { true, false, true, false };
        public List<string> Acts { get; set; } = new() { "Go", "Go", "Go", "Go" };
        public List<uint> Ids { get; } = new() { 1, 2, 3, 4 };
        public string DistTitle { get; set; } = "Dist";
        public int Selected { get; set; } = -1;
        public List<int> Picks { get; } = new();
        public Action<int> Pick => row => { Selected = row; Picks.Add(row); };
        public List<int> Toggled { get; } = new();
        public Action<int> Toggle => row => Toggled.Add(row);
        public List<int> Acted { get; } = new();
        public Action<int> Act => row => Acted.Add(row);
        public int SortCol { get; set; } = -1;
        public bool SortDesc { get; set; }
        public List<(int, bool)> Sorts { get; } = new();
        public Action<int, bool> SetSort => (c, d) => { SortCol = c; SortDesc = d; Sorts.Add((c, d)); };
        public Action<int, bool> NotifySort => (c, d) => Sorts.Add((c, d));
        public List<float> Widths { get; set; } = new() { 120f, 80f, 40f, 0f };
        public List<IReadOnlyList<float>> Resizes { get; } = new();
        public Action<IReadOnlyList<float>> SetWidths => w => { Widths = w.ToList(); Resizes.Add(w); };
        public Action<IReadOnlyList<float>> NotifyWidths => w => Resizes.Add(w);
    }

    // Columns: Name 0-120, Dist 120-200, On 200-240, button 240-300.
    // Header y 0-18; view row v spans y 18+18v .. 36+18v.
    private const string ColumnsXml = """
          <column type="text" header="Name" width="120" items="{Names}" sort="text"/>
          <column type="text" header="{DistTitle}" width="80" items="{Dists}" sort="number" sortkeys="{DistKeys}"/>
          <column type="checkbox" header="On" width="40" values="{On}" onchange="{Toggle}" sort="bool"/>
          <column type="button" items="{Acts}" onclick="{Act}"/>
        """;

    // header="true" unless the test sets header itself (a repeated attribute is invalid XML).
    private static string Markup(string attributes, string columns = ColumnsXml, string extraPanel = "")
    {
        string header = attributes.Contains("header=") ? "" : "header=\"true\"";
        return $"""
            <panel x="0" y="0" w="400" h="300" {extraPanel}>
              <list name="mobs" x="0" y="0" w="300" h="108" {header} selected="{"{Selected}"}" onchange="{"{Pick}"}" {attributes}>
            {columns}
              </list>
            </panel>
            """;
    }

    /// <summary>Every quad drawn with a texture, as (left, top, right, bottom).</summary>
    private static List<(float X, float Y, float R, float B)> SpriteQuads(
        AcDream.App.Rendering.TextRenderer renderer, uint texture) =>
        renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == texture)
            .SelectMany(s => Enumerable.Range(0, s.Verts.Count / 48)
                .Select(q => (s.Verts[q * 48], s.Verts[q * 48 + 1], s.Verts[q * 48 + 8], s.Verts[q * 48 + 9])))
            .ToList();

    private static (UiRoot Root, UiMarkupList List) Mount(
        string attributes, Binding binding, IMarkupControlState? state = null, string columns = ColumnsXml,
        PluginUiThemeSettings? themes = null)
    {
        var panel = MarkupDocument.Build(
            Markup(attributes, columns, themes is null ? "" : "theme=\"plugin\""),
            binding, id => (id, 16, 16), controlState: state, themes: themes);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        MarkupDrawProbe.Draw(panel, 800f, 600f);   // lists lay out and read rows at draw
        return (root, list);
    }

    private static void Redraw(UiMarkupList list) => MarkupDrawProbe.Draw(list.Parent!, 800f, 600f);

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void ClickAndDraw(UiRoot root, UiMarkupList list, int x, int y)
    {
        Click(root, x, y);
        Redraw(list);
    }

    /// <summary>The plugin row picked by clicking the Name cell of each view row in turn.</summary>
    private static int[] ViewOrder(UiRoot root, UiMarkupList list, Binding b)
    {
        var order = new List<int>();
        for (int view = 0; view < 4; view++)
        {
            b.Picks.Clear();
            Click(root, 60, 27 + 18 * view);
            order.Add(Assert.Single(b.Picks));
        }
        Redraw(list);
        return order.ToArray();
    }

    [Fact]
    public void Header_RowsStartBelowItAndHeaderPressesSelectNothing()
    {
        var b = new Binding();
        var (root, _) = Mount("", b);
        Click(root, 270, 9);   // the unsortable button header: a sortable one would reorder the rows
        Assert.Empty(b.Picks);
        Assert.Empty(b.Acted);
        Click(root, 60, 27);
        Assert.Equal(new[] { 0 }, b.Picks);
        Click(root, 270, 27);
        Assert.Equal(new[] { 0 }, b.Acted);
    }

    [Fact]
    public void Header_ScrollbarStartsBelowIt()
    {
        var b = new Binding { Names = Enumerable.Range(0, 8).Select(i => $"m{i}").ToList() };
        var (_, list) = Mount("", b);
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Contains((284f, 18f, 300f, 108f), SpriteQuads(renderer, RetailScrollbarChrome.Track));
    }

    [Fact]
    public void Header_ClassicFaceIsTheTrackArtWithAnUnderlineAndSeparators()
    {
        var (_, list) = Mount("", new Binding());
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Equal(new[] { (0f, 0f, 300f, 18f) }, SpriteQuads(renderer, RetailScrollbarChrome.Track));
        var lines = MarkupDrawProbe.QuadBounds(renderer, list.BorderColor);
        Assert.Contains((0f, 17f, 300f, 1f), lines);
        foreach (float x in new[] { 119f, 199f, 239f })
            Assert.Contains((x, 2f, 1f, 14f), lines);
    }

    [Fact]
    public void Header_PluginThemeUsesTheFieldAndBorderTokens()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var (_, list) = Mount("", new Binding(), themes: themes);
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Contains((0f, 0f, 300f, 18f), MarkupDrawProbe.QuadBounds(renderer, PluginUiPalette.Moss.Field));
        Assert.Contains((0f, 17f, 300f, 1f), MarkupDrawProbe.QuadBounds(renderer, PluginUiPalette.Moss.Border));
        Assert.Empty(SpriteQuads(renderer, RetailScrollbarChrome.Track));
    }

    [Fact]
    public void Header_CaptionsAreLiteralOrBound()
    {
        var b = new Binding();
        var (_, list) = Mount("", b);
        Assert.Equal("Name", list.Columns![0].HeaderSource!());
        Assert.Equal("Dist", list.Columns[1].HeaderSource!());
        b.DistTitle = "Range";
        Assert.Equal("Range", list.Columns[1].HeaderSource!());
        Assert.Null(list.Columns[3].HeaderSource);
    }

    [Fact]
    public void Header_OffLeavesTheListAsBefore()
    {
        var b = new Binding();
        var (root, _) = Mount("header=\"false\"", b);
        Click(root, 60, 9);
        Assert.Equal(new[] { 0 }, b.Picks);
    }

    [Theory]
    [InlineData("header=\"maybe\"")]
    public void Header_BadValueThrows(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }

    [Theory]
    [InlineData("header=\"true\"")]
    [InlineData("resizable=\"true\"")]
    [InlineData("sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\"")]
    [InlineData("onsort=\"{SetSort}\"")]
    [InlineData("widths=\"{Widths}\"")]
    [InlineData("onresize=\"{SetWidths}\"")]
    public void Header_AttributesOnASingleColumnListThrow(string attributes)
    {
        string xml = $"""
            <panel x="0" y="0" w="400" h="300">
              <list x="0" y="0" w="300" h="108" items="{"{Names}"}" selected="{"{Selected}"}" {attributes}/>
            </panel>
            """;
        Assert.Throws<FormatException>(() => MarkupDocument.Build(xml, new Binding(), id => (id, 16, 16)));
    }

    private const int NameX = 60, DistX = 160, OnX = 220, ButtonX = 270, HeaderY = 9;

    [Fact]
    public void Sort_NameCyclesAscendingDescendingNone()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 3, 1, 0, 2 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 2, 0, 1, 3 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_SortKeysColumn()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, DistX, HeaderY);
        Assert.Equal(new[] { 1, 3, 0, 2 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_BoolColumnIsStableBothWays()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, OnX, HeaderY);
        Assert.Equal(new[] { 1, 3, 0, 2 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, OnX, HeaderY);
        Assert.Equal(new[] { 0, 2, 1, 3 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_NumberParsesCellsAndKeepsUnparseableLast()
    {
        var b = new Binding();
        string columns = """
              <column type="text" header="Name" width="120" items="{Names}"/>
              <column type="text" header="Lvl" width="80" items="{Levels}" sort="number"/>
              <column type="button" items="{Acts}" onclick="{Act}"/>
            """;
        var (root, list) = Mount("", b, columns: columns);
        ClickAndDraw(root, list, DistX, HeaderY);
        Assert.Equal(new[] { 2, 0, 3, 1 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, DistX, HeaderY);
        Assert.Equal(new[] { 3, 0, 2, 1 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_UnsortableHeadersAndDraggedPressesDoNothing()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, ButtonX, HeaderY);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
        root.OnMouseDown(UiMouseButton.Left, NameX, HeaderY);
        root.OnMouseUp(UiMouseButton.Left, DistX, HeaderY);
        Redraw(list);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_SelectionBandFollowsTheRow()
    {
        var b = new Binding { Selected = 0 };
        var (root, list) = Mount("selectionband=\"true\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        var band = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(list), list.SelectedColor));
        Assert.Equal(18f + 36f + 1f, band.Y);
    }

    [Fact]
    public void Sort_CallbacksReceivePluginRows()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Click(root, OnX, 27);
        Assert.Equal(new[] { 3 }, b.Toggled);
        Redraw(list);
        Click(root, ButtonX, 27);
        Assert.Equal(new[] { 3 }, b.Acted);
    }

    [Fact]
    public void Sort_APressIsCancelledWhenTheOrderChangesUnderIt()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        root.OnMouseMove(ButtonX, 27);
        root.OnMouseDown(UiMouseButton.Left, ButtonX, 27);
        b.Names[3] = "zzz";
        Redraw(list);
        root.OnMouseUp(UiMouseButton.Left, ButtonX, 27);
        Assert.Empty(b.Acted);
    }

    [Fact]
    public void Sort_BoundBelongsToThePlugin()
    {
        var b = new Binding();
        var (root, list) = Mount("sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\" onsort=\"{SetSort}\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 3, 1, 0, 2 }, ViewOrder(root, list, b));
        ClickAndDraw(root, list, NameX, HeaderY);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { (0, false), (0, true), (-1, false) }, b.Sorts);
        b.SortCol = 1;
        Redraw(list);
        Assert.Equal(new[] { 1, 3, 0, 2 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_BoundWithoutOnsortOnlyDisplays()
    {
        var b = new Binding();
        var (root, list) = Mount("sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_UnboundStillNotifies()
    {
        var b = new Binding();
        var (root, list) = Mount("onsort=\"{NotifySort}\"", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal(new[] { (0, false) }, b.Sorts);
        Assert.Equal(new[] { 3, 1, 0, 2 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_RemembersAndForgetsNone()
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        var b = new Binding();
        var (root, list) = Mount("", b, state);
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal("0:asc", state.Get("mobs", "sort"));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Equal("0:desc", state.Get("mobs", "sort"));
        ClickAndDraw(root, list, NameX, HeaderY);
        Assert.Null(state.Get("mobs", "sort"));
    }

    [Theory]
    [InlineData("1:desc", new[] { 2, 0, 3, 1 })]
    [InlineData("9:asc", new[] { 0, 1, 2, 3 })]
    [InlineData("3:asc", new[] { 0, 1, 2, 3 })]
    [InlineData("0:sideways", new[] { 0, 1, 2, 3 })]
    public void Sort_StoredValuesApplyOrAreIgnored(string stored, int[] order)
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        state.Set("mobs", "sort", stored);
        var b = new Binding();
        var (root, list) = Mount("", b, state);
        Assert.Equal(order, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_ACharacterSwitchReReads()
    {
        string character = "alice";
        var store = new MarkupControlStateStore(new SettingsStore(_settingsPath), () => character);
        var state = store.ForWindow("win");
        var b = new Binding();
        var (root, list) = Mount("", b, state);
        ClickAndDraw(root, list, NameX, HeaderY);
        store.Save();
        character = "bob";
        root.Tick(0.016, 0);
        Redraw(list);
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));
        character = "alice";
        root.Tick(0.016, 0);
        Redraw(list);
        Assert.Equal(new[] { 3, 1, 0, 2 }, ViewOrder(root, list, b));
    }

    [Fact]
    public void Sort_GlyphPointsUpThenDown()
    {
        var b = new Binding();
        var (root, list) = Mount("", b);
        ClickAndDraw(root, list, NameX, HeaderY);
        var up = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(list), list.TextColor)
            .Where(q => q.H == 1f && q.X >= 110f && q.X < 120f).OrderBy(q => q.Y).Select(q => q.W).ToArray();
        Assert.Equal(new[] { 1f, 3f, 5f, 7f }, up);
        ClickAndDraw(root, list, NameX, HeaderY);
        var down = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(list), list.TextColor)
            .Where(q => q.H == 1f && q.X >= 110f && q.X < 120f).OrderBy(q => q.Y).Select(q => q.W).ToArray();
        Assert.Equal(new[] { 7f, 5f, 3f, 1f }, down);
    }

    [Theory]
    [InlineData("""<column type="text" width="80" items="{Names}" sort="weird"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="text" width="80" items="{Names}" sort="bool"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="icon" width="20" values="{Ids}" onclick="{Act}" sort="text"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="checkbox" width="20" values="{On}" onchange="{Toggle}" sort="bool" sortkeys="{DistKeys}"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="text" width="80" items="{Names}" sortkeys="{DistKeys}"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    [InlineData("""<column type="text" width="80" items="{Names}" sort="number" sortkeys="{On}"/><column type="button" items="{Acts}" onclick="{Act}"/>""")]
    public void Sort_BadColumnsThrow(string columns)
    {
        Assert.Throws<FormatException>(() => Mount("", new Binding(), columns: columns));
    }

    [Theory]
    [InlineData("sortcolumn=\"{SortCol}\"")]
    [InlineData("sortdescending=\"{SortDesc}\"")]
    [InlineData("onsort=\"{Toggle}\"")]
    public void Sort_BadListAttributesThrow(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }

    private static void DragHeader(UiRoot root, UiMarkupList list, int fromX, int toX)
    {
        root.OnMouseDown(UiMouseButton.Left, fromX, HeaderY);
        root.OnMouseMove(toX, HeaderY);
        root.OnMouseUp(UiMouseButton.Left, toX, HeaderY);
        Redraw(list);
    }

    [Fact]
    public void Resize_DragsTheColumnLeftOfTheBoundary()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\"", b);
        DragHeader(root, list, 120, 150);
        Assert.Equal((0f, 150f), list.ColumnLayoutForTest(0));
        Assert.Equal((150f, 80f), list.ColumnLayoutForTest(1));
        Assert.Equal(new[] { 0, 1, 2, 3 }, ViewOrder(root, list, b));   // no sort
        DragHeader(root, list, 150, 0);
        Assert.Equal(UiMarkupList.MinColumnWidth, list.ColumnLayoutForTest(0).W);
    }

    // UiRoot pairs clicks by its clock: the drag at 1000 ms and the clicks at
    // 5000 ms are too far apart to pair, so both clicks of a double-click are
    // presses from rest.
    private static void DragHeaderThenDoubleClick(UiRoot root, UiMarkupList list, int fromX, int toX)
    {
        root.Tick(0, 1000);
        DragHeader(root, list, fromX, toX);
        root.Tick(0, 5000);
        ClickAndDraw(root, list, toX, HeaderY);
        ClickAndDraw(root, list, toX, HeaderY);
    }

    [Fact]
    public void Resize_DoubleClickRestoresTheMarkupWidth()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\" onsort=\"{NotifySort}\"", b);
        DragHeaderThenDoubleClick(root, list, 120, 150);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
        Assert.Empty(b.Sorts);
    }

    [Fact]
    public void Resize_ADragReleaseIsNotTheFirstClickOfAReset()
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        var (root, list) = Mount("resizable=\"true\"", new Binding(), state);
        root.Tick(0, 1000);
        DragHeader(root, list, 120, 150);
        ClickAndDraw(root, list, 150, HeaderY);   // within 500 ms of the drag's release
        Assert.Equal((0f, 150f), list.ColumnLayoutForTest(0));
        Assert.Equal("150,80,40,0", state.Get("mobs", "widths"));
    }

    [Fact]
    public void Resize_HidingTheHeaderDropsTheCursor()
    {
        var (root, list) = Mount("resizable=\"true\"", new Binding());
        root.OnMouseMove(122, HeaderY);
        Assert.True(list.ActiveCursor().IsValid);
        list.ShowHeader = false;
        Assert.False(list.ActiveCursor().IsValid);
    }

    [Fact]
    public void Resize_DisablingMidDragStopsResizing()
    {
        var (root, list) = Mount("resizable=\"true\"", new Binding());
        root.OnMouseDown(UiMouseButton.Left, 120, HeaderY);
        root.OnMouseMove(130, HeaderY);
        list.Enabled = false;
        root.OnMouseMove(150, HeaderY);
        root.OnMouseUp(UiMouseButton.Left, 150, HeaderY);
        Redraw(list);
        Assert.Equal((0f, 130f), list.ColumnLayoutForTest(0));
    }

    [Fact]
    public void Resize_UnboundNotifiesEachWidth()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\" onresize=\"{NotifyWidths}\"", b);
        DragHeader(root, list, 120, 150);
        var widths = Assert.Single(b.Resizes);
        Assert.Equal(new[] { 150f, 80f, 40f, 0f }, widths);
    }

    [Fact]
    public void Resize_BoundWidthsBelongToThePlugin()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\" widths=\"{Widths}\" onresize=\"{SetWidths}\"", b);
        DragHeader(root, list, 120, 150);
        Assert.Equal(new[] { 150f, 80f, 40f, 0f }, Assert.Single(b.Resizes));
        Assert.Equal((0f, 150f), list.ColumnLayoutForTest(0));
        b.Widths = new() { 60f, 80f, 40f, 0f };
        Redraw(list);
        Assert.Equal((0f, 60f), list.ColumnLayoutForTest(0));
        b.Widths = new() { 60f };
        Redraw(list);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
    }

    [Fact]
    public void Resize_BoundWithoutOnresizeOnlyDisplays()
    {
        var b = new Binding();
        var (root, list) = Mount("resizable=\"true\" widths=\"{Widths}\"", b);
        DragHeader(root, list, 120, 150);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
    }

    [Fact]
    public void Resize_RemembersAndForgetsTheMarkupWidths()
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        var (root, list) = Mount("resizable=\"true\"", new Binding(), state);
        DragHeaderThenDoubleClick(root, list, 120, 150);
        Assert.Null(state.Get("mobs", "widths"));
        Assert.Null(state.Get("mobs", "sort"));
        root.Tick(0, 9000);
        DragHeader(root, list, 120, 150);
        Assert.Equal("150,80,40,0", state.Get("mobs", "widths"));
    }

    [Theory]
    [InlineData("100,80,40,0", 100f)]
    [InlineData("100,80", 120f)]
    [InlineData("a,b,c,d", 120f)]
    [InlineData("5,80,40,0", 16f)]
    public void Resize_StoredWidthsApplyOrAreIgnored(string stored, float nameWidth)
    {
        var state = new MarkupControlStateStore(null, () => "alice").ForWindow("win");
        state.Set("mobs", "widths", stored);
        var (_, list) = Mount("resizable=\"true\"", new Binding(), state);
        Redraw(list);
        Assert.Equal(nameWidth, list.ColumnLayoutForTest(0).W);
    }

    [Fact]
    public void Resize_CursorShowsOverBoundariesOnly()
    {
        var (root, list) = Mount("resizable=\"true\"", new Binding());
        root.OnMouseMove(122, HeaderY);
        Assert.Equal(0x06006128u, list.ActiveCursor().File);
        root.OnMouseMove(60, HeaderY);
        Assert.False(list.ActiveCursor().IsValid);

        var (plainRoot, plain) = Mount("", new Binding());
        plainRoot.OnMouseMove(120, HeaderY);
        Assert.False(plain.ActiveCursor().IsValid);
    }

    [Fact]
    public void Resize_DisabledIgnoresDragsAndShowsNoCursor()
    {
        var (root, list) = Mount("resizable=\"true\" enabled=\"false\"", new Binding());
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 120, Data2 = HeaderY });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 150, Data2 = HeaderY });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 150, Data2 = HeaderY });
        Redraw(list);
        Assert.Equal((0f, 120f), list.ColumnLayoutForTest(0));
        list.OnEvent(new UiEvent { Type = UiEventType.HoverEnter, Data1 = 120, Data2 = HeaderY });
        Assert.False(list.ActiveCursor().IsValid);
    }

    [Theory]
    [InlineData("header=\"false\" resizable=\"true\"")]
    [InlineData("widths=\"{Widths}\"")]
    [InlineData("onresize=\"{SetWidths}\"")]
    [InlineData("resizable=\"true\" widths=\"{Names}\"")]
    [InlineData("resizable=\"true\" onresize=\"{Toggle}\"")]
    public void Resize_BadAttributesThrow(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }
}
