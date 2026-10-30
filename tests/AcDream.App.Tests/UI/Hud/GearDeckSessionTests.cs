using AcDream.App.UI.Hud;
using AcDream.Core.Items;
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
    public void TheLocksFollowTheSettings_ThroughLoadUpdateAndEnd()
    {
        var store = new FakeStore();
        store.Saved["Toon"] = """{"lockedIds":[5]}""";
        var locks = new ItemLocks();
        var session = new GearDeckSession(store, () => "Toon", () => true, locks: locks);
        session.Tick();
        Assert.True(locks.IsLocked(5));
        session.Update(s => s.WithLock([6u], true));
        Assert.True(locks.IsLocked(6));
        session.EndSession();
        Assert.Empty(locks.Ids);
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
    public void EndSession_RaisesEnded_AfterThePendingChangeIsSaved_AndTheDefaultsAreBack()
    {
        var store = new FakeStore();
        var s = new GearDeckSession(store, () => "Davian", () => true, () => 0);
        s.Tick();
        s.Update(x => x with { Folded = true });   // pending: not yet idle long enough to save
        Assert.False(store.Saved.ContainsKey("Davian"));
        string? savedAtEnd = null;
        GearDeckSettings? settingsAtEnd = null;
        s.Ended += () =>
        {
            savedAtEnd = store.Saved.GetValueOrDefault("Davian");
            settingsAtEnd = s.Settings;
        };
        s.EndSession();
        Assert.True(GearDeckSettings.FromJson(savedAtEnd).Folded);
        Assert.Equal(GearDeckSettings.Default, settingsAtEnd);
    }

    [Fact]
    public void SwitchingCharacter_EndsTheOldSession_ThenLoadsTheNewOne()
    {
        var store = new FakeStore();
        store.Saved["Bob"] = """{"version":1,"view":"list"}""";
        string key = "Davian";
        var s = new GearDeckSession(store, () => key, () => true, () => 0);
        int ended = 0;
        s.Ended += () => ended++;
        s.Tick();
        Assert.Equal(0, ended);                    // the first character starts a session, it ends none
        s.Update(x => x with { Folded = true });   // pending: not yet idle long enough to save
        GearDeckSettings? settingsAtEnd = null;
        s.Ended += () => settingsAtEnd = s.Settings;
        key = "Bob";
        s.Tick();
        Assert.Equal(1, ended);
        Assert.Equal(GearDeckSettings.Default, settingsAtEnd);
        Assert.True(GearDeckSettings.FromJson(store.Saved["Davian"]).Folded);
        Assert.Equal(GearPackView.List, s.Settings.View);
        Assert.False(s.Settings.Folded);
        s.Tick();
        Assert.Equal(1, ended);
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

    [Fact]
    public void Equip_DefaultsToFigure_AndRoundTrips()
    {
        Assert.Equal(GearEquipView.Figure, GearDeckSettings.Default.Equip);
        Assert.Equal(GearEquipView.Figure, GearDeckSettings.FromJson("""{"version":1,"folded":false}""").Equip);
        Assert.Equal(GearEquipView.Figure, GearDeckSettings.FromJson("""{"equip":"hologram"}""").Equip);

        GearDeckSettings slots = GearDeckSettings.Default with { Equip = GearEquipView.Slots };
        string json = slots.ToJson();
        Assert.Contains("\"equip\":\"slots\"", json);
        Assert.Equal(GearEquipView.Slots, GearDeckSettings.FromJson(json).Equip);
        Assert.Contains("\"equip\":\"figure\"", GearDeckSettings.Default.ToJson());
    }

    [Fact]
    public void Equip_TakesPartInEquality()
    {
        GearDeckSettings slots = GearDeckSettings.Default with { Equip = GearEquipView.Slots };
        Assert.NotEqual(GearDeckSettings.Default, slots);
        Assert.Equal(slots, GearDeckSettings.FromJson(slots.ToJson()));
    }

    [Fact]
    public void Equip_IsNotKeptAsAnUnknownKey()
    {
        GearDeckSettings s = GearDeckSettings.FromJson("""{"equip":"slots","future":3}""");
        string json = (s with { Equip = GearEquipView.Figure }).ToJson();
        Assert.Contains("\"equip\":\"figure\"", json);
        Assert.Contains("\"future\":3", json);
        // Unknown keys are written first, then ours: a kept "equip" would come before "version".
        Assert.True(json.IndexOf("\"version\"", StringComparison.Ordinal) < json.IndexOf("\"equip\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("equip", "3")]
    [InlineData("equip", "true")]
    [InlineData("view", "{}")]
    [InlineData("folded", "\"yes\"")]
    [InlineData("selectedPack", "\"seven\"")]
    [InlineData("selectedPack", "-1")]
    public void AWronglyTypedValue_ReadsAsItsDefault_AndKeepsTheOtherSettings(string key, string value)
    {
        var o = new System.Text.Json.Nodes.JsonObject
        {
            ["version"] = 1, ["folded"] = true, ["view"] = "list", ["selectedPack"] = 7, ["equip"] = "slots", ["figureLocked"] = true,
            ["savedFilters"] = new System.Text.Json.Nodes.JsonArray(GearQueryJson.Write(new SavedFilter("Steel", GearQuery.Empty with { Text = "a" }))),
        };
        o[key] = System.Text.Json.Nodes.JsonNode.Parse(value);
        GearDeckSettings s = GearDeckSettings.FromJson(o.ToJsonString());
        Assert.Equal(key != "folded", s.Folded);
        Assert.Equal(key == "view" ? GearPackView.Grid : GearPackView.List, s.View);
        Assert.Equal(key == "selectedPack" ? 0u : 7u, s.SelectedPack);
        Assert.Equal(key == "equip" ? GearEquipView.Figure : GearEquipView.Slots, s.Equip);
        Assert.True(s.FigureLocked);
        Assert.Equal(["Steel"], s.SavedFilters.Select(f => f.Name));
    }

    [Fact]
    public void FigureLocked_DefaultsToFalse_AndRoundTrips()
    {
        Assert.False(GearDeckSettings.Default.FigureLocked);
        Assert.Contains("\"figureLocked\":false", GearDeckSettings.Default.ToJson());
        GearDeckSettings locked = GearDeckSettings.Default with { FigureLocked = true };
        string json = locked.ToJson();
        Assert.Contains("\"figureLocked\":true", json);
        Assert.True(GearDeckSettings.FromJson(json).FigureLocked);
    }

    [Theory]
    [InlineData("""{"version":1,"folded":false}""")]
    [InlineData("""{"figureLocked":"yes"}""")]
    [InlineData("""{"figureLocked":1}""")]
    [InlineData("""{"figureLocked":null}""")]
    [InlineData("""{"figureLocked":{}}""")]
    public void FigureLocked_MissingOrNotABool_ReadsFalse(string json)
    {
        GearDeckSettings s = GearDeckSettings.FromJson(json);
        Assert.False(s.FigureLocked);
        string back = s.ToJson();
        Assert.Contains("\"figureLocked\":false", back);
        // Not kept as an unknown key: those are written before "version".
        Assert.True(back.IndexOf("\"version\"", StringComparison.Ordinal) < back.IndexOf("\"figureLocked\"", StringComparison.Ordinal));
    }

    [Fact]
    public void FigureLocked_TakesPartInEquality()
    {
        GearDeckSettings locked = GearDeckSettings.Default with { FigureLocked = true };
        Assert.NotEqual(GearDeckSettings.Default, locked);
        Assert.Equal(locked, GearDeckSettings.FromJson(locked.ToJson()));
    }
}
