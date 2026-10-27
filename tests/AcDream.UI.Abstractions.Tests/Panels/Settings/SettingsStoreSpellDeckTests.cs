using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class SettingsStoreSpellDeckTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");

    [Fact]
    public void SpellDeck_RoundTrips_PerCharacter_AndKeepsOtherSections()
    {
        string path = TempPath();
        try
        {
            var store = new SettingsStore(path);
            store.SaveMisc(MiscSettings.Default with { ModernHud = true, SpellDeckClickCasts = true });
            Assert.Null(store.LoadSpellDeck("toon"));
            store.SaveSpellDeck("toon", new StoredSpellDeck(9, 4, false, null, [[10u, 11u], [12u]]));

            StoredSpellDeck? back = new SettingsStore(path).LoadSpellDeck("toon");
            Assert.NotNull(back);
            Assert.False(back!.Attached);
            Assert.Equal((9, 4), (back.Tab, back.RowCount));
            Assert.Equal(2, back.LocalTabs.Count);
            Assert.Equal([10u, 11u], back.LocalTabs[0]);
            Assert.Equal([12u], back.LocalTabs[1]);
            Assert.Null(store.LoadSpellDeck("other"));
            MiscSettings misc = store.LoadMisc();
            Assert.True(misc.ModernHud);
            Assert.True(misc.SpellDeckClickCasts);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SpellDeck_DetachedRows_RoundTrips_AndIsNullWhenMissing()
    {
        string path = TempPath();
        try
        {
            var store = new SettingsStore(path);
            store.SaveSpellDeck("toon", new StoredSpellDeck(0, 2, true, 5, []));
            Assert.Equal(5, new SettingsStore(path).LoadSpellDeck("toon")!.DetachedRows);
            store.SaveSpellDeck("old", new StoredSpellDeck(0, 1, true, null, []));
            Assert.Null(new SettingsStore(path).LoadSpellDeck("old")!.DetachedRows);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SpellDeck_V1RowsArray_MigratesToRowCountAndFirstTab()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, """
                {"version":1,"spellDeck":{"toon":{"attached":false,"detachedRows":4,
                 "rows":[{"id":3,"tab":5},{"id":4,"tab":1},{"id":9,"tab":2}]}}}
                """);
            StoredSpellDeck d = new SettingsStore(path).LoadSpellDeck("toon")!;
            Assert.Equal((5, 3, false, (int?)4), (d.Tab, d.RowCount, d.Attached, d.DetachedRows));
            Assert.Empty(d.LocalTabs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SpellDeck_MalformedLocalTab_IsSkipped_NotTheWholeDeck()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(path, """
                {"spellDeck":{"toon":{"tab":8,"rowCount":2,"attached":true,
                 "localTabs":[[1,2],"junk",[3,"x"],[4]]}}}
                """);
            StoredSpellDeck d = new SettingsStore(path).LoadSpellDeck("toon")!;
            Assert.Equal(2, d.RowCount);
            Assert.Equal(2, d.LocalTabs.Count);
            Assert.Equal([1u, 2u], d.LocalTabs[0]);
            Assert.Equal([4u], d.LocalTabs[1]);
        }
        finally { File.Delete(path); }
    }
}
