using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListContextMenuTests
{
    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "mosswart", "Drudge", "rat", "Banderling" };
        public List<string> Menu { get; set; } = new() { "Loot", "-", "Ignore" };
        public int Single { get; set; } = -1;
        public List<int> SingleChanges { get; } = new();
        public Action<int> Pick => row => { Single = row; SingleChanges.Add(row); };
        public List<int> Selection { get; set; } = new();
        public List<IReadOnlyList<int>> Changes { get; } = new();
        public Action<IReadOnlyList<int>> SetSelection => rows => { Selection = rows.ToList(); Changes.Add(rows); };
        public List<int> Opens { get; } = new();
        public List<int> SelectedAtOpen { get; } = new();
        public Action<int> OnOpen => row => { Opens.Add(row); SelectedAtOpen.Add(Single); };
        public List<int> Picks { get; } = new();
        public Action<int> OnPick => Picks.Add;
        public int SortCol { get; set; }
        public bool SortDesc { get; set; }
    }

    // Header y 0-18; view row v spans y 18+18v .. 36+18v; 5 rows fit in h=108.
    // Ascending view order: Banderling(3), Drudge(1), mosswart(0), rat(2).
    private const string Menu = "<contextmenu items=\"{Menu}\" onopen=\"{OnOpen}\" onselect=\"{OnPick}\"/>";

    private static string ColumnList(string selection) => $"""
          <list x="0" y="0" w="300" h="108" header="true" sortcolumn="{"{SortCol}"}" sortdescending="{"{SortDesc}"}" {selection}>
            <column type="text" header="Name" items="{"{Names}"}" sort="text"/>
            {Menu}
          </list>
        """;

    private const string SingleSelection = "selected=\"{Single}\" onchange=\"{Pick}\"";
    private const string MultiSelection = "selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\"";

    private static int RowY(int view) => 27 + 18 * view;

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required UiMarkupList List { get; init; }
        public required UiMarkupContextMenu Menu { get; init; }
        public UiSelectModifiers Keys { get; set; }
    }

    private static Mounted Mount(Binding binding, string listXml)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\">{listXml}</panel>", binding, id => (id, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        var mounted = new Mounted
        {
            Root = root, List = list, Menu = list.Children.OfType<UiMarkupContextMenu>().Single(),
        };
        list.ModifiersSource = () => mounted.Keys;
        Redraw(mounted);
        return mounted;
    }

    private static void Redraw(Mounted m) => MarkupDrawProbe.Draw(m.List.Parent!, 800f, 600f);

    private static void RightClick(Mounted m, int x, int y)
    {
        m.Root.OnMouseDown(UiMouseButton.Right, x, y);
        m.Root.OnMouseUp(UiMouseButton.Right, x, y);
        Redraw(m);
    }

    private static void Click(Mounted m, int x, int y, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnMouseDown(UiMouseButton.Left, x, y);
        m.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Redraw(m);
    }

    [Fact]
    public void RightClickARow_SelectsItThenOpens_WithThePluginRow()
    {
        var b = new Binding();
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(1));
        Assert.Equal(new[] { 1 }, b.SingleChanges);
        Assert.Equal(new[] { 1 }, b.Opens);
        Assert.Equal(new[] { 1 }, b.SelectedAtOpen);
        Assert.True(m.Menu.IsOpen);
    }

    [Fact]
    public void RightClickTheSelectedRow_DoesNotCallOnchange()
    {
        var b = new Binding { Single = 3 };
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(0));
        Assert.Empty(b.SingleChanges);
        Assert.Equal(new[] { 3 }, b.Opens);
    }

    [Fact]
    public void EmptySpaceAndHeader_GiveMinusOne_AndKeepTheSelection()
    {
        var b = new Binding { Single = 2 };
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(4));       // below the four rows
        m.Menu.Close();
        RightClick(m, 60, 9);             // header
        Assert.Equal(new[] { -1, -1 }, b.Opens);
        Assert.Empty(b.SingleChanges);
        Assert.Equal(2, b.Single);
    }

    [Fact]
    public void Multi_RowInTheSelection_KeepsIt()
    {
        var b = new Binding { Selection = new() { 0, 1 } };
        var m = Mount(b, ColumnList(MultiSelection));
        RightClick(m, 60, RowY(1));       // plugin 1
        Assert.Empty(b.Changes);
        Assert.Equal(new[] { 1 }, b.Opens);
    }

    [Fact]
    public void Multi_RowOutsideTheSelection_SelectsOnlyIt_AndAnchorsThere()
    {
        var b = new Binding { Selection = new() { 0 } };
        var m = Mount(b, ColumnList(MultiSelection));
        RightClick(m, 60, RowY(0));       // plugin 3
        Assert.Equal(new[] { 3 }, Assert.Single(b.Changes));
        m.Menu.Close();
        Click(m, 60, RowY(2), new UiSelectModifiers(Add: false, Range: true));
        Assert.Equal(new[] { 0, 1, 3 }, b.Selection);   // views 0..2 = plugins 3, 1, 0
    }

    [Fact]
    public void EmptyMenu_SelectsAndCallsOnopen_ButDoesNotOpen()
    {
        var b = new Binding { Menu = new() };
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(1));
        Assert.Equal(new[] { 1 }, b.SingleChanges);
        Assert.Equal(new[] { 1 }, b.Opens);
        Assert.False(m.Menu.IsOpen);
    }

    [Fact]
    public void SingleColumnForm_GivesPluginRows()
    {
        var b = new Binding();
        var m = Mount(b, $"<list x=\"0\" y=\"0\" w=\"300\" h=\"108\" items=\"{{Names}}\" {SingleSelection}>{Menu}</list>");
        RightClick(m, 60, 27);            // row 1 (no header: rows start at 0)
        Assert.Equal(new[] { 1 }, b.Opens);
        Assert.Equal(1, b.Single);
    }

    [Fact]
    public void PickAfterARowRightClick_ReportsTheItem()
    {
        var b = new Binding();
        var m = Mount(b, ColumnList(SingleSelection));
        RightClick(m, 60, RowY(1));       // popup top at y 45
        Click(m, 70, 80);                 // Ignore: 45 + 3 + 18 + 7 = 73 .. 91
        Assert.Equal(new[] { 2 }, b.Picks);
    }
}
