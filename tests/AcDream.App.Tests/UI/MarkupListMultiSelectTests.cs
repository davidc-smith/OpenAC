using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.Tests.Plugins;
using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListMultiSelectTests
{
    private sealed class Binding
    {
        public List<string> Names { get; set; } = new() { "mosswart", "Drudge", "rat", "Banderling" };
        public List<bool> On { get; set; } = new() { false, false, false, false };
        public List<string> Acts { get; set; } = new() { "Go", "Go", "Go", "Go" };
        public List<int> Selection { get; set; } = new();
        public List<IReadOnlyList<int>> Changes { get; } = new();
        public Action<IReadOnlyList<int>> SetSelection => rows => { Selection = rows.ToList(); Changes.Add(rows); };
        public Action<int> Pick => _ => { };
        public Action<int> Toggle => _ => { };
        public Action<int> Act => _ => { };
        public int Single { get; set; } = -1;
        public int SortCol { get; set; } = 0;
        public bool SortDesc { get; set; }
    }

    // Name 0-120 (sorted by sortcolumn), checkbox 120-160, button 160-300.
    // Header y 0-18; view row v spans y 18+18v .. 36+18v.
    // Ascending view order: Banderling(3), Drudge(1), mosswart(0), rat(2).
    private const string ColumnsXml = """
          <column type="text" header="Name" width="120" items="{Names}" sort="text"/>
          <column type="checkbox" header="On" width="40" values="{On}" onchange="{Toggle}"/>
          <column type="button" items="{Acts}" onclick="{Act}"/>
        """;

    private const int NameX = 60, CheckX = 140, ButtonX = 230;

    private static int RowY(int view) => 27 + 18 * view;

    private static UiPanel BuildPanel(string listXml, Binding binding, PluginUiThemeSettings? themes = null)
    {
        string theme = themes is null ? "" : "theme=\"plugin\"";
        return MarkupDocument.Build(
            $"""
            <panel x="0" y="0" w="400" h="300" {theme}>
            {listXml}
            </panel>
            """,
            binding, id => (id, 16, 16), themes: themes);
    }

    private static string ColumnList(string attributes = "selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\"") => $"""
          <list name="mobs" x="0" y="0" w="300" h="108" header="true" sortcolumn="{"{SortCol}"}" sortdescending="{"{SortDesc}"}" {attributes}>
        {ColumnsXml}
          </list>
        """;

    private sealed class Mounted
    {
        public required UiRoot Root { get; init; }
        public required UiMarkupList List { get; init; }
        public UiSelectModifiers Keys { get; set; }
    }

    private static Mounted Mount(Binding binding, string? listXml = null, PluginUiThemeSettings? themes = null,
        bool fakeModifiers = true)
    {
        var panel = BuildPanel(listXml ?? ColumnList(), binding, themes);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        var mounted = new Mounted { Root = root, List = list };
        if (fakeModifiers)
            list.ModifiersSource = () => mounted.Keys;
        Redraw(list);                                   // lists read rows and lay out at draw
        return mounted;
    }

    private static void Redraw(UiMarkupList list) => MarkupDrawProbe.Draw(list.Parent!, 800f, 600f);

    private static void Click(Mounted m, int x, int y, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnMouseDown(UiMouseButton.Left, x, y);
        m.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Redraw(m.List);
    }

    private static readonly UiSelectModifiers Add = new(Add: true, Range: false);
    private static readonly UiSelectModifiers Range = new(Add: false, Range: true);
    private static readonly UiSelectModifiers AddRange = new(Add: true, Range: true);

    [Fact]
    public void Click_SelectsOnlyThatRow()
    {
        var b = new Binding { Selection = new() { 0, 2 } };
        var m = Mount(b);
        Click(m, NameX, RowY(1));
        Assert.Equal(new[] { 1 }, Assert.Single(b.Changes));
    }

    [Fact]
    public void CtrlClick_TogglesTheRow()
    {
        var b = new Binding { Selection = new() { 1 } };
        var m = Mount(b);
        Click(m, NameX, RowY(0), Add);
        Assert.Equal(new[] { 1, 3 }, b.Selection);
        Click(m, NameX, RowY(0), Add);
        Assert.Equal(new[] { 1 }, b.Selection);
    }

    [Fact]
    public void ShiftClick_SelectsTheRangeInViewOrder()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(0));          // plugin 3, the anchor
        Click(m, NameX, RowY(2), Range);   // views 0..2 = plugins 3, 1, 0
        Assert.Equal(new[] { 0, 1, 3 }, b.Selection);
        Click(m, NameX, RowY(1), Range);   // same anchor: views 0..1
        Assert.Equal(new[] { 1, 3 }, b.Selection);
    }

    [Fact]
    public void CtrlShiftClick_AddsTheRange()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(3));             // [2]
        Click(m, NameX, RowY(0), Add);        // [2, 3], anchor plugin 3
        Click(m, NameX, RowY(1), AddRange);   // + views 0..1 = plugins 3, 1
        Assert.Equal(new[] { 1, 2, 3 }, b.Selection);
    }

    [Fact]
    public void ShiftClick_WithoutAnAnchor_SelectsOnlyThatRow()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(1), Range);
        Assert.Equal(new[] { 1 }, b.Selection);
    }

    [Fact]
    public void Anchor_FollowsItsRowAcrossAResort()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, NameX, RowY(0));          // anchor plugin 3 (Banderling)
        b.SortDesc = true;                 // views: rat(2), mosswart(0), Drudge(1), Banderling(3)
        Redraw(m.List);
        Click(m, NameX, RowY(1), Range);   // views 1..3 = plugins 0, 1, 3
        Assert.Equal(new[] { 0, 1, 3 }, b.Selection);
    }

    [Fact]
    public void CheckButtonAndTextOnclickCells_DoNotSelect()
    {
        var b = new Binding();
        var m = Mount(b);
        Click(m, CheckX, RowY(0));
        Click(m, ButtonX, RowY(0));
        Assert.Empty(b.Changes);

        var b2 = new Binding();
        var m2 = Mount(b2, """
              <list x="0" y="0" w="300" h="108" selectmode="multi" selected="{Selection}" onchange="{SetSelection}">
                <column type="text" items="{Names}" onclick="{Pick}"/>
              </list>
            """);
        Click(m2, NameX, 9);
        Assert.Empty(b2.Changes);
    }

    [Fact]
    public void SingleColumnForm_UsesPluginOrder()
    {
        var b = new Binding();
        var m = Mount(b, """
              <list x="0" y="0" w="300" h="108" items="{Names}" selectmode="multi" selected="{Selection}" onchange="{SetSelection}"/>
            """);
        Click(m, 60, 9);                  // row 0
        Click(m, 60, 9 + 36, Range);      // rows 0..2
        Assert.Equal(new[] { 0, 1, 2 }, b.Selection);
        Click(m, 60, 9 + 18, Add);
        Assert.Equal(new[] { 0, 2 }, b.Selection);
    }

    [Fact]
    public void Disabled_FiresNothing()
    {
        var b = new Binding();
        var m = Mount(b, ColumnList("selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\" enabled=\"false\""));
        Click(m, NameX, RowY(0));
        Assert.Empty(b.Changes);
    }

    [Fact]
    public void WithoutOnchange_ClickIsDisplayOnly()
    {
        var b = new Binding { Selection = new() { 2 } };
        var m = Mount(b, ColumnList("selectmode=\"multi\" selected=\"{Selection}\""));
        Click(m, NameX, RowY(0));
        Assert.Equal(new[] { 2 }, b.Selection);
    }

    [Fact]
    public void Band_DrawsOnEverySelectedRow()
    {
        var b = new Binding { Selection = new() { 0, 3 } };
        var m = Mount(b);
        var bands = MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor)
            .Select(q => q.Y).Order().ToArray();
        Assert.Equal(new[] { 18f + 1f, 18f + 36f + 1f }, bands);   // plugin 3 at view 0, plugin 0 at view 2
    }

    [Fact]
    public void Band_IgnoresDuplicatesAndStaleRows()
    {
        var b = new Binding { Selection = new() { 0, 0, 9, -1 } };
        var m = Mount(b);
        Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
    }

    [Fact]
    public void PluginTheme_Draws()
    {
        var b = new Binding { Selection = new() { 1 } };
        var m = Mount(b, themes: new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
    }

    [Theory]
    [InlineData(ControlKey.ControlLeft, new[] { 0, 3 })]
    [InlineData(ControlKey.SuperLeft, new[] { 0, 3 })]
    [InlineData(ControlKey.ShiftLeft, new[] { 0, 1, 3 })]
    public void DefaultModifiers_ReadTheRootKeyboard(ControlKey held, int[] expected)
    {
        var b = new Binding();
        var m = Mount(b, fakeModifiers: false);
        var keyboard = new FakeKeyboard();
        m.Root.Keyboard = keyboard;
        Click(m, NameX, RowY(0));                    // [3], anchor 3
        keyboard.Held.Add((Key)held);
        Click(m, NameX, RowY(2));                    // toggle plugin 0 in, or range views 0..2
        Assert.Equal(expected, b.Selection);
        Assert.Null(m.Root.KeyboardFocus);
    }

    // Silk's Key values, so InlineData can name them.
    public enum ControlKey
    {
        ControlLeft = (int)Key.ControlLeft,
        SuperLeft = (int)Key.SuperLeft,
        ShiftLeft = (int)Key.ShiftLeft,
    }

    [Fact]
    public void BuildErrors()
    {
        var b = new Binding();
        Assert.Contains("selectmode", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"many\" selected=\"{Selection}\""), b)).Message);
        Assert.Contains("list selected", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\""), b)).Message);
        Assert.Contains("list selected", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\" selected=\"{Single}\""), b)).Message);
        Assert.Contains("list onchange", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{Pick}\""), b)).Message);
        Assert.Contains("selectionband", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selectmode=\"multi\" selected=\"{Selection}\" selectionband=\"false\""), b)).Message);
    }

    [Fact]
    public void SingleMode_IsUnchanged()
    {
        var b = new Binding();
        var panel = BuildPanel(ColumnList("selected=\"{Single}\" onchange=\"{Pick}\""), b);
        var list = panel.Children.OfType<UiMarkupList>().Single();
        Assert.False(list.MultiSelect);
        Assert.False(list.SelectionBandEnabled);
        Assert.Contains("did not resolve to an Action<int> property", Assert.Throws<FormatException>(() =>
            BuildPanel(ColumnList("selected=\"{Single}\" onchange=\"{SetSelection}\""), b)).Message);
    }

    private static void Press(Mounted m, Key key, UiSelectModifiers keys = default)
    {
        m.Keys = keys;
        m.Root.OnKeyDown((int)key);
        Redraw(m.List);
    }

    private static Mounted MountFocused(Binding b, string? listXml = null)
    {
        var m = Mount(b, listXml);
        m.Root.SetKeyboardFocus(m.List);
        return m;
    }

    [Fact]
    public void Focus_FlagsOnlyInMultiMode()
    {
        var multi = Mount(new Binding()).List;
        Assert.True(multi.AcceptsFocus && multi.TabStop);
        Assert.False(multi.FocusOnMouseClick);
        var single = BuildPanel(ColumnList("selected=\"{Single}\""), new Binding())
            .Children.OfType<UiMarkupList>().Single();
        Assert.False(single.TabStop);
    }

    [Fact]
    public void UpDown_MoveASingleSelectionInViewOrder()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Press(m, Key.Down); Assert.Equal(new[] { 3 }, b.Selection);
        Press(m, Key.Down); Assert.Equal(new[] { 1 }, b.Selection);
        Press(m, Key.Up);   Assert.Equal(new[] { 3 }, b.Selection);
        b.Changes.Clear();
        Press(m, Key.Up);
        Assert.Equal(new[] { 3 }, Assert.Single(b.Changes));
    }

    [Fact]
    public void ShiftUpDown_ExtendFromTheAnchor()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Press(m, Key.Down);          // [3], anchor 3
        Press(m, Key.Down, Range);   Assert.Equal(new[] { 1, 3 }, b.Selection);
        Press(m, Key.Down, Range);   Assert.Equal(new[] { 0, 1, 3 }, b.Selection);
        Press(m, Key.Up, Range);     Assert.Equal(new[] { 1, 3 }, b.Selection);
    }

    [Fact]
    public void CtrlA_SelectsAll()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Press(m, Key.A, Add);
        Assert.Equal(new[] { 0, 1, 2, 3 }, b.Selection);
    }

    [Fact]
    public void Keys_ContinueFromTheClickedRow()
    {
        var b = new Binding();
        var m = MountFocused(b);
        Click(m, NameX, RowY(2));                 // plugin 0
        Assert.Null(m.Root.KeyboardFocus);        // a click on a non-text control clears focus (UiRoot rule)
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.Down);                       // view 3 = plugin 2
        Assert.Equal(new[] { 2 }, b.Selection);
    }

    [Fact]
    public void Keys_FallThroughWhenUnfocusedOrUnhandled()
    {
        var b = new Binding();
        var m = Mount(b);
        var fell = new List<int>();
        m.Root.WorldKeyFallThrough += (vk, _) => fell.Add(vk);
        Press(m, Key.Down);
        Assert.Empty(b.Changes);
        m.Root.SetKeyboardFocus(m.List);
        Press(m, Key.W);
        Press(m, Key.Down);
        Assert.Equal(new[] { (int)Key.Down, (int)Key.W }, fell);
        Assert.Single(b.Changes);
    }

    [Fact]
    public void Keys_DisabledFireNothing()
    {
        var b = new Binding();
        var m = MountFocused(b, ColumnList("selectmode=\"multi\" selected=\"{Selection}\" onchange=\"{SetSelection}\" enabled=\"false\""));
        Press(m, Key.Down);
        Press(m, Key.A, Add);
        Assert.Empty(b.Changes);
    }

    [Fact]
    public void Keys_SingleColumnFormStepsInPluginOrder()
    {
        var b = new Binding();
        var m = MountFocused(b, """
              <list x="0" y="0" w="300" h="108" items="{Names}" selectmode="multi" selected="{Selection}" onchange="{SetSelection}"/>
            """);
        Press(m, Key.Down);
        Press(m, Key.Down);
        Assert.Equal(new[] { 1 }, b.Selection);
    }

    [Fact]
    public void Lead_IsRevealedButReordersDoNotScroll()
    {
        var b = new Binding
        {
            Names = Enumerable.Range(0, 12).Select(i => $"mob {i:00}").ToList(),
            On = Enumerable.Repeat(false, 12).ToList(),
            Acts = Enumerable.Repeat("Go", 12).ToList(),
        };
        var m = MountFocused(b);                  // 108 px: header + 5 rows (top row y 19, last y 91)
        for (int i = 0; i < 8; i++)
            Press(m, Key.Down);                   // lead plugin 7 at view 7: scrolls to top row 3
        var band = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
        Assert.Equal(18f + 4 * 18f + 1f, band.Y);

        b.SortDesc = true;                        // descending: plugin 7 at view 4, still in rows 3..7
        Redraw(m.List);
        band = Assert.Single(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
        Assert.Equal(18f + 1 * 18f + 1f, band.Y);

        // Live data moves the lead's row to the end (view 11): no reveal, so it scrolls out of sight.
        b.Names = b.Names.Select((n, i) => i == 7 ? "a" : n).ToList();
        Redraw(m.List);
        Assert.Empty(MarkupDrawProbe.QuadBounds(MarkupDrawProbe.Draw(m.List), m.List.SelectedColor));
    }

    [Fact]
    public void FocusOutline_OnlyWhileFocused()
    {
        var m = Mount(new Binding());
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(m.List), UiMarkupExpanderHeader.FocusColor));
        m.Root.SetKeyboardFocus(m.List);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(m.List), UiMarkupExpanderHeader.FocusColor));
        m.Root.SetKeyboardFocus(null);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(MarkupDrawProbe.Draw(m.List), UiMarkupExpanderHeader.FocusColor));
    }
}
