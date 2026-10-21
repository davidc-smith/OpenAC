using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.UI;
using AcDream.App.UI.Markup;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI;

public sealed class MarkupSplitTests
{
    private sealed class Binding
    {
        public float At { get; set; } = 120f;
        public List<float> Changes { get; } = new();
        public Action<float> SetAt => v => { At = v; Changes.Add(v); };
        public Action<float> Notify => v => Changes.Add(v);
        public int NotFloat { get; set; }
        public Action<int> WrongAction => _ => { };
        public int LeftClicks { get; private set; }
        public int RightClicks { get; private set; }
        public Action LeftClick => () => LeftClicks++;
        public Action RightClick => () => RightClicks++;
    }

    [Fact]
    public void FloatReader_ReadsTheProperty()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredFloatReader("{At}", b, "split position");
        Assert.Equal(120f, read());
        b.At = 7.5f;
        Assert.Equal(7.5f, read());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12")]
    [InlineData("{Missing}")]
    [InlineData("{NotFloat}")]
    public void FloatReader_RejectsAbsentLiteralsAndWrongTypes(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredFloatReader(expression, new Binding(), "split position"));
        Assert.Contains("split position", ex.Message);
        Assert.Contains("float", ex.Message);
    }

    [Fact]
    public void FloatAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindFloatAction(null, b, "ctx"));
        MarkupBindings.BindFloatAction("{SetAt}", b, "ctx")!(3f);
        Assert.Equal(3f, b.At);
    }

    [Theory]
    [InlineData("SetAt")]
    [InlineData("{WrongAction}")]
    [InlineData("{Missing}")]
    public void FloatAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindFloatAction(attribute, new Binding(), "split onchange"));
        Assert.Contains("split onchange", ex.Message);
        Assert.Contains("Action<float>", ex.Message);
    }
}
