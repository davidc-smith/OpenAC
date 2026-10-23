using System;
using System.Collections.Generic;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListBindingsTests
{
    private sealed class Binding
    {
        public (int Column, bool Descending)? LastSort { get; private set; }
        public Action<int, bool> SetSort => (c, d) => LastSort = (c, d);
        public Action<int> WrongSort => _ => { };
        public List<float> Widths { get; } = new() { 120f, 80f };
        public IReadOnlyList<int> IntWidths { get; } = new[] { 1, 2 };
        public IReadOnlyList<float>? LastWidths { get; private set; }
        public Action<IReadOnlyList<float>> SetWidths => w => LastWidths = w;
        public IReadOnlyList<double> DistKeys { get; } = new[] { 1.5, 2.5 };
        public IReadOnlyList<string> NameKeys { get; } = new[] { "a", "b" };
        public IReadOnlyList<bool> Flags { get; } = new[] { true };
    }

    [Fact]
    public void IntBoolAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindIntBoolAction(null, MarkupBindingSource.For(b), "list onsort"));
        MarkupBindings.BindIntBoolAction("{SetSort}", MarkupBindingSource.For(b), "list onsort")!(2, true);
        Assert.Equal((2, true), b.LastSort);
    }

    [Theory]
    [InlineData("SetSort")]
    [InlineData("{WrongSort}")]
    [InlineData("{Missing}")]
    public void IntBoolAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindIntBoolAction(attribute, MarkupBindingSource.For(new Binding()), "list onsort"));
        Assert.Contains("list onsort", ex.Message);
        Assert.Contains("Action<int, bool>", ex.Message);
    }

    [Fact]
    public void FloatListReader_SnapshotsTheList()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredFloatListReader("{Widths}", MarkupBindingSource.For(b), "list widths");
        IReadOnlyList<float> first = read();
        b.Widths[0] = 99f;
        Assert.Equal(new[] { 120f, 80f }, first);
        Assert.Equal(new[] { 99f, 80f }, read());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("120,80")]
    [InlineData("{Missing}")]
    [InlineData("{IntWidths}")]
    public void FloatListReader_RejectsAbsentLiteralsAndWrongTypes(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindRequiredFloatListReader(expression, MarkupBindingSource.For(new Binding()), "list widths"));
        Assert.Contains("list widths", ex.Message);
    }

    [Fact]
    public void FloatListAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindFloatListAction(null, MarkupBindingSource.For(b), "list onresize"));
        MarkupBindings.BindFloatListAction("{SetWidths}", MarkupBindingSource.For(b), "list onresize")!(new[] { 1f });
        Assert.Equal(new[] { 1f }, b.LastWidths);
    }

    [Theory]
    [InlineData("SetWidths")]
    [InlineData("{SetSort}")]
    public void FloatListAction_RejectsLiteralsAndWrongTypes(string attribute)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindFloatListAction(attribute, MarkupBindingSource.For(new Binding()), "list onresize"));
        Assert.Contains("Action<IReadOnlyList<float>>", ex.Message);
    }

    [Fact]
    public void SortKeys_TakeDoublesOrStrings()
    {
        var b = new Binding();
        var numbers = MarkupBindings.BindSortKeys("{DistKeys}", MarkupBindingSource.For(b), "column[1] sortkeys");
        Assert.Null(numbers.Texts);
        Assert.Equal(new[] { 1.5, 2.5 }, numbers.Numbers!());
        var texts = MarkupBindings.BindSortKeys("{NameKeys}", MarkupBindingSource.For(b), "column[0] sortkeys");
        Assert.Null(texts.Numbers);
        Assert.Equal(new[] { "a", "b" }, texts.Texts!());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("DistKeys")]
    [InlineData("{Missing}")]
    [InlineData("{Flags}")]
    public void SortKeys_RejectEverythingElse(string? expression)
    {
        var ex = Assert.Throws<FormatException>(
            () => MarkupBindings.BindSortKeys(expression, MarkupBindingSource.For(new Binding()), "column[1] sortkeys"));
        Assert.Contains("column[1] sortkeys", ex.Message);
    }
}
