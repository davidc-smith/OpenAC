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
        Assert.True(MarkupBindings.BindRequiredCheckState("{Loot}", binding, "ctx")());
        Assert.Null(MarkupBindings.BindRequiredCheckState("{Mixed}", binding, "ctx")());
        binding.Mixed = false;
        Assert.False(MarkupBindings.BindRequiredCheckState("{Mixed}", binding, "ctx")());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    [InlineData("{NotBool}")]
    [InlineData("{Missing}")]
    public void CheckState_RejectsAnythingButABoolBinding(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredCheckState(expression, new Binding(), "checkbox checked"));
        Assert.Contains("checkbox checked", ex.Message);
    }

    [Fact]
    public void BoolAction_MissingIsNullAndBoundInvokes()
    {
        var binding = new Binding();
        Assert.Null(MarkupBindings.BindBoolAction(null, binding, "ctx"));
        MarkupBindings.BindBoolAction("{SetLoot}", binding, "ctx")!(true);
        Assert.True(binding.Loot);
    }

    [Theory]
    [InlineData("SetLoot")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void BoolAction_RejectsLiteralsAndWrongTypes(string expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindBoolAction(expression, new Binding(), "checkbox onchange"));
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
}
