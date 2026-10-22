using System;
using System.Collections.Generic;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public sealed class MarkupTreeBindingsTests
{
    private sealed class Binding
    {
        public HashSet<string> Set { get; set; } = new() { "a" };
        public List<string> List { get; } = new() { "b", "b" };
        public List<string>? Nothing { get; set; }
        public int One { get; } = 1;
        public IReadOnlyList<int> Ints { get; } = new[] { 1 };
        public (string, bool)? Last { get; private set; }
        public Action<string, bool> SetExpand => (k, v) => Last = (k, v);
        public Action<int> Wrong => _ => { };
    }

    [Fact]
    public void StringSetReader_ReadsALiveSetWithoutCopying()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredStringSetReader("{Set}", MarkupBindingSource.For(b), "list expanded");
        Assert.Same(b.Set, read());
        b.Set = new HashSet<string> { "z" };
        Assert.Contains("z", read());
    }

    [Fact]
    public void StringSetReader_CopiesOtherEnumerablesAndReadsNullAsEmpty()
    {
        var b = new Binding();
        var set = MarkupBindings.BindRequiredStringSetReader("{List}", MarkupBindingSource.For(b), "list expanded")();
        Assert.Single(set);
        Assert.Contains("b", set);
        Assert.False(set.Contains("B"));   // ordinal
        Assert.Empty(MarkupBindings.BindRequiredStringSetReader("{Nothing}", MarkupBindingSource.For(b), "list expanded")());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("a")]
    [InlineData("{Missing}")]
    [InlineData("{One}")]
    [InlineData("{Ints}")]
    public void StringSetReader_ThrowsNamingContext(string? expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindRequiredStringSetReader(expression, MarkupBindingSource.For(new Binding()), "list expanded"));
        Assert.Contains("list expanded", ex.Message);
    }

    [Fact]
    public void StringBoolAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindStringBoolAction(null, MarkupBindingSource.For(b), "list onexpand"));
        MarkupBindings.BindStringBoolAction("{SetExpand}", MarkupBindingSource.For(b), "list onexpand")!("k", true);
        Assert.Equal(("k", true), b.Last);
    }

    [Theory]
    [InlineData("SetExpand")]
    [InlineData("{Missing}")]
    [InlineData("{Wrong}")]
    public void StringBoolAction_ThrowsNamingContextAndType(string expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindStringBoolAction(expression, MarkupBindingSource.For(new Binding()), "list onexpand"));
        Assert.Contains("list onexpand", ex.Message);
        Assert.Contains("Action<string, bool>", ex.Message);
    }
}
