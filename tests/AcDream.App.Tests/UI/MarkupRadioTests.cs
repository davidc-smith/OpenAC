using System.Linq;
using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public class MarkupRadioTests
{
    private sealed class Binding
    {
        public int Mode { get; set; }
        public int Calls { get; private set; }
        public int LastChange { get; private set; } = -1;
        public Action<int> SetMode => v => { Mode = v; LastChange = v; Calls++; };
        public IReadOnlyList<string> Modes { get; set; } = ["Melee", "Missile", "Magic"];
        public bool NotInt { get; set; }
        public Action<string> WrongAction => _ => { };
    }

    [Fact]
    public void IntAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindIntAction(null, b, "ctx"));
        MarkupBindings.BindIntAction("{SetMode}", b, "ctx")!(2);
        Assert.Equal(2, b.Mode);
    }

    [Theory]
    [InlineData("SetMode")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void IntAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindIntAction(attribute, new Binding(), "radio onchange"));
        Assert.Contains("radio onchange", ex.Message);
        Assert.Contains("Action<int>", ex.Message);
    }

    private const string Literal = "items=\"Melee|Missile|Magic\" selected=\"{Mode}\" onchange=\"{SetMode}\"";

    // The radio sits at (8, 8). Vertical 200×54: rows 18 px tall (y 8, 26, 44).
    // Horizontal 210×20: cells 70 px wide (x 8, 78, 148).
    private static (UiRoot Root, UiMarkupRadio Radio) Mount(
        string attributes, Binding binding, string size = "w=\"200\" h=\"54\"")
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"300\" h=\"100\"><radio x=\"8\" y=\"8\" {size} {attributes}/></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        return (root, Assert.IsType<UiMarkupRadio>(panel.Children[0]));
    }

    private static void Click(UiRoot root, int x, int y)
    {
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    private static void Focus(UiMarkupRadio radio) =>
        radio.OnEvent(new UiEvent { Type = UiEventType.FocusGained });

    private static void Press(UiMarkupRadio radio, Key key) =>
        radio.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)key });

    [Fact]
    public void Radio_LiteralItemsAndBoundSelection()
    {
        var (_, radio) = Mount(Literal, new Binding { Mode = 2 });
        Assert.Equal(new[] { "Melee", "Missile", "Magic" }, radio.Items);
        Assert.Equal(2, radio.Selected);
    }

    [Fact]
    public void Radio_BoundItemsFollowTheList()
    {
        var b = new Binding();
        var (_, radio) = Mount("items=\"{Modes}\" selected=\"{Mode}\" onchange=\"{SetMode}\"", b);
        Assert.Equal(3, radio.Items.Count);
        b.Modes = ["A", "B"];
        Assert.Equal(new[] { "A", "B" }, radio.Items);
    }

    [Theory]
    [InlineData("selected=\"{Mode}\"")]
    [InlineData("items=\"\" selected=\"{Mode}\"")]
    [InlineData("items=\"{NotInt}\" selected=\"{Mode}\"")]
    [InlineData("items=\"a|b\"")]
    [InlineData("items=\"a||b\" selected=\"{Mode}\"")]
    [InlineData("items=\"a|\" selected=\"{Mode}\"")]
    [InlineData("items=\"|\" selected=\"{Mode}\"")]
    [InlineData("items=\"a|b\" selected=\"{NotInt}\"")]
    [InlineData("items=\"a|b\" selected=\"{Mode}\" onchange=\"{WrongAction}\"")]
    [InlineData("items=\"a|b\" selected=\"{Mode}\" orientation=\"diagonal\"")]
    [InlineData("items=\"a|b\" selected=\"{Mode}\" style=\"round\"")]
    public void Radio_BadAttributesThrowAtBuild(string attributes)
    {
        Assert.Throws<FormatException>(() => Mount(attributes, new Binding()));
    }

    [Fact]
    public void Radio_ClickReportsTheOptionWithoutTakingFocus()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal, b);
        Click(root, 20, 26 + 5);
        Assert.Equal(1, b.LastChange);
        Assert.Equal(1, b.Calls);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Radio_ClickingTheSelectedOptionReportsNothing()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal, b);
        Click(root, 20, 8 + 5);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_HorizontalCellsSplitTheWidth()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal + " orientation=\"horizontal\"", b, "w=\"210\" h=\"20\"");
        Click(root, 78 + 10, 15);
        Assert.Equal(1, b.LastChange);
        Click(root, 148 + 10, 15);
        Assert.Equal(2, b.LastChange);
    }

    [Fact]
    public void Radio_PressAndReleaseMustBeOnTheSameOption()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal, b);
        root.OnMouseDown(UiMouseButton.Left, 20, 26 + 5);
        root.OnMouseUp(UiMouseButton.Left, 20, 44 + 5);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_WithoutOnchangeOnlyDisplays()
    {
        var b = new Binding();
        var (root, radio) = Mount("items=\"a|b|c\" selected=\"{Mode}\"", b);
        Click(root, 20, 26 + 5);
        Assert.Equal(0, b.Mode);
        Assert.Null(radio.Changed);
    }

    [Fact]
    public void Radio_ReleaseOutsideReportsNothing()
    {
        var b = new Binding();
        var (root, _) = Mount(Literal, b);
        root.OnMouseDown(UiMouseButton.Left, 20, 31);
        root.OnMouseUp(UiMouseButton.Left, 250, 31);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_GlyphWellDoesNotOverlap()
    {
        var (_, radio) = Mount("items=\"a\" selected=\"{Mode}\"", new Binding { Mode = -1 });
        var r = MarkupDrawProbe.Draw(radio);
        Assert.Equal(3, MarkupDrawProbe.CountQuads(r, UiRadioGlyph.ClassicWell));
    }

    [Fact]
    public void Radio_DisabledReportsNothing()
    {
        var b = new Binding();
        var (root, radio) = Mount(Literal + " enabled=\"false\"", b);
        Click(root, 20, 26 + 5);
        Focus(radio);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_ArrowKeysMoveAndWrap()
    {
        var b = new Binding();
        var (_, radio) = Mount(Literal, b);
        Focus(radio);
        Press(radio, Key.Down);
        Assert.Equal(1, b.Mode);
        Press(radio, Key.Right);
        Assert.Equal(2, b.Mode);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Mode);
        Press(radio, Key.Up);
        Assert.Equal(2, b.Mode);
        Press(radio, Key.Left);
        Assert.Equal(1, b.Mode);
        Assert.Equal(5, b.Calls);
    }

    [Theory]
    [InlineData(-1, Key.Down, 0)]
    [InlineData(-1, Key.Up, 2)]
    [InlineData(5, Key.Right, 0)]
    [InlineData(5, Key.Left, 2)]
    public void Radio_ArrowsFromNoSelectionPickAnEnd(int start, Key key, int expected)
    {
        var b = new Binding { Mode = start };
        var (_, radio) = Mount(Literal, b);
        Focus(radio);
        Press(radio, key);
        Assert.Equal(expected, b.Mode);
    }

    [Fact]
    public void Radio_ArrowsNeedFocus()
    {
        var b = new Binding();
        var (_, radio) = Mount(Literal, b);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public void Radio_IsOneTabStopThatClicksDoNotFocus()
    {
        var (_, radio) = Mount(Literal, new Binding());
        Assert.True(radio.TabStop);
        Assert.True(radio.AcceptsFocus);
        Assert.False(radio.FocusOnMouseClick);
    }

    [Fact]
    public void Radio_EmptyBoundItemsIgnoreInput()
    {
        var b = new Binding { Modes = [] };
        var (root, radio) = Mount("items=\"{Modes}\" selected=\"{Mode}\" onchange=\"{SetMode}\"", b);
        Click(root, 20, 13);
        Focus(radio);
        Press(radio, Key.Down);
        Assert.Equal(0, b.Calls);
        MarkupDrawProbe.Draw(radio);
    }

    private static UiMarkupRadio MountThemed(string attributes, Binding binding)
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"300\" h=\"100\">"
            + $"<radio x=\"8\" y=\"8\" w=\"200\" h=\"54\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16), themes: themes);
        return panel.Children.OfType<UiMarkupRadio>().Single();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    public void Radio_ClassicDrawsOneMarkForTheSelection(int selected, int marks)
    {
        var (_, radio) = Mount(Literal, new Binding { Mode = selected });
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(marks, MarkupDrawProbe.CountQuads(renderer, UiRadioGlyph.ClassicMark));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 0)]
    public void Radio_SegmentedFillsTheSelectedSegment(int selected, int fills)
    {
        var (_, radio) = Mount(Literal + " style=\"segmented\"", new Binding { Mode = selected });
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(fills, MarkupDrawProbe.CountQuads(renderer, UiMarkupRadio.ClassicSegmentSelected));
    }

    [Fact]
    public void Radio_DisabledDrawsFaded()
    {
        var (_, radio) = Mount(Literal + " enabled=\"false\"", new Binding { Mode = 1 });
        var renderer = MarkupDrawProbe.Draw(radio);
        var faded = UiRadioGlyph.ClassicMark with { W = UiMarkupToggle.DisabledAlpha };
        Assert.Equal(1, MarkupDrawProbe.CountQuads(renderer, faded));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiRadioGlyph.ClassicMark));
    }

    [Fact]
    public void Radio_FocusDrawsAnOutline()
    {
        var (_, radio) = Mount(Literal, new Binding());
        Focus(radio);
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(4, MarkupDrawProbe.CountQuads(renderer, UiMarkupRadio.FocusColor));
    }

    [Fact]
    public void Radio_PluginThemeUsesThePalette()
    {
        var radio = MountThemed(Literal, new Binding { Mode = 0 });
        Assert.Same(PluginUiPalette.Moss, radio.ThemePalette);
        Assert.Equal(PluginUiPalette.Moss.Text, radio.TextColor);
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.Equal(6, MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Accent));
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiRadioGlyph.ClassicMark));
    }

    [Fact]
    public void Radio_PluginThemeSegmented()
    {
        var radio = MountThemed(Literal + " style=\"segmented\"", new Binding { Mode = 1 });
        var renderer = MarkupDrawProbe.Draw(radio);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Selected) >= 1);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiMarkupRadio.ClassicSegmentSelected));
    }

    [Fact]
    public void Radio_ExplicitColorSurvivesTheTheme()
    {
        var radio = MountThemed(Literal + " color=\"#FFFF0000\"", new Binding());
        Assert.Equal(new Vector4(1f, 0f, 0f, 1f), radio.TextColor);
    }
}
