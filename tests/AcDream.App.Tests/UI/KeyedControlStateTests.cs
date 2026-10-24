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
    public void A_keys_slash_and_percent_are_escaped()
    {
        var inner = new Fake();
        new KeyedControlState(new KeyedControlState(inner, "a/b"), "50%").Set("a", "kind", "v");

        Assert.Equal("v", inner.Values[("a#a%2Fb/50%25", "kind")]);
    }

    [Fact]
    public void IsUnder_matches_a_template_name_at_a_path_or_nested_in_it()
    {
        var names = new HashSet<string> { "row" }.GetAlternateLookup<ReadOnlySpan<char>>();
        var paths = new HashSet<string> { "7", "8/2" }.GetAlternateLookup<ReadOnlySpan<char>>();

        Assert.True(KeyedControlState.IsUnder("row#7", names, paths));
        Assert.True(KeyedControlState.IsUnder("row#7/3/1", names, paths));
        Assert.True(KeyedControlState.IsUnder("row#8/2/5", names, paths));
        Assert.False(KeyedControlState.IsUnder("row#8", names, paths));
        Assert.False(KeyedControlState.IsUnder("row#70", names, paths));
        Assert.False(KeyedControlState.IsUnder("other#7", names, paths));
        Assert.False(KeyedControlState.IsUnder("row", names, paths));
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
