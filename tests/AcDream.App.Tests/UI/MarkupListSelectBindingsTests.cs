using System;
using System.Collections.Generic;
using AcDream.App.UI.Markup;

namespace AcDream.App.Tests.UI;

public sealed class MarkupListSelectBindingsTests
{
    private sealed class Binding
    {
        public List<int> Rows { get; set; } = new() { 2, 0 };
        public HashSet<int> RowSet { get; } = new() { 5 };
        public List<int>? NullRows { get; set; }
        public int One { get; } = 1;
        public IReadOnlyList<float> Floats { get; } = new[] { 1f };
        public IReadOnlyList<int>? LastRows { get; private set; }
        public Action<IReadOnlyList<int>> SetRows => r => LastRows = r;
        public Action<int> WrongAction => _ => { };
    }

    [Fact]
    public void IntListReader_ReadsLiveListWithoutCopying()
    {
        var b = new Binding();
        var read = MarkupBindings.BindRequiredIntListReader("{Rows}", b, "list selected");
        Assert.Same(b.Rows, read());
        b.Rows = new List<int> { 3 };
        Assert.Equal(new[] { 3 }, read());
    }

    [Fact]
    public void IntListReader_CopiesOtherEnumerablesAndReadsNullAsEmpty()
    {
        var b = new Binding();
        Assert.Equal(new[] { 5 }, MarkupBindings.BindRequiredIntListReader("{RowSet}", b, "list selected")());
        Assert.Empty(MarkupBindings.BindRequiredIntListReader("{NullRows}", b, "list selected")());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("3")]
    [InlineData("{Missing}")]
    [InlineData("{One}")]
    [InlineData("{Floats}")]
    public void IntListReader_ThrowsNamingContext(string? expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindRequiredIntListReader(expression, new Binding(), "list selected"));
        Assert.Contains("list selected", ex.Message);
    }

    [Fact]
    public void IntListAction_MissingIsNullAndBoundInvokes()
    {
        var b = new Binding();
        Assert.Null(MarkupBindings.BindIntListAction(null, b, "list onchange"));
        MarkupBindings.BindIntListAction("{SetRows}", b, "list onchange")!(new[] { 4, 1 });
        Assert.Equal(new[] { 4, 1 }, b.LastRows);
    }

    [Theory]
    [InlineData("SetRows")]
    [InlineData("{Missing}")]
    [InlineData("{WrongAction}")]
    public void IntListAction_ThrowsNamingContextAndType(string expression)
    {
        var ex = Assert.Throws<FormatException>(() =>
            MarkupBindings.BindIntListAction(expression, new Binding(), "list onchange"));
        Assert.Contains("list onchange", ex.Message);
        Assert.Contains("Action<IReadOnlyList<int>>", ex.Message);
    }
}
