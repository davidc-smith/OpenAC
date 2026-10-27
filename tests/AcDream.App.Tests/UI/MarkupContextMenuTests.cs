using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class MarkupContextMenuTests
{
    private sealed class Binding
    {
        public List<string> Menu { get; set; } = new() { "Rename", "-", "Delete" };
        public List<bool> MenuEnabled { get; set; } = new() { true, true, false };
        public List<int> Picks { get; } = new();
        public List<int> Opens { get; } = new();
        public int Clicks { get; private set; }
        public Action<int> OnPick => Picks.Add;
        public Action<int> OnOpen => Opens.Add;
        public Action OnClick => () => Clicks++;
        public Action<string> WrongType => _ => { };
        public int NotAList { get; set; }
        public int Row { get; set; }
        public List<string> Names { get; set; } = new() { "a", "b" };
    }

    private const string Pick = "onselect=\"{OnPick}\"";

    private static UiPanel Build(string body, Binding binding, PluginUiThemeSettings? themes = null)
    {
        string theme = themes is null ? "" : "theme=\"plugin\"";
        return MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"400\" h=\"300\" {theme}>{body}</panel>",
            binding, id => (id, 16, 16), themes: themes);
    }

    private static (UiRoot Root, UiPanel Panel) Mount(string body, Binding binding)
    {
        var panel = Build(body, binding);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, panel);
    }

    private static string Button(string menu) =>
        $"<button x=\"10\" y=\"10\" w=\"80\" h=\"20\" text=\"Go\" onclick=\"{{OnClick}}\">{menu}</button>";

    private static void RightClick(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void Button_RightClickOpens_AndAPickReportsTheIndex()
    {
        var b = new Binding();
        var (root, panel) = Mount(Button($"<contextmenu items=\"{{Menu}}\" {Pick}/>"), b);
        var button = panel.Children.OfType<UiSimpleButton>().Single();
        var menu = button.Children.OfType<UiMarkupContextMenu>().Single();
        Assert.Same(menu, button.ContextMenu);
        Assert.DoesNotContain(panel.Children, child => child is UiMarkupContextMenu);

        RightClick(root, 20, 15);
        Assert.True(menu.IsOpen);
        Click(root, 30, 27);              // Rename: 15 + 3 .. 15 + 21
        Assert.Equal(new[] { 0 }, b.Picks);

        Click(root, 20, 15);
        Assert.Equal(1, b.Clicks);        // the button still clicks
    }

    [Fact]
    public void LiteralItems_Work()
    {
        var b = new Binding();
        var (root, _) = Mount(Button($"<contextmenu items=\"Copy|-|Paste\" {Pick}/>"), b);
        RightClick(root, 20, 15);
        Click(root, 30, 52);              // Paste: 15 + 3 + 18 + 7 = 43 .. 61
        Assert.Equal(new[] { 2 }, b.Picks);
    }

    [Fact]
    public void EnabledList_DisablesItems()
    {
        var b = new Binding();
        var (root, panel) = Mount(Button($"<contextmenu items=\"{{Menu}}\" enabled=\"{{MenuEnabled}}\" {Pick}/>"), b);
        RightClick(root, 20, 15);
        Click(root, 30, 52);              // Delete, disabled
        Assert.Empty(b.Picks);
        var button = panel.Children.OfType<UiSimpleButton>().Single();
        Assert.True(button.Children.OfType<UiMarkupContextMenu>().Single().IsOpen);
    }

    [Fact]
    public void OnOpen_OffAList_GetsMinusOne()
    {
        var b = new Binding();
        var (root, _) = Mount(Button($"<contextmenu items=\"{{Menu}}\" onopen=\"{{OnOpen}}\" {Pick}/>"), b);
        RightClick(root, 20, 15);
        Assert.Equal(new[] { -1 }, b.Opens);
    }

    [Fact]
    public void DisabledOwner_DoesNotOpen()
    {
        var b = new Binding();
        var (root, panel) = Mount(
            $"<button x=\"10\" y=\"10\" w=\"80\" h=\"20\" text=\"Go\" enabled=\"false\"><contextmenu items=\"{{Menu}}\" {Pick}/></button>", b);
        RightClick(root, 20, 15);
        var button = panel.Children.OfType<UiSimpleButton>().Single();
        Assert.False(button.Children.OfType<UiMarkupContextMenu>().Single().IsOpen);
    }

    [Fact]
    public void Group_OpensOverItsChildren()
    {
        var b = new Binding();
        var (root, panel) = Mount(
            "<group x=\"0\" y=\"0\" w=\"200\" h=\"100\">"
            + "<button x=\"10\" y=\"10\" w=\"80\" h=\"20\" text=\"Go\"/>"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></group>", b);
        var group = panel.Children.Single(child => child.ContextMenu is not null);
        RightClick(root, 20, 15);
        Assert.True(group.Children.OfType<UiMarkupContextMenu>().Single().IsOpen);
    }

    [Fact]
    public void ListAndSplit_LeaveTheMenuOutOfTheirChildren()
    {
        var b = new Binding();
        var list = Build(
            "<list x=\"0\" y=\"0\" w=\"200\" h=\"100\" selected=\"{Row}\"><column type=\"text\" items=\"{Names}\"/>"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></list>", b)
            .Children.OfType<UiMarkupList>().Single();
        Assert.Single(list.Columns!);
        Assert.NotNull(list.ContextMenu);

        var split = Build(
            "<split x=\"0\" y=\"0\" w=\"200\" h=\"100\"><group x=\"0\" y=\"0\" w=\"10\" h=\"10\"/><group x=\"0\" y=\"0\" w=\"10\" h=\"10\"/>"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></split>", b)
            .Children.OfType<UiMarkupSplit>().Single();
        Assert.NotNull(split.ContextMenu);
    }

    [Fact]
    public void PluginTheme_SetsThePalette()
    {
        var b = new Binding();
        var panel = Build(Button($"<contextmenu items=\"{{Menu}}\" {Pick}/>"), b,
            new PluginUiThemeSettings { Theme = PluginUiTheme.Moss });
        var menu = panel.Children.OfType<UiSimpleButton>().Single().Children.OfType<UiMarkupContextMenu>().Single();
        Assert.Same(PluginUiPalette.Moss, menu.ThemePalette);
    }

    [Theory]
    [InlineData("<contextmenu items=\"{Menu}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onselect=\"{WrongType}\"/>")]
    [InlineData("<contextmenu onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"a||b\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{NotAList}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" enabled=\"{NotAList}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onopen=\"{WrongType}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onselect=\"{OnPick}\"/><contextmenu items=\"{Menu}\" onselect=\"{OnPick}\"/>")]
    [InlineData("<contextmenu items=\"{Menu}\" onselect=\"{OnPick}\"><label text=\"x\"/></contextmenu>")]
    public void BadContextMenu_FailsTheBuild(string menu)
    {
        Assert.Throws<FormatException>(() => Build(Button(menu), new Binding()));
    }

    [Fact]
    public void ContextMenuInThePanelOrAColumn_FailsTheBuild()
    {
        Assert.Throws<FormatException>(() =>
            Build($"<contextmenu items=\"{{Menu}}\" {Pick}/>", new Binding()));
        Assert.Throws<FormatException>(() => Build(
            "<list x=\"0\" y=\"0\" w=\"200\" h=\"100\"><column type=\"text\" items=\"{Names}\">"
            + $"<contextmenu items=\"{{Menu}}\" {Pick}/></column></list>", new Binding()));
    }
}
