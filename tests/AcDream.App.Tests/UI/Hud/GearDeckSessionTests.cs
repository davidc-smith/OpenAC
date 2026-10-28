using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearDeckSessionTests
{
    private sealed class FakeStore : IGearDeckStore
    {
        public readonly Dictionary<string, string> Saved = [];
        public string? Load(string key) => Saved.GetValueOrDefault(key);
        public void Save(string key, string json) => Saved[key] = json;
    }

    [Fact]
    public void LoadsForRealCharacterOnly()
    {
        var store = new FakeStore();
        store.Saved["Davian"] = """{"version":1,"folded":true,"view":"list","selectedPack":7}""";
        string key = "default";
        var s = new GearDeckSession(store, () => key, () => true, () => 0);
        s.Tick();
        Assert.False(s.Settings.Folded);
        key = "Davian";
        s.Tick();
        Assert.True(s.Settings.Folded);
        Assert.Equal(GearPackView.List, s.Settings.View);
        Assert.Equal(7u, s.Settings.SelectedPack);
    }

    [Fact]
    public void Update_SavesAfterHalfSecondIdle()
    {
        var store = new FakeStore();
        double now = 0;
        var s = new GearDeckSession(store, () => "Davian", () => true, () => now);
        s.Tick();
        s.Update(x => x with { Folded = true });
        now = 0.3; s.Tick();
        Assert.False(store.Saved.ContainsKey("Davian"));
        now = 0.6; s.Tick();
        Assert.Contains("\"folded\":true", store.Saved["Davian"]);
    }

    [Fact]
    public void BadJson_Defaults_UnknownKeysKept()
    {
        Assert.Equal(GearDeckSettings.Default, GearDeckSettings.FromJson("not json"));
        GearDeckSettings s = GearDeckSettings.FromJson("""{"version":1,"folded":true,"future":{"x":1}}""");
        Assert.Contains("\"future\"", s.ToJson());
    }

    [Fact]
    public void EndSession_FlushesAndResets()
    {
        var store = new FakeStore();
        var s = new GearDeckSession(store, () => "Davian", () => true, () => 0);
        s.Tick();
        s.Update(x => x with { View = GearPackView.List });
        s.EndSession();
        Assert.Contains("\"view\":\"list\"", store.Saved["Davian"]);
        Assert.Equal(GearDeckSettings.Default, s.Settings);
    }
}
