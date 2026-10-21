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
}
