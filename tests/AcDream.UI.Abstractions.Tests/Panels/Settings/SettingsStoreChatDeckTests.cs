using System.IO;
using System.Text.Json.Nodes;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class SettingsStoreChatDeckTests : System.IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"acdream-settings-test-{System.Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private JsonObject Root() => (JsonObject)JsonNode.Parse(File.ReadAllText(_path))!;

    [Fact]
    public void Missing_ReturnsNull()
    {
        var store = new SettingsStore(_path);
        Assert.Null(store.LoadChatDeck("Davian"));
        store.SaveCharacter("Davian", CharacterSettings.Default);
        Assert.Null(store.LoadChatDeck("Davian"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsTheObject()
    {
        var store = new SettingsStore(_path);
        store.SaveChatDeck("Davian", """{"version":1,"tabs":[],"extra":{"a":2}}""");

        string? json = store.LoadChatDeck("Davian");
        var loaded = (JsonObject)JsonNode.Parse(json!)!;
        Assert.Equal(1, (int)loaded["version"]!);
        Assert.Equal(2, (int)loaded["extra"]!["a"]!);
    }

    [Fact]
    public void SaveCharacter_KeepsChatDeck_AndSaveChatDeck_KeepsCharacterKeys()
    {
        var store = new SettingsStore(_path);
        store.SaveCharacter("Davian", CharacterSettings.Default with { AutoAttack = true });
        store.SaveChatDeck("Davian", """{"version":1}""");
        store.SaveCharacter("Davian", CharacterSettings.Default with { ConfirmSalvage = false });

        Assert.NotNull(store.LoadChatDeck("Davian"));
        Assert.False(store.LoadCharacter("Davian").ConfirmSalvage);
        store.SaveChatDeck("Davian", """{"version":2}""");
        Assert.False(store.LoadCharacter("Davian").ConfirmSalvage);
        Assert.Equal(2, (int)Root()["character"]!["Davian"]!["chatDeck"]!["version"]!);
    }

    [Fact]
    public void SaveCharacter_KeepsUnknownCharacterKeys()
    {
        var store = new SettingsStore(_path);
        store.SaveCharacter("Davian", CharacterSettings.Default);
        JsonObject root = Root();
        root["character"]!["Davian"]!["fromNewerBuild"] = "kept";
        File.WriteAllText(_path, root.ToJsonString());

        store.SaveCharacter("Davian", CharacterSettings.Default with { AutoAttack = true });
        Assert.Equal("kept", (string)Root()["character"]!["Davian"]!["fromNewerBuild"]!);
    }

    [Fact]
    public void OtherCharactersAndSections_AreKept()
    {
        var store = new SettingsStore(_path);
        store.SaveChat(ChatSettings.Default);
        store.SaveChatDeck("Bob", """{"version":1}""");
        store.SaveChatDeck("Davian", """{"version":1}""");
        Assert.NotNull(store.LoadChatDeck("Bob"));
        Assert.NotNull(Root()["chat"]);
    }

    [Fact]
    public void NotAnObject_ThrowsAndWritesNothing()
    {
        var store = new SettingsStore(_path);
        Assert.Throws<System.ArgumentException>(() => store.SaveChatDeck("Davian", "[1,2]"));
        Assert.Throws<System.ArgumentException>(() => store.SaveChatDeck("Davian", "{oops"));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void UnparseableFile_LoadsNull()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Null(new SettingsStore(_path).LoadChatDeck("Davian"));
    }
}
