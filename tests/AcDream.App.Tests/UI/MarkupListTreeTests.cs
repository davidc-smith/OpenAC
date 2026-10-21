using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListTreeTests
{
    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "A", "A1", "A2", "A2a", "B", "B1" };
        public List<int> Levels { get; set; } = new() { 0, 1, 1, 2, 0, 1 };
        public List<string> Ids { get; set; } = new() { "a", "a1", "a2", "a2a", "b", "b1" };
        public List<bool> On { get; set; } = new() { false, false, false, false, false, false };
        public HashSet<string> Expanded { get; set; } = new();
        public List<(string Key, bool Expanded)> Expands { get; } = new();
        public Action<string, bool> NotifyExpand => (k, v) => Expands.Add((k, v));
        public Action<int> WrongExpand => _ => { };
        public int Selected { get; set; } = -1;
        public List<int> Picks { get; } = new();
        public Action<int> Pick => row => { Selected = row; Picks.Add(row); };
        public Action<int> Toggle => _ => { };
        public List<int> Selection { get; set; } = new();
        public List<IReadOnlyList<int>> Changes { get; } = new();
        public Action<IReadOnlyList<int>> SetSelection => rows => { Selection = rows.ToList(); Changes.Add(rows); };
        public int SortCol { get; set; } = -1;
        public bool SortDesc { get; set; }
    }

    // Checkbox 0-20, tree column 20-300 (the first text column, not the first column).
    // No header: view row v spans y 18v .. 18v+18. Arrow slot for level L: x 23+12L .. 35+12L.
    private const string ColumnsXml = """
          <column type="checkbox" width="20" values="{On}" onchange="{Toggle}"/>
          <column type="text" items="{Names}"/>
        """;

    private const string SortColumnsXml = """
          <column type="checkbox" header="" width="20" values="{On}" onchange="{Toggle}"/>
          <column type="text" header="Name" items="{Names}" sort="text"/>
        """;

    private const string Tree = "tree=\"true\" levels=\"{Levels}\" keys=\"{Ids}\" selected=\"{Selected}\" onchange=\"{Pick}\"";
    private const string MultiTree = "tree=\"true\" levels=\"{Levels}\" keys=\"{Ids}\" selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\"";

    private const int TextX = 150;

    private static int RowY(int view, bool header = false) => (header ? 18 : 0) + 9 + 18 * view;

    private static int ArrowX(int level) => 29 + 12 * level;

    private static string ListXml(string attributes, string columns = ColumnsXml, string? name = "rules", int height = 200)
    {
        string named = name is null ? "" : $"name=\"{name}\"";
        return $"""
              <list {named} x="0" y="0" w="300" h="{height}" {attributes}>
            {columns}
              </list>
            """;
    }

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required UiMarkupList List { get; init; }
        public UiSelectModifiers Keys { get; set; }
    }

    private static UiPanel BuildPanel(string listXml, Binding b, IMarkupControlState? state = null) =>
        MarkupDocument.Build($"""
            <panel x="0" y="0" w="400" h="300">
            {listXml}
            </panel>
            """, b, id => (id, 16, 16), controlState: state);

    private static Mounted Mount(Binding b, string attributes = Tree, string? listXml = null, IMarkupControlState? state = null)
    {
        var panel = BuildPanel(listXml ?? ListXml(attributes), b, state);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        var m = new Mounted { Root = root, List = list };
        list.ModifiersSource = () => m.Keys;
        Redraw(list);                                   // lists read rows and lay out at draw
        return m;
    }

    private static void Redraw(UiMarkupList list) => MarkupDrawProbe.Draw(list.Parent!, 800f, 600f);

    private static void Click(Mounted m, int x, int y, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnMouseDown(UiMouseButton.Left, x, y);
        m.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Redraw(m.List);
    }

    /// <summary>The plugin row shown at each view row, by clicking each row's text until a click picks nothing.</summary>
    private static int[] ViewOrder(Mounted m, Binding b, bool header = false)
    {
        var order = new List<int>();
        for (int view = 0; view < 10; view++)
        {
            b.Picks.Clear();
            Click(m, TextX, RowY(view, header));
            if (b.Picks.Count == 0)
                break;
            order.Add(Assert.Single(b.Picks));
        }
        return order.ToArray();
    }

    private static IMarkupControlState NewState() =>
        new MarkupControlStateStore(null, () => "alice").ForWindow("win");

    /// <summary>Records every call, so a test can prove the state was never touched.</summary>
    private sealed class RecordingState : IMarkupControlState
    {
        public int Calls { get; private set; }
        public int Generation { get { Calls++; return 1; } }
        public string? Get(string control, string kind) { Calls++; return null; }
        public void Set(string control, string kind, string value) => Calls++;
        public void Remove(string control, string kind) => Calls++;
    }

    [Fact]
    public void Default_ShowsEveryRowExpanded()
    {
        var b = new Binding();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(Mount(b), b));
    }

    [Fact]
    public void CollapsedAttribute_StartsWithOnlyTopLevelRows()
    {
        var b = new Binding();
        Assert.Equal(new[] { 0, 4 }, ViewOrder(Mount(b, Tree + " collapsed=\"true\""), b));
    }

    [Fact]
    public void BoundExpanded_IsReadEveryFrame()
    {
        var b = new Binding { Expanded = new() { "a" } };
        var m = Mount(b, Tree + " expanded=\"{Expanded}\"");
        Assert.Equal(new[] { 0, 1, 2, 4 }, ViewOrder(m, b));
        b.Expanded = new() { "a", "a2" };
        Redraw(m.List);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, ViewOrder(m, b));
    }

    [Fact]
    public void RowsPastTheKeys_UseHashRowKeys()
    {
        var b = new Binding { Ids = new() { "a" }, Expanded = new() { "a", "#2" } };
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, ViewOrder(Mount(b, Tree + " expanded=\"{Expanded}\""), b));
    }

    [Fact]
    public void RowsPastTheLevels_AreTopLevel()
    {
        var b = new Binding { Levels = new() { 0, 1 } };   // rows 2..5 are level 0; row 0 has child 1
        Assert.Equal(new[] { 0, 2, 3, 4, 5 }, ViewOrder(Mount(b, Tree + " collapsed=\"true\""), b));
    }

    [Fact]
    public void Remembered_KeysFlipTheDefault()
    {
        var state = NewState();
        state.Set("rules", "expanded", "[\"a\"]");
        var b = new Binding();
        Assert.Equal(new[] { 0, 4, 5 }, ViewOrder(Mount(b, state: state), b));

        var b2 = new Binding();
        Assert.Equal(new[] { 0, 1, 2, 4 }, ViewOrder(Mount(b2, Tree + " collapsed=\"true\"", state: state), b2));
    }

    [Fact]
    public void Remembered_MalformedValueIsIgnored()
    {
        var state = NewState();
        state.Set("rules", "expanded", "{");
        var b = new Binding();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(Mount(b, state: state), b));
    }

    [Fact]
    public void UnnamedTree_NeverTouchesTheState()
    {
        var state = new RecordingState();
        var b = new Binding();
        var m = Mount(b, listXml: ListXml(Tree, name: null), state: state);
        ViewOrder(m, b);
        Assert.Equal(0, state.Calls);
    }

    [Fact]
    public void Sorting_SortsSiblingsAndKeepsChildrenUnderTheirParent()
    {
        var b = new Binding { SortCol = 1, SortDesc = true };
        var m = Mount(b, listXml: ListXml(
            Tree + " header=\"true\" sortcolumn=\"{SortCol}\" sortdescending=\"{SortDesc}\"", SortColumnsXml));
        Assert.Equal(new[] { 4, 5, 0, 2, 3, 1 }, ViewOrder(m, b, header: true));
    }

    [Fact]
    public void Clicks_ReportPluginRowsUnderCollapse()
    {
        var b = new Binding { Expanded = new() { "b" } };
        var m = Mount(b, Tree + " expanded=\"{Expanded}\"");   // shown: 0, 4, 5
        Click(m, TextX, RowY(1));
        Assert.Equal(4, b.Selected);
    }

    [Fact]
    public void Scrolling_UsesTheRowsShown()
    {
        var b = new Binding();
        var m = Mount(b, listXml: ListXml(Tree + " collapsed=\"true\"", height: 36));   // 2 visible rows, 2 shown
        m.Root.OnMouseMove(TextX, 9);
        m.Root.OnScroll(-1);
        Redraw(m.List);
        Click(m, TextX, RowY(0));
        Assert.Equal(0, b.Selected);
    }

    [Fact]
    public void Draw_ExpandedArrowsOnParentsOnly()
    {
        var b = new Binding();
        var m = Mount(b);
        m.List.TextColor = new Vector4(0.1f, 0.9f, 0.2f, 1f);   // unmistakable
        var quads = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.TextColor);
        Assert.Equal(12, quads.Count);                          // ▾ on rows 0, 2, 4
        Assert.All(quads, q => Assert.Equal(1f, q.H));
        Assert.Contains(quads, q => q.X == 25f && q.Y == 6f);   // row 0, level 0
        Assert.Contains(quads, q => q.X == 37f && q.Y == 42f);  // row 2, level 1, view 2
        Assert.Contains(quads, q => q.X == 25f && q.Y == 78f);  // row 4, view 4
    }

    [Fact]
    public void Draw_CollapsedArrows()
    {
        var b = new Binding();
        var m = Mount(b, Tree + " collapsed=\"true\"");
        m.List.TextColor = new Vector4(0.1f, 0.9f, 0.2f, 1f);
        var quads = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.TextColor);
        Assert.Equal(8, quads.Count);                           // ▸ on rows 0 and 4
        Assert.All(quads, q => Assert.Equal(1f, q.W));
        Assert.Contains(quads, q => q.X == 26f && q.Y == 5f);
        Assert.Contains(quads, q => q.X == 26f && q.Y == 23f);
    }

    [Fact]
    public void BuildErrors()
    {
        var b = new Binding();
        void Throws(string listXml, string expected) =>
            Assert.Contains(expected, Assert.Throws<FormatException>(() => BuildPanel(listXml, b)).Message);

        Throws("""<list x="0" y="0" w="300" h="200" items="{Names}" selected="{Selected}" tree="true" levels="{Levels}" keys="{Ids}"/>""",
            "<column>");
        Throws(ListXml(Tree, """<column type="checkbox" values="{On}" onchange="{Toggle}"/>"""), "text column");
        Throws(ListXml("tree=\"true\" keys=\"{Ids}\" selected=\"{Selected}\""), "list levels");
        Throws(ListXml("tree=\"true\" levels=\"{Names}\" keys=\"{Ids}\" selected=\"{Selected}\""), "list levels");
        Throws(ListXml("tree=\"true\" levels=\"{Levels}\" selected=\"{Selected}\""), "list keys");
        Throws(ListXml(Tree + " expanded=\"{Levels}\""), "list expanded");
        Throws(ListXml(Tree + " onexpand=\"{WrongExpand}\""), "list onexpand");
        Throws(ListXml(Tree + " expanded=\"{Expanded}\" collapsed=\"true\""), "collapsed");
        Throws(ListXml("levels=\"{Levels}\" selected=\"{Selected}\""), "tree");
    }

    private static readonly UiSelectModifiers Add = new(Add: true, Range: false);

    private static void Press(Mounted m, Key key, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnKeyDown((int)key);
        Redraw(m.List);
    }

    [Fact]
    public void ArrowClick_TogglesAParentWithoutSelecting()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, ArrowX(0), RowY(0));
        Assert.Empty(b.Picks);
        Assert.Equal(new[] { 0, 4, 5 }, ViewOrder(m, b));
        Click(m, ArrowX(0), RowY(0));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(m, b));
    }

    [Fact]
    public void ArrowSlotOfALeaf_Selects()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, ArrowX(1), RowY(1));   // A1 is a leaf
        Assert.Equal(new[] { 1 }, b.Picks);
    }

    [Fact]
    public void Unbound_RemembersOnlyKeysThatDifferFromTheDefault()
    {
        var state = NewState();
        var b = new Binding();
        var m = Mount(b, Tree + " onexpand=\"{NotifyExpand}\"", state: state);
        Click(m, ArrowX(0), RowY(0));
        Assert.Equal("[\"a\"]", state.Get("rules", "expanded"));
        Assert.Equal(("a", false), Assert.Single(b.Expands));
        Click(m, ArrowX(0), RowY(0));
        Assert.Null(state.Get("rules", "expanded"));
        Assert.Equal(("a", true), b.Expands[1]);
    }

    [Fact]
    public void Unbound_CollapsedDefaultRemembersExpandedKeys()
    {
        var state = NewState();
        var b = new Binding();
        var m = Mount(b, Tree + " collapsed=\"true\"", state: state);
        Click(m, ArrowX(0), RowY(1));   // B at view 1
        Assert.Equal("[\"b\"]", state.Get("rules", "expanded"));
        Assert.Equal(new[] { 0, 4, 5 }, ViewOrder(m, b));
    }

    [Fact]
    public void Bound_ArrowClickOnlyAsksThePlugin()
    {
        var b = new Binding { Expanded = new() { "a", "a2", "b" } };
        var m = Mount(b, Tree + " expanded=\"{Expanded}\" onexpand=\"{NotifyExpand}\"");
        Click(m, ArrowX(0), RowY(0));
        Assert.Equal(("a", false), Assert.Single(b.Expands));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(m, b));   // the plugin didn't apply it
    }

    [Fact]
    public void TreeList_IsATabStopThatClicksDoNotFocus()
    {
        var list = Mount(new Binding()).List;
        Assert.True(list.AcceptsFocus && list.TabStop);
        Assert.False(list.FocusOnMouseClick);
    }

    [Fact]
    public void LeftRight_SingleSelect()
    {
        var b = new Binding { Selected = 2 };   // A2, expanded, child A2a
        var m = Mount(b);
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Right);
        Assert.Equal(3, b.Selected);            // to the first child
        Press(m, Key.Left);
        Assert.Equal(2, b.Selected);            // leaf: to the parent
        b.Picks.Clear();
        Press(m, Key.Left);                     // expanded parent: collapse
        Assert.Empty(b.Picks);
        Assert.Equal(new[] { 0, 1, 2, 4, 5 }, ViewOrder(m, b));
        b.Selected = 2;
        Redraw(m.List);
        m.Root.SetKeyboardFocus(m.List);        // ViewOrder's clicks cleared focus
        Press(m, Key.Right);                    // collapsed parent: expand
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, ViewOrder(m, b));
    }

    [Fact]
    public void LeftRight_ActOnTheLeadInMultiMode()
    {
        var b = new Binding();
        var m = Mount(b, MultiTree);
        Click(m, TextX, RowY(0));               // lead A
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Right);
        Assert.Equal(new[] { 1 }, b.Selection); // to A1
        Press(m, Key.Left);
        Assert.Equal(new[] { 0 }, b.Selection); // back to A
    }

    [Fact]
    public void Collapse_DeselectsHiddenRowsAndMovesTheLeadUp()
    {
        var b = new Binding();
        var m = Mount(b, MultiTree);
        Click(m, TextX, RowY(4));               // [4]
        Click(m, TextX, RowY(1), Add);          // [1, 4]
        Click(m, TextX, RowY(3), Add);          // [1, 3, 4], lead 3
        b.Changes.Clear();
        Click(m, ArrowX(0), RowY(0));           // collapse A
        Assert.Equal(new[] { 4 }, Assert.Single(b.Changes));
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Down);                     // lead moved to A (view 0): Down → view 1 = B
        Assert.Equal(new[] { 4 }, b.Selection);
    }

    [Fact]
    public void CtrlA_SelectsOnlyTheRowsShown()
    {
        var b = new Binding();
        var m = Mount(b, MultiTree + " collapsed=\"true\"");
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.A, Add);
        Assert.Equal(new[] { 0, 4 }, b.Selection);
    }

    [Fact]
    public void SingleMode_CollapseKeepsTheSelection()
    {
        var b = new Binding { Selected = 3 };
        var m = Mount(b);
        Click(m, ArrowX(0), RowY(0));
        Assert.Empty(b.Picks);
        Assert.Equal(3, b.Selected);
    }

    [Fact]
    public void Disabled_IgnoresArrowsAndKeys()
    {
        var b = new Binding { Selected = 0 };
        var m = Mount(b, Tree + " enabled=\"false\"");
        Click(m, ArrowX(0), RowY(0));
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Left);
        b.Selected = -1;
        Redraw(m.List);
        // Still every row shown: nothing collapsed. (Rows can't be clicked while disabled, so count arrows.)
        m.List.TextColor = new Vector4(0.1f, 0.9f, 0.2f, 1f);
        Assert.Equal(12, MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.TextColor).Count);
    }
}
