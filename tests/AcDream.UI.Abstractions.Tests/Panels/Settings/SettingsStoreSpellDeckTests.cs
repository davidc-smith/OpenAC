using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class SettingsStoreSpellDeckTests
{
    [Fact]
    public void SpellDeck_RoundTrips_PerCharacter_AndKeepsOtherSections()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.SaveMisc(MiscSettings.Default with { ModernHud = true, SpellDeckClickCasts = true });
            Assert.Null(store.LoadSpellDeck("toon"));
            store.SaveSpellDeck("toon", new StoredSpellDeck(false, [(4, 2), (7, 0)]));

            StoredSpellDeck? back = new SettingsStore(path).LoadSpellDeck("toon");
            Assert.NotNull(back);
            Assert.False(back!.Attached);
            Assert.Equal([(4, 2), (7, 0)], back.Rows);
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
        string path = Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            store.SaveSpellDeck("toon", new StoredSpellDeck(true, [(1, 0), (2, 1)], DetachedRows: 5));
            Assert.Equal(5, new SettingsStore(path).LoadSpellDeck("toon")!.DetachedRows);

            // An older file has no detachedRows.
            store.SaveSpellDeck("old", new StoredSpellDeck(true, [(1, 0)]));
            Assert.Null(new SettingsStore(path).LoadSpellDeck("old")!.DetachedRows);
        }
        finally { File.Delete(path); }
    }
}
