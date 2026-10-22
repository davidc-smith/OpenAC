using System.Numerics;
using AcDream.App.UI;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public class MarkupCheckboxTests
{
    private sealed class Binding
    {
        public bool Loot { get; set; }
        public bool? Mixed { get; set; }
        public int NotBool { get; set; }
        public int Calls { get; private set; }
        public bool? LastChange { get; private set; }
        public Action<bool> SetLoot => v => { Loot = v; LastChange = v; Calls++; };
        public Action<bool> SetMixed => v => { Mixed = v; LastChange = v; Calls++; };
        public Action<int> WrongAction => _ => { };
        public IReadOnlyList<bool> Flags { get; set; } = [true, false];
        public IReadOnlyList<string> Names { get; set; } = ["Drudge", "Mite"];
        public int Toggled { get; private set; } = -1;
        public Action<int> ToggleFlag => row => Toggled = row;
    }

    [Fact]
    public void CheckState_ReadsBoolAndNullableBool()
    {
        var binding = new Binding { Loot = true, Mixed = null };
        Assert.True(MarkupBindings.BindRequiredNullableBoolReader("{Loot}", MarkupBindingSource.For(binding), "ctx")());
        Assert.Null(MarkupBindings.BindRequiredNullableBoolReader("{Mixed}", MarkupBindingSource.For(binding), "ctx")());
        binding.Mixed = false;
        Assert.False(MarkupBindings.BindRequiredNullableBoolReader("{Mixed}", MarkupBindingSource.For(binding), "ctx")());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("{NotBool}")]
    [InlineData("{Missing}")]
    public void CheckState_RejectsAnythingButABoolBinding(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredNullableBoolReader(expression, MarkupBindingSource.For(new Binding()), "checkbox checked"));
        Assert.Contains("checkbox checked", ex.Message);
    }

    [Fact]
    public void BoolAction_MissingIsNullAndBoundInvokes()
    {
        var binding = new Binding();
        Assert.Null(MarkupBindings.BindBoolAction(null, MarkupBindingSource.For(binding), "ctx"));
        MarkupBindings.BindBoolAction("{SetLoot}", MarkupBindingSource.For(binding), "ctx")!(true);
        Assert.True(binding.Loot);
    }

    [Theory]
    [InlineData("SetLoot")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void BoolAction_RejectsLiteralsAndWrongTypes(string expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindBoolAction(expression, MarkupBindingSource.For(new Binding()), "checkbox onchange"));
        Assert.Contains("checkbox onchange", ex.Message);
    }

    private static UiMarkupCheckbox BuildOne(string attributes, Binding binding)
    {
        var panel = MarkupDocument.Build(
            $"<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\"><checkbox x=\"4\" y=\"4\" w=\"120\" h=\"16\" text=\"Loot rares\" {attributes}/></panel>",
            binding, _ => (1u, 16, 16));
        return Assert.IsType<UiMarkupCheckbox>(panel.Children[0]);
    }

    private static void Click(UiMarkupCheckbox box) =>
        box.OnEvent(new UiEvent { Type = UiEventType.Click });

    [Fact]
    public void Checkbox_ReportsTheNewValue()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\" onchange=\"{SetLoot}\"", binding);
        Assert.False(box.State);
        Click(box);
        Assert.Equal(true, binding.LastChange);
        Assert.True(box.State);
        Click(box);
        Assert.Equal(false, binding.LastChange);
        Assert.Equal(2, binding.Calls);
    }

    [Fact]
    public void Checkbox_ClickThroughUiRootActivatesWithoutTakingFocus()
    {
        var binding = new Binding();
        var panel = MarkupDocument.Build(
            "<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\"><checkbox x=\"4\" y=\"4\" w=\"120\" h=\"16\" text=\"Loot rares\" checked=\"{Loot}\" onchange=\"{SetLoot}\"/></panel>",
            binding, _ => (1u, 16, 16));
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(panel);
        root.OnMouseDown(UiMouseButton.Left, 20, 12);
        root.OnMouseUp(UiMouseButton.Left, 20, 12);
        Assert.Equal(true, binding.LastChange);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Checkbox_CheckedUnderPluginThemeDrawsAnAccentBoxWithATick()
    {
        var box = new UiMarkupCheckbox
        {
            Width = 120f, Height = 16f, Text = "Loot",
            ThemePalette = PluginUiPalette.Moss,
        };
        box.CheckedSource = () => true;
        var renderer = MarkupDrawProbe.Draw(box);
        // The modern rounded box (PluginUiStyle.Check): an accent box with a tick in the field colour.
        Assert.Contains(ThemeDrawCapture.Vertices(renderer), v => Vector4.Distance(v.Color, PluginUiPalette.Moss.Accent) < 0.01f);
        Assert.Contains(ThemeDrawCapture.Vertices(renderer), v => Vector4.Distance(v.Color, PluginUiPalette.Moss.Field) < 0.01f);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiCheckBoxGlyph.ClassicMark));
    }

    [Fact]
    public void Checkbox_IndeterminateClicksToTrue()
    {
        var binding = new Binding { Mixed = null };
        var box = BuildOne("checked=\"{Mixed}\" onchange=\"{SetMixed}\"", binding);
        Assert.Null(box.State);
        Click(box);
        Assert.Equal(true, binding.LastChange);
    }

    [Theory]
    [InlineData("")]
    [InlineData("checked=\"{NotBool}\"")]
    [InlineData("checked=\"{Loot}\" onchange=\"{WrongAction}\"")]
    public void Checkbox_BadBindingsThrowAtBuild(string attributes)
    {
        Assert.Throws<FormatException>(() => BuildOne(attributes, new Binding()));
    }

    [Fact]
    public void Checkbox_WithoutOnchangeIsDisplayOnly()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\"", binding);
        Click(box);
        Assert.False(binding.Loot);
    }

    [Fact]
    public void Checkbox_DisabledFiresNothing()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\" onchange=\"{SetLoot}\" enabled=\"false\"", binding);
        Click(box);
        box.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Silk.NET.Input.Key.Space });
        Assert.Equal(0, binding.Calls);
    }

    [Fact]
    public void Checkbox_FocusedSpaceAndEnterActivate()
    {
        var binding = new Binding();
        var box = BuildOne("checked=\"{Loot}\" onchange=\"{SetLoot}\"", binding);
        Assert.True(box.TabStop);
        box.OnEvent(new UiEvent { Type = UiEventType.FocusGained });
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Silk.NET.Input.Key.Space });
        Assert.True(binding.Loot);
        box.OnEvent(new UiEvent { Type = UiEventType.KeyDown, Data0 = (int)Silk.NET.Input.Key.Enter });
        Assert.False(binding.Loot);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 7)]
    [InlineData(null, 1)]
    public void Glyph_ClassicDrawsTickDashOrNothing(bool? state, int markQuads)
    {
        var box = new UiMarkupCheckbox { Width = 120f, Height = 16f, CheckedSource = () => state };
        var renderer = MarkupDrawProbe.Draw(box);
        Assert.Equal(markQuads, MarkupDrawProbe.CountQuads(renderer, UiCheckBoxGlyph.ClassicMark));
    }

    [Fact]
    public void Checkbox_PluginThemeUsesThePalette()
    {
        var themes = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = MarkupDocument.Build(
            "<panel theme=\"plugin\" x=\"0\" y=\"0\" w=\"200\" h=\"100\">"
            + "<checkbox x=\"4\" y=\"4\" w=\"120\" h=\"16\" text=\"Loot\" checked=\"{Loot}\"/></panel>",
            new Binding(), _ => (1u, 16, 16), themes: themes);
        var box = Assert.IsType<UiMarkupCheckbox>(panel.Children[^1]);
        Assert.Same(PluginUiPalette.Moss, box.ThemePalette);
        var renderer = MarkupDrawProbe.Draw(box);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, PluginUiPalette.Moss.Field) >= 1);
        Assert.Equal(0, MarkupDrawProbe.CountQuads(renderer, UiCheckBoxGlyph.ClassicFill));
    }

    private static UiMarkupList BuildList(string columnType, Binding binding, string extra = "")
    {
        var panel = MarkupDocument.Build(
            "<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\">"
            + "<list x=\"0\" y=\"0\" w=\"120\" h=\"60\" rowheight=\"18\" selected=\"{NotBool}\">"
            + $"<column type=\"{columnType}\" width=\"20\" {extra}/>"
            + "<column type=\"text\" items=\"{Names}\"/>"
            + "</list></panel>",
            binding, _ => (1u, 16, 16));
        return Assert.IsType<UiMarkupList>(panel.Children[0]);
    }

    [Theory]
    [InlineData("checkbox", true)]
    [InlineData("check", false)]
    public void Column_CheckboxIsABoxedCheckColumn(string type, bool boxed)
    {
        var list = BuildList(type, new Binding(), "values=\"{Flags}\" onchange=\"{ToggleFlag}\"");
        var column = list.Columns![0];
        Assert.Equal(UiMarkupListColumnKind.Check, column.Kind);
        Assert.Equal(boxed, column.Boxed);
    }

    [Theory]
    [InlineData("onchange=\"{ToggleFlag}\"")]
    [InlineData("values=\"{Flags}\"")]
    public void Column_CheckboxRequiresValuesAndOnchange(string extra)
    {
        var ex = Assert.Throws<FormatException>(() => BuildList("checkbox", new Binding(), extra));
        Assert.Contains("type=\"checkbox\"", ex.Message);
    }

    [Theory]
    [InlineData("checkbox", 7)]
    [InlineData("check", 1)]
    public void Column_DrawsTickBoxesOrLamps(string type, int markQuads)
    {
        var list = BuildList(type, new Binding(), "values=\"{Flags}\" onchange=\"{ToggleFlag}\"");
        var renderer = MarkupDrawProbe.Draw(list);
        Assert.Equal(markQuads, MarkupDrawProbe.CountQuads(renderer, UiCheckBoxGlyph.ClassicMark));
    }

    [Fact]
    public void Column_CheckboxClickReportsTheRow()
    {
        var binding = new Binding();
        var list = BuildList("checkbox", binding, "values=\"{Flags}\" onchange=\"{ToggleFlag}\"");
        MarkupDrawProbe.Draw(list); // frame order: draw, then handle input
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 8, Data2 = 18 + 6 });
        Assert.Equal(1, binding.Toggled);
    }

    [Fact]
    public void Column_UnknownTypeListsCheckbox()
    {
        var ex = Assert.Throws<FormatException>(() => BuildList("lamp", new Binding()));
        Assert.Contains("text, check, checkbox, icon, or button", ex.Message);
    }
}
