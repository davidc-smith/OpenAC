using AcDream.App.UI.Hud;
using AcDream.Core.Spells;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

/// <summary>v3 slot layouts in the runtime's wiring: per character, through relogs, and reconciled
/// with the server's favourites on load and when they change from elsewhere.</summary>
public sealed partial class RetailWindowLayoutPersistenceTests
{
    private const uint SpellA = 1001u, SpellB = 1002u, SpellC = 1003u, SpellD = 1004u, SpellX = 1009u;

    /// <summary>The server's favourites arrive (login's player description): each array is one tab.</summary>
    private static void ServeFavorites(Spellbook book, params uint[][] tabs) =>
        book.ReplaceManifest(new Dictionary<uint, float>(), [], tabs, [], 0x3FFFu);

    private static Spellbook BookWith(params uint[][] tabs)
    {
        var book = new Spellbook();
        ServeFavorites(book, tabs);
        return book;
    }

    private static SpellDeckLayout Slots(params (int Row, int Col, uint Spell)[] slots) =>
        SpellDeckLayout.FromSlots(slots);

    private static void StoreLayout(SettingsStore store, string key, SpellDeckLayout tab0) =>
        (SpellDeckSettings.Default with { Layouts = new Dictionary<int, SpellDeckLayout> { [0] = tab0 } })
            .Save(store, key);

    private static SpellDeckLayout StoredLayout(SettingsStore store, string key) =>
        SpellDeckSettings.Load(store, key).LayoutsAt(12).GetValueOrDefault(0) ?? SpellDeckLayout.Empty;

    /// <summary>Bob's tab 1: A at (0,0) and copied to (0,4), B moved to (1,3); the server keeps A, B.</summary>
    private static readonly SpellDeckLayout BobsGapAndDuplicate = Slots((0, 0, SpellA), (0, 4, SpellA), (1, 3, SpellB));

    /// <summary>Bob arranges his tab with a gap and a duplicate through the deck's own drops.</summary>
    private static void ArrangeBobsTab(SpellDeckRig rig)
    {
        Assert.Equal(Slots((0, 0, SpellA), (0, 1, SpellB)), rig.Controller.LayoutOf(0));
        rig.Controller.DropOnSlot(new SpellDeckDragSource(0, 0, 1, SpellB), 1, 3, copy: false);
        rig.Controller.DropOnSlot(new SpellDeckDragSource(0, 0, 0, SpellA), 0, 4, copy: true);
        rig.Frames(2);
        Assert.Equal(BobsGapAndDuplicate, rig.Controller.LayoutOf(0));
    }

    [Fact]
    public void SpellDeck_AFreshStart_ReconcilesTheSavedLayoutWhenTheCharacterEntersTheWorld()
    {
        // Bob's saved layout lacks C, which was added from elsewhere (another PC) since.
        StoreLayout(new SettingsStore(PathName), "Bob", Slots((0, 0, SpellA), (0, 2, SpellB)));

        using var rig = new SpellDeckRig(PathName, BookWith([SpellA, SpellB, SpellC]));
        rig.Frames(2);
        rig.EnterWorld("Bob");

        // C takes the first empty slot, and the server follows the layout's reading order.
        SpellDeckLayout expected = Slots((0, 0, SpellA), (0, 1, SpellC), (0, 2, SpellB));
        Assert.Equal(expected, rig.Controller.LayoutOf(0));
        Assert.Equal([SpellA, SpellC, SpellB], rig.Book!.GetFavorites(0));
        Assert.All(rig.Sent, s => Assert.Equal("Bob", s.Key));
        Assert.NotEmpty(rig.Sent);
        Assert.Equal(expected, StoredLayout(rig.Store, "Bob"));
        Assert.Null(rig.Store.LoadSpellDeck("default"));
    }

    [Fact]
    public void SpellDeck_ALayoutWithAGapAndADuplicate_SurvivesARelog()
    {
        using (var first = new SpellDeckRig(PathName, BookWith([SpellA, SpellB])))
        {
            first.Frames(2);
            first.EnterWorld("Bob");
            ArrangeBobsTab(first);
            // Extra copies never reach the server; the gap and the move keep A before B.
            Assert.Equal([SpellA, SpellB], first.Book!.GetFavorites(0));
        }

        // A new session: the server still holds A, B.
        using var rig = new SpellDeckRig(PathName, BookWith([SpellA, SpellB]));
        rig.Frames(2);
        rig.EnterWorld("Bob");

        Assert.Equal(BobsGapAndDuplicate, rig.Controller.LayoutOf(0));
        Assert.Equal([SpellA, SpellB], rig.Book!.GetFavorites(0));
        Assert.Empty(rig.Sent);
        Assert.Equal(BobsGapAndDuplicate, StoredLayout(rig.Store, "Bob"));
    }

    [Fact]
    public void SpellDeck_AnotherCharactersLayout_IsNotAffected()
    {
        var book = BookWith([SpellA, SpellB]);
        using var rig = new SpellDeckRig(PathName, book);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        ArrangeBobsTab(rig);

        // Bob logs out (spellbook cleared, key back to default), Carol logs in with her own favourites.
        book.Clear();
        rig.Key = "default";
        rig.Frames(3);
        rig.EnterWorld("Carol");
        ServeFavorites(book, [SpellC, SpellD]);
        rig.Frames(2);

        Assert.Equal(Slots((0, 0, SpellC), (0, 1, SpellD)), rig.Controller.LayoutOf(0));
        Assert.Equal(BobsGapAndDuplicate, StoredLayout(rig.Store, "Bob"));
        Assert.DoesNotContain(rig.Sent, s => s.Key == "Bob" || s.Key == "default");

        // And back: Bob's layout is his, unchanged, and nothing is sent for it.
        book.Clear();
        rig.Key = "default";
        rig.Frames(3);
        ServeFavorites(book, [SpellA, SpellB]);
        int before = rig.Sent.Count;
        rig.EnterWorld("Bob");
        Assert.Equal(BobsGapAndDuplicate, rig.Controller.LayoutOf(0));
        Assert.Equal(before, rig.Sent.Count);
        Assert.Equal(Slots((0, 0, SpellC), (0, 1, SpellD)), StoredLayout(rig.Store, "Carol"));
    }

    [Fact]
    public void SpellDeck_AServerSideAddFromElsewhere_LandsInTheFirstEmptySlot()
    {
        var book = BookWith([SpellA, SpellB]);
        using var rig = new SpellDeckRig(PathName, book);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        ArrangeBobsTab(rig);
        rig.Sent.Clear();

        // The retail client (or another PC) adds X at the end of the server's list.
        book.SetFavorite(0, 2, SpellX);

        SpellDeckLayout expected = Slots((0, 0, SpellA), (0, 1, SpellX), (0, 4, SpellA), (1, 3, SpellB));
        Assert.Equal(expected, rig.Controller.LayoutOf(0));
        // The server follows the reading order (X now before B), sent for Bob.
        Assert.Equal([SpellA, SpellX, SpellB], book.GetFavorites(0));
        Assert.NotEmpty(rig.Sent);
        Assert.All(rig.Sent, s => Assert.Equal("Bob", s.Key));
        Assert.Equal(expected, StoredLayout(rig.Store, "Bob"));

        // The echo and a repeat change find nothing more to do.
        rig.Sent.Clear();
        rig.Sync.ServerFavoritesChanged();
        rig.Frames(2);
        Assert.Empty(rig.Sent);

        // A removal from elsewhere clears every copy.
        book.RemoveFavorite(0, SpellA);
        Assert.Equal(Slots((0, 1, SpellX), (1, 3, SpellB)), rig.Controller.LayoutOf(0));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void SpellDeck_ACharacterSwitch_WithTheNewSpellbookAlreadyIn_LeavesTheOldCharacterAlone()
    {
        // Carol's saved layout orders her spells differently from her server list.
        StoreLayout(new SettingsStore(PathName), "Carol", Slots((0, 0, SpellD), (0, 1, SpellC)));

        var book = BookWith([SpellA, SpellB]);
        using var rig = new SpellDeckRig(PathName, book);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        ArrangeBobsTab(rig);
        rig.Sent.Clear();

        // Carol's key and favourites arrive before the sync has had a frame to load her.
        rig.Key = "Carol";
        ServeFavorites(book, [SpellC, SpellD]);
        book.SetFavorite(0, 2, SpellX);

        Assert.Equal("Bob", rig.Sync.LoadedCharacter);
        Assert.Empty(rig.Sent);
        Assert.Equal(BobsGapAndDuplicate, rig.Controller.LayoutOf(0));
        Assert.Equal(BobsGapAndDuplicate, StoredLayout(rig.Store, "Bob"));
        Assert.Equal([SpellC, SpellD, SpellX], book.GetFavorites(0));

        // Carol loads: her layout reconciles against her favourites, and only then is anything sent.
        rig.EnterWorld("Carol");
        SpellDeckLayout carols = Slots((0, 0, SpellD), (0, 1, SpellC), (0, 2, SpellX));
        Assert.Equal(carols, rig.Controller.LayoutOf(0));
        Assert.Equal([SpellD, SpellC, SpellX], book.GetFavorites(0));
        Assert.NotEmpty(rig.Sent);
        Assert.All(rig.Sent, s => Assert.Equal("Carol", s.Key));
        Assert.Equal(carols, StoredLayout(rig.Store, "Carol"));
        Assert.Equal(BobsGapAndDuplicate, StoredLayout(rig.Store, "Bob"));
    }

    [Fact]
    public void SpellDeck_WhileTheSpellDeckIsNotActive_TheServerIsLeftAlone_UntilItIs()
    {
        StoreLayout(new SettingsStore(PathName), "Bob", Slots((0, 0, SpellB), (0, 1, SpellA)));

        var book = BookWith([SpellA, SpellB]);
        using var rig = new SpellDeckRig(PathName, book) { Active = false };
        rig.Frames(2);
        rig.EnterWorld("Bob");
        // Modern HUD off: retail's order stands, on load and when it changes.
        book.SetFavorite(0, 2, SpellX);
        rig.Frames(2);
        Assert.Empty(rig.Sent);
        Assert.Equal([SpellA, SpellB, SpellX], book.GetFavorites(0));
        Assert.Equal(Slots((0, 0, SpellB), (0, 1, SpellA)), StoredLayout(rig.Store, "Bob"));
        // A server-tab edit is refused as well: it would send the deck's order to retail's bar.
        Assert.False(rig.Sync.CanSendToServer);
        rig.Controller.DropOnSlot(new SpellDeckDragSource(0, 0, 0, SpellB), 1, 3, copy: false);
        Assert.Empty(rig.Sent);
        Assert.Equal(Slots((0, 0, SpellB), (0, 1, SpellA)), rig.Controller.LayoutOf(0));

        // Modern HUD on: the layout reconciles on the next frame.
        rig.Active = true;
        rig.Frames(1);
        Assert.True(rig.Sync.CanSendToServer);
        Assert.Equal(Slots((0, 0, SpellB), (0, 1, SpellA), (0, 2, SpellX)), rig.Controller.LayoutOf(0));
        Assert.Equal([SpellB, SpellA, SpellX], book.GetFavorites(0));
        Assert.All(rig.Sent, s => Assert.Equal("Bob", s.Key));
    }
}
