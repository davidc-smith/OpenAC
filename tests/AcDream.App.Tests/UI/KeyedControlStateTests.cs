using AcDream.App.UI.Markup;
using Xunit;

namespace AcDream.App.Tests.UI;

public sealed class KeyedControlStateTests
{
    private sealed class Fake : IMarkupControlState
    {
        public Dictionary<(string, string), string> Values { get; } = new();
        public int Generation { get; set; } = 7;
        public string? Get(string control, string kind) => Values.GetValueOrDefault((control, kind));
        public void Set(string control, string kind, string value) => Values[(control, kind)] = value;
        public void Remove(string control, string kind) => Values.Remove((control, kind));
    }

    [Fact]
    public void Names_are_stored_with_the_key()
    {
        var inner = new Fake();
        var keyed = new KeyedControlState(inner, "k1");

        keyed.Set("box", "text", "v");

        Assert.Equal("v", inner.Values[("box#k1", "text")]);
        Assert.Equal("v", keyed.Get("box", "text"));
        Assert.Null(new KeyedControlState(inner, "k2").Get("box", "text"));

        keyed.Remove("box", "text");
        Assert.Empty(inner.Values);
    }

    [Fact]
    public void Nested_keys_join_outermost_first_with_one_hash()
    {
        var inner = new Fake();
        var nested = new KeyedControlState(new KeyedControlState(inner, "k1"), "k2");

        nested.Set("a", "kind", "v");

        Assert.Equal("v", inner.Values[("a#k1/k2", "kind")]);
    }

    [Fact]
    public void Generation_passes_through()
    {
        var inner = new Fake();
        var keyed = new KeyedControlState(inner, "k");
        Assert.Equal(7, keyed.Generation);
        inner.Generation = 8;
        Assert.Equal(8, keyed.Generation);
    }
}
