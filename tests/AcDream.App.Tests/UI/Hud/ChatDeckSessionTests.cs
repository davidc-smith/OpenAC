using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckSessionTests
{
    private sealed class MemoryStore : IChatDeckStore
    {
        public readonly Dictionary<string, string> Saved = [];
        public readonly List<string> Loads = [];
        public Exception? LoadThrow;
        public string? Load(string toonKey)
        {
            Loads.Add(toonKey);
            if (LoadThrow is not null) throw LoadThrow;
            return Saved.GetValueOrDefault(toonKey);
        }
        public void Save(string toonKey, string json) => Saved[toonKey] = json;
    }

    private sealed class Harness
    {
        public readonly ChatLog Log = new();
        public readonly MemoryStore Store = new();
        public string Key = "default";
        public bool Gameplay = true;
        public int Changes;
        public readonly List<string> Logged = [];
        public readonly ChatDeckSession Session;

        public Harness()
        {
            Session = new ChatDeckSession(Log, new ChatDeckStorage(Store, _ => { }), () => Key, () => Gameplay, Logged.Add);
            Session.ModelChanged += () => Changes++;
        }

        public void Say(string text) =>
            Log.OnLocalSpeech("Bob", text, 0x50000002u, isRanged: false, logTextType: 0x02u);
    }

    [Fact]
    public void DefaultKeyOrNoGameplay_NoModel()
    {
        var h = new Harness();
        h.Session.Tick();
        Assert.Null(h.Session.Model);
        h.Key = "Davian";
        h.Gameplay = false;
        h.Session.Tick();
        Assert.Null(h.Session.Model);
        Assert.Empty(h.Store.Loads);
    }

    [Fact]
    public void RealCharacter_StartsWithItsSettings_AndTicks()
    {
        var h = new Harness();
        h.Store.Saved["Davian"] = ChatDeckSettingsJson.Serialize(
            ChatDeckDefaults.Create() with { Layout = new ChatDeckLayoutState { LastTabId = "combat" } }, null);
        h.Key = "Davian";

        h.Session.Tick();
        ChatDeckModel model = Assert.IsType<ChatDeckModel>(h.Session.Model);
        Assert.Equal("Davian", model.CharacterName);
        Assert.Equal("combat", model.SelectedTabId);
        Assert.Equal(1, h.Changes);

        h.Say("hello");
        h.Session.Tick();
        Assert.Equal(1, model.FindTab("all")!.Count);
    }

    [Fact]
    public void SettingsChanged_Saves()
    {
        var h = new Harness { Key = "Davian" };
        h.Session.Tick();
        h.Session.Model!.SelectTab("group");
        Assert.Contains("\"group\"", h.Store.Saved["Davian"]);
    }

    [Fact]
    public void Save_WritesCurrentSettings()
    {
        var h = new Harness { Key = "Davian" };
        h.Session.Tick();
        h.Session.Save();
        Assert.True(h.Store.Saved.ContainsKey("Davian"));
        Assert.False(h.Store.Saved.ContainsKey("default"));
    }

    [Fact]
    public void EndSession_DropsTheModel_SameCharacterStartsEmpty()
    {
        var h = new Harness { Key = "Davian" };
        h.Session.Tick();
        ChatDeckModel first = h.Session.Model!;
        h.Say("before");
        h.Session.Tick();

        h.Session.EndSession();
        Assert.Null(h.Session.Model);
        Assert.Equal(2, h.Changes);
        h.Say("while gone");
        first.Tick();
        Assert.Equal(1, first.FindTab("all")!.Count);   // disposed: no longer listening

        h.Session.Tick();
        Assert.NotSame(first, h.Session.Model);
        Assert.Equal(0, h.Session.Model!.FindTab("all")!.Count);
    }

    [Fact]
    public void AnotherCharacter_ReplacesTheModel()
    {
        var h = new Harness { Key = "Davian" };
        h.Session.Tick();
        ChatDeckModel first = h.Session.Model!;
        h.Key = "Bob";
        h.Session.Tick();
        Assert.Equal("Bob", h.Session.Model!.CharacterName);
        Assert.NotSame(first, h.Session.Model);
        Assert.Equal(["Davian", "Bob"], h.Store.Loads);
    }

    [Fact]
    public void FailedStart_LogsOnce_AndDoesNotRetryEveryFrame()
    {
        var h = new Harness { Key = "Davian" };
        h.Store.LoadThrow = new IOException("disk gone");

        h.Session.Tick();
        h.Session.Tick();
        h.Session.Tick();

        Assert.Null(h.Session.Model);
        Assert.Equal(["Davian"], h.Store.Loads);
        Assert.Contains("disk gone", Assert.Single(h.Logged));
        Assert.Equal(0, h.Changes);
    }

    [Fact]
    public void FailedStart_TriesAgainAfterTheSessionEnds()
    {
        var h = new Harness { Key = "Davian" };
        h.Store.LoadThrow = new IOException("disk gone");
        h.Session.Tick();

        h.Store.LoadThrow = null;
        h.Session.EndSession();
        h.Session.Tick();

        Assert.NotNull(h.Session.Model);
        Assert.Equal(["Davian", "Davian"], h.Store.Loads);
    }
}
