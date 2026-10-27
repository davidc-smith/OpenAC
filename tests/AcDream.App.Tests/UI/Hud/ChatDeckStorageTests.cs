using System.Text.Json.Nodes;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckStorageTests
{
    private sealed class MemoryStore : IChatDeckStore
    {
        public readonly Dictionary<string, string> Saved = [];
        public int Saves;
        public Exception? Throw;
        public string? Load(string toonKey) => Saved.TryGetValue(toonKey, out string? json) ? json : null;
        public void Save(string toonKey, string json)
        {
            if (Throw is not null) throw Throw;
            Saves++;
            Saved[toonKey] = json;
        }
    }

    private static (ChatDeckStorage Storage, MemoryStore Store, List<string> Log) Make()
    {
        var store = new MemoryStore();
        var log = new List<string>();
        return (new ChatDeckStorage(store, log.Add), store, log);
    }

    [Fact]
    public void NothingSaved_LoadsDefaultsQuietly()
    {
        var (storage, _, log) = Make();
        ChatDeckSettings settings = storage.Load("Davian");
        Assert.Equal(ChatDeckDefaults.Create().Tabs.Select(t => t.Id), settings.Tabs.Select(t => t.Id));
        Assert.Empty(log);
    }

    [Fact]
    public void Malformed_LoadsDefaults_LogsOnce_LeavesTheTextAlone()
    {
        var (storage, store, log) = Make();
        store.Saved["Davian"] = "{\"tabs\": 5, ";
        storage.Load("Davian");
        Assert.Single(log);
        Assert.Contains("Davian", log[0]);
        Assert.Equal("{\"tabs\": 5, ", store.Saved["Davian"]);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public void UnknownKeys_SurviveLoadAndSave()
    {
        var (storage, store, _) = Make();
        string first = ChatDeckSettingsJson.Serialize(ChatDeckDefaults.Create(), previousJson: null);
        var root = (JsonObject)JsonNode.Parse(first)!;
        root["fromNewerBuild"] = 7;
        store.Saved["Davian"] = root.ToJsonString();

        ChatDeckSettings settings = storage.Load("Davian");
        Assert.True(storage.Save("Davian", settings with { Layout = settings.Layout with { LastTabId = "combat" } }));
        var saved = (JsonObject)JsonNode.Parse(store.Saved["Davian"])!;
        Assert.Equal(7, (int)saved["fromNewerBuild"]!);
        Assert.Equal("combat", (string)saved["layout"]!["lastTab"]!);
    }

    [Fact]
    public void OverSize_IsRefused_LoggedOncePerSession()
    {
        var (storage, store, log) = Make();
        storage.Load("Davian");
        ChatDeckSettings huge = ChatDeckDefaults.Create() with
        {
            Rules = Enumerable.Range(0, ChatDeckLimits.MaxRules).Select(i => new ChatDeckRule
            {
                Id = "r" + i, Kind = ChatDeckMatchKind.Contains, Pattern = new string('x', 2000),
                Action = ChatDeckRuleAction.Hide,
            }).ToList(),
        };

        Assert.False(storage.Save("Davian", huge));
        Assert.False(storage.Save("Davian", huge));
        Assert.Equal(0, store.Saves);
        Assert.Single(log);

        storage.Load("Davian");
        Assert.False(storage.Save("Davian", huge));
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void StoreFailure_IsCaughtAndLogged()
    {
        var (storage, store, log) = Make();
        storage.Load("Davian");
        store.Throw = new IOException("disk full");
        Assert.False(storage.Save("Davian", ChatDeckDefaults.Create()));
        Assert.Contains(log, l => l.Contains("disk full"));
    }

    [Fact]
    public void AnyStoreFailure_IsCaughtAndLogged()
    {
        var (storage, store, log) = Make();
        storage.Load("Davian");
        store.Throw = new InvalidOperationException("store closed");
        Assert.False(storage.Save("Davian", ChatDeckDefaults.Create()));
        Assert.Contains(log, l => l.Contains("store closed"));
    }

    [Fact]
    public void NoStore_DefaultsAndNoSave()
    {
        var log = new List<string>();
        var storage = new ChatDeckStorage(null, log.Add);
        Assert.NotEmpty(storage.Load("Davian").Tabs);
        Assert.False(storage.Save("Davian", ChatDeckDefaults.Create()));
        Assert.Empty(log);
    }
}
