using AcDream.App.UI.Hud;
using AcDream.Core.Items.Gear;

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

    [Fact]
    public void TickSave_WritesAPendingChangeAfterTheIdle_WithoutReadingTheKey()
    {
        var store = new FakeStore();
        double now = 0;
        int keyReads = 0;
        var s = new GearDeckSession(store, () => { keyReads++; return "Davian"; }, () => true, () => now);
        s.Tick();
        s.Update(x => x with { Folded = true });   // then the deck closes: only the runtime ticks now
        int reads = keyReads;
        now = 0.3; s.TickSave();
        Assert.False(store.Saved.ContainsKey("Davian"));
        now = 0.6; s.TickSave();
        Assert.Contains("\"folded\":true", store.Saved["Davian"]);
        Assert.Equal(reads, keyReads);
    }

    [Fact]
    public void TickSave_NeverLoadsOrSavesBeforeARealCharacter()
    {
        var store = new FakeStore();
        store.Saved["Davian"] = """{"version":1,"folded":true}""";
        double now = 0;
        var s = new GearDeckSession(store, () => "Davian", () => false, () => now);
        s.Update(x => x with { View = GearPackView.List });
        now = 5; s.TickSave();
        Assert.False(s.Settings.Folded);
        Assert.Equal("""{"version":1,"folded":true}""", store.Saved["Davian"]);
    }

    [Fact]
    public void SavedFilters_RoundTrip_KeepUnreadable()
    {
        const string json = "{\"version\":1,\"folded\":false,\"view\":\"grid\",\"selectedPack\":0," +
            "\"savedFilters\":[{\"name\":\"future\",\"chips\":[{\"f\":\"armourLevel\",\"min\":300}]}," +
            "{\"name\":\"Steel\",\"chips\":[{\"f\":\"material\",\"id\":64}]}]}";
        GearDeckSettings s = GearDeckSettings.FromJson(json);
        Assert.Equal(["Steel"], s.SavedFilters.Select(f => f.Name));
        string back = s.ToJson();
        Assert.Contains("armourLevel", back);
        Assert.True(back.IndexOf("\"Steel\"", StringComparison.Ordinal) < back.IndexOf("future", StringComparison.Ordinal));
        Assert.Equal(s, GearDeckSettings.FromJson(back));
    }

    [Fact]
    public void WithSaved_ReplacesByName_CapsCount()
    {
        var q = GearQuery.Empty with { Text = "a" };
        GearDeckSettings s = GearDeckSettings.Default.WithSaved(new SavedFilter("Steel", q));
        s = s.WithSaved(new SavedFilter("  steel ", q with { Text = "b" }));
        Assert.Single(s.SavedFilters);
        Assert.Equal("b", s.SavedFilters[0].Query.Text);
        Assert.Equal("steel", s.SavedFilters[0].Name);
        for (int i = 0; i < 30; i++) s = s.WithSaved(new SavedFilter($"f{i}", q));
        Assert.Equal(SavedFilter.MaxCount, s.SavedFilters.Count);
        Assert.Equal(SavedFilter.MaxCount - 1, s.WithoutSaved("STEEL").SavedFilters.Count);
        Assert.Equal(SavedFilter.MaxNameLength, GearDeckSettings.Default.WithSaved(new SavedFilter(new string('x', 40), q)).SavedFilters[0].Name.Length);
    }

    [Fact]
    public void SavedFilters_InEquality()
    {
        var q = GearQuery.Empty with { Text = "a" };
        Assert.NotEqual(GearDeckSettings.Default, GearDeckSettings.Default.WithSaved(new SavedFilter("a", q)));
        Assert.Equal(GearDeckSettings.Default.WithSaved(new SavedFilter("a", q)), GearDeckSettings.Default.WithSaved(new SavedFilter("a", q)));
    }

    [Fact]
    public void EndSession_RaisesEnded()
    {
        var session = new GearDeckSession(null, () => "", () => false);
        int ended = 0;
        session.Ended += () => ended++;
        session.EndSession();
        Assert.Equal(1, ended);
    }
}
