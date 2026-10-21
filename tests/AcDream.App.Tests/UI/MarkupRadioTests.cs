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
}
