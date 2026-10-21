using System;
using System.IO;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class MarkupControlStateStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"markup-state-{Guid.NewGuid():N}.json");
    private string _character = "alice";

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        string blocker = _path + ".blocker";
        try
        {
            if (Directory.Exists(blocker)) Directory.Delete(blocker, true);
            else if (File.Exists(blocker)) File.Delete(blocker);
        }
        catch (IOException) { }
    }

    [Fact]
    public void SetRejectsANullValue()
    {
        var nav = NewStore().ForWindow("nav");
        Assert.Throws<ArgumentNullException>(() => nav.Set("adv", "expanded", null!));
    }

    private MarkupControlStateStore NewStore() => new(new SettingsStore(_path), () => _character);

    [Fact]
    public void ValuesAreKeptPerWindowControlAndKind()
    {
        var store = NewStore();
        var nav = store.ForWindow("nav");
        var loot = store.ForWindow("loot");
        nav.Set("adv", "expanded", "false");
        nav.Set("adv", "widths", "10");
        loot.Set("adv", "expanded", "true");
        Assert.Equal("false", nav.Get("adv", "expanded"));
        Assert.Equal("10", nav.Get("adv", "widths"));
        Assert.Equal("true", loot.Get("adv", "expanded"));
        Assert.Null(nav.Get("other", "expanded"));
    }

    [Fact]
    public void SavedValuesLoadIntoAFreshStore()
    {
        var store = NewStore();
        store.ForWindow("nav").Set("adv", "expanded", "false");
        store.Save();
        Assert.Equal("false", NewStore().ForWindow("nav").Get("adv", "expanded"));
    }

    [Fact]
    public void UnsavedValuesAreNotWritten()
    {
        NewStore().ForWindow("nav").Set("adv", "expanded", "false");
        Assert.Null(NewStore().ForWindow("nav").Get("adv", "expanded"));
    }

    [Fact]
    public void SaveWritesOnlyWhenSomethingChanged()
    {
        var store = NewStore();
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        store.Save();
        File.Delete(_path);
        store.Save();
        Assert.False(File.Exists(_path));
        nav.Set("adv", "expanded", "false");
        store.Save();
        Assert.False(File.Exists(_path));
        nav.Set("adv", "expanded", "true");
        store.Save();
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void EachCharacterHasItsOwnValues()
    {
        var store = NewStore();
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        int aliceGeneration = nav.Generation;

        _character = "bob";
        Assert.Null(nav.Get("adv", "expanded"));
        Assert.True(nav.Generation > aliceGeneration);
        nav.Set("adv", "expanded", "true");

        _character = "alice";
        Assert.Equal("false", nav.Get("adv", "expanded"));
        store.Save();

        var fresh = NewStore().ForWindow("nav");
        Assert.Equal("false", fresh.Get("adv", "expanded"));
        _character = "bob";
        Assert.Equal("true", fresh.Get("adv", "expanded"));
    }

    [Fact]
    public void GenerationIsStableForTheSameCharacter()
    {
        var nav = NewStore().ForWindow("nav");
        int first = nav.Generation;
        nav.Set("adv", "expanded", "false");
        Assert.Equal(first, nav.Generation);
    }

    [Fact]
    public void WithoutASettingsFileValuesLastTheSession()
    {
        var store = new MarkupControlStateStore(null, () => "alice");
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        store.Save();
        Assert.Equal("false", nav.Get("adv", "expanded"));
    }

    [Fact]
    public void ABlankCharacterIsNeverWritten()
    {
        _character = "";
        var store = NewStore();
        store.ForWindow("nav").Set("adv", "expanded", "false");
        store.Save();
        Assert.False(File.Exists(_path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ABlankWindowNameThrows(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => NewStore().ForWindow(name));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    public void TheHostsNoCharacterKeyIsNeverWritten(string key)
    {
        _character = key;
        var store = NewStore();
        store.ForWindow("nav").Set("adv", "expanded", "false");
        store.Save();
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void AFailedWriteKeepsTheValueAndDoesNotThrow()
    {
        string blocker = _path + ".blocker";
        File.WriteAllText(blocker, "x");
        try
        {
            var store = new MarkupControlStateStore(
                new SettingsStore(Path.Combine(blocker, "settings.json")), () => "alice");
            var nav = store.ForWindow("nav");
            nav.Set("adv", "expanded", "false");
            store.Save();
            Assert.Equal("false", nav.Get("adv", "expanded"));

            // Once the obstruction is gone, the next Save retries and succeeds.
            File.Delete(blocker);
            store.Save();
            Assert.True(File.Exists(Path.Combine(blocker, "settings.json")));
        }
        finally
        {
            if (Directory.Exists(blocker)) Directory.Delete(blocker, true);
            else if (File.Exists(blocker)) File.Delete(blocker);
        }
    }

    [Fact]
    public void AnUnreadableFileIsNeverOverwritten()
    {
        File.WriteAllText(_path, "{ not json");
        var store = NewStore();
        var nav = store.ForWindow("nav");
        nav.Set("adv", "expanded", "false");
        store.Save();
        Assert.Equal("false", nav.Get("adv", "expanded"));
        Assert.Equal("{ not json", File.ReadAllText(_path));
    }

    [Theory]
    [InlineData("", "k")]
    [InlineData(" ", "k")]
    [InlineData("c", "")]
    [InlineData("c", " ")]
    public void ABlankControlOrKindThrows(string control, string kind)
    {
        var nav = NewStore().ForWindow("nav");
        Assert.ThrowsAny<ArgumentException>(() => nav.Get(control, kind));
        Assert.ThrowsAny<ArgumentException>(() => nav.Set(control, kind, "v"));
    }

    [Fact]
    public void SaveAfterACharacterChangeStillWritesTheOldCharacter()
    {
        var store = NewStore();
        store.ForWindow("nav").Set("adv", "expanded", "false");
        _character = "bob";
        store.Save();
        _character = "alice";
        Assert.Equal("false", NewStore().ForWindow("nav").Get("adv", "expanded"));
        _character = "bob";
        Assert.Null(NewStore().ForWindow("nav").Get("adv", "expanded"));
    }
}
