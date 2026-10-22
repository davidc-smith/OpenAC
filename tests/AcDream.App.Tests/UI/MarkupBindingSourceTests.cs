using AcDream.App.UI.Markup;
using Xunit;

namespace AcDream.App.Tests.UI;

public sealed class MarkupBindingSourceTests
{
    private class Row { public string Label { get; set; } = "a"; public Action? Click { get; set; } }
    private sealed class FancyRow : Row { public int Extra => 1; }

    [Fact]
    public void A_window_source_reads_its_object()
    {
        var row = new Row { Label = "x" };
        var source = MarkupBindingSource.For(row);
        Assert.Equal(typeof(Row), source.Type);
        Assert.Equal("x", source.Read(source.Property("Label")!));
    }

    [Fact]
    public void Names_resolve_on_the_declared_type_not_the_object()
    {
        var source = new MarkupBindingSource(typeof(Row)) { Current = new FancyRow() };
        Assert.Null(source.Property("Extra"));
        Assert.NotNull(source.Property("Label"));
    }

    [Fact]
    public void With_no_current_object_reads_are_null_and_actions_do_nothing()
    {
        var source = new MarkupBindingSource(typeof(Row));
        Assert.Null(source.Read(source.Property("Label")!));
        Assert.Null(source.Read(source.Property("Click")!));
    }

    [Fact]
    public void A_kept_source_follows_a_new_current_object()
    {
        var source = new MarkupBindingSource(typeof(Row)) { Current = new Row { Label = "old" } };
        var label = source.Property("Label")!;
        source.Current = new Row { Label = "new" };
        Assert.Equal("new", source.Read(label));
    }
}
