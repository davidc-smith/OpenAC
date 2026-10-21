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
        Click(root, 60, 9);
        Assert.Empty(b.Picks);
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
}
