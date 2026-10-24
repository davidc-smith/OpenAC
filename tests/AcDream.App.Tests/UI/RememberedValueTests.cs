using System;
using System.IO;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public class RememberedValueTests
{
    private string _character = "alice";

    private (MarkupControlStateStore Store, IMarkupControlState State) NewState()
    {
        var store = new MarkupControlStateStore(null, () => _character);
        return (store, store.ForWindow("nav"));
    }

    [Fact]
    public void RefreshReportsTheFirstReadAndEachCharacterSwitch()
    {
        var (_, state) = NewState();
        state.Set("adv", "expanded", "false");
        var value = new RememberedValue(state, "adv", "expanded");
        Assert.True(value.TryRefresh(out string? first));
        Assert.Equal("false", first);
        Assert.False(value.TryRefresh(out _));

        _character = "bob";
        Assert.True(value.TryRefresh(out string? bob));
        Assert.Null(bob);
        Assert.False(value.TryRefresh(out _));
    }

    [Fact]
    public void RememberStoresAndNullRemoves()
    {
        var (_, state) = NewState();
        var value = new RememberedValue(state, "adv", "expanded");
        value.Remember("false");
        Assert.Equal("false", state.Get("adv", "expanded"));
        value.Remember(null);
        Assert.Null(state.Get("adv", "expanded"));
    }

    [Fact]
    public void RememberWritesToTheCurrentCharacter()
    {
        var (_, state) = NewState();
        var value = new RememberedValue(state, "adv", "expanded");
        value.TryRefresh(out _);
        _character = "bob";
        value.Remember("false");
        Assert.Equal("false", state.Get("adv", "expanded"));
        _character = "alice";
        Assert.Null(state.Get("adv", "expanded"));
    }

    [Fact]
    public void RememberBeforeTheFirstRefreshStillReportsTheFirstRefresh()
    {
        var (_, state) = NewState();
        var value = new RememberedValue(state, "adv", "expanded");
        value.Remember("false");
        Assert.True(value.TryRefresh(out string? first));
        Assert.Equal("false", first);
    }

    [Fact]
    public void RememberAfterACharacterSwitchStillReportsTheSwitch()
    {
        var (_, state) = NewState();
        state.Set("adv", "expanded", "true");
        var value = new RememberedValue(state, "adv", "expanded");
        Assert.True(value.TryRefresh(out string? alice));
        Assert.Equal("true", alice);
        _character = "bob";
        value.Remember("false");
        Assert.True(value.TryRefresh(out string? bob));
        Assert.Equal("false", bob);
        Assert.False(value.TryRefresh(out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void WithoutANameNothingIsRemembered(string? control)
    {
        string path = Path.Combine(Path.GetTempPath(), $"remembered-{Guid.NewGuid():N}.json");
        try
        {
            var store = new MarkupControlStateStore(new SettingsStore(path), () => _character);
            var state = store.ForWindow("nav");
            var value = new RememberedValue(state, control, "expanded");
            Assert.False(value.IsActive);
            Assert.False(value.TryRefresh(out _));
            value.Remember("false");
            store.Save();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WithoutStateNothingIsRemembered()
    {
        var value = new RememberedValue(null, "adv", "expanded");
        Assert.False(value.IsActive);
        Assert.False(value.TryRefresh(out _));
        value.Remember("false");
    }
}
