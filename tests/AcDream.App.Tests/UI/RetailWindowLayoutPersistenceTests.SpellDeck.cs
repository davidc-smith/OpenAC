using AcDream.App.Spells;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    private sealed class NoSpellActions : ISpellDeckActions
    {
        public void CastSpell(uint spellId) { }
        public void CastItemSpell(uint itemId) { }
        public void SetFavorite(int tab, int position, uint spellId) { }
        public void RemoveFavorite(int tab, uint spellId) { }
        public void ExamineSpell(uint spellId) { }
        public void ShowMessage(string text) { }
    }

    /// <summary>The Command Deck, the Spell Deck, the layout and the character sync,
    /// wired the way <c>RetailUiRuntime</c> wires them.</summary>
    private sealed class SpellDeckRig : IDisposable
    {
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public readonly SettingsStore Store;
        public readonly UiSpellDeck Spell;
        public readonly SpellDeckController Controller;
        public readonly RetailWindowLayoutPersistence Persistence;
        public readonly SpellDeckCharacterSync Sync;
        public readonly SpellDeckDock Dock;
        public string Key = "default";
        public (int Width, int Height) Screen = (1600, 900);

        public SpellDeckRig(string path)
        {
            Store = new SettingsStore(path);
            UiCommandDeck deck = MountDeck(Root);
            SpellDeckCharacterSync? sync = null;
            SpellDeckDock? dock = null;
            Controller = new SpellDeckController(
                new NoSpellActions(), _ => [], () => (ItemSpell?)null, () => 12,
                SpellDeckSettings.Default, s => sync?.Save(s), () => false, () => dock?.CanAddRow() ?? true);
            Spell = new UiSpellDeck(Controller, _ => 0u, _ => null, _ => "", () => null, () => 0.0,
                () => false, () => { }, _ => { });
            RetailWindowHandle spellHandle = RetailWindowFrame.Mount(
                Root, Spell, _ => (0u, 0, 0), UiSpellDeck.MountOptions(DeckLeft, 40f) with { Visible = true });
            Assert.True(Root.WindowManager.TryGet(WindowNames.CommandDeck, out RetailWindowHandle deckHandle));
            Persistence = new RetailWindowLayoutPersistence(
                Root.WindowManager, Store, () => Key, () => Screen,
                [WindowNames.CommandDeck, WindowNames.SpellDeck]);
            Persistence.SetCaptureGate(WindowNames.SpellDeck, () => !Controller.Attached);
            Persistence.ResetToDefaults();
            dock = new SpellDeckDock(
                new SpellDeckWindowDockHost(Root, () => Persistence, deck, deckHandle, Spell, spellHandle), Controller);
            sync = new SpellDeckCharacterSync(Controller, dock, Store, () => Key, () => Persistence.RestoreCount);
            Sync = sync;
            Dock = dock;
        }

        /// <summary>The runtime's frame.</summary>
        public void Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Sync.Tick();
                Spell.Refresh();
            }
        }

        /// <summary>A character enters the world: its key appears, frames run, then the
        /// login restores the layout (<c>RetailUiRuntime.RestoreLayout</c>'s order).</summary>
        public void EnterWorld(string key)
        {
            Key = key;
            Frames(3);
            Sync.LoadIfChanged();
            Persistence.RestoreAll();
            Sync.AfterRestore();
            Frames(3);
        }

        /// <summary>A resolution change in <c>RetailUiRuntime.Draw</c>: reflow, then the Spell Deck.</summary>
        public void Reflow(int width, int height)
        {
            Screen = (width, height);
            Root.Width = width;
            Root.Height = height;
            Persistence.ReflowToScreen();
            Sync.AfterReflow();
            Frames(2);
        }

        public void Dispose() => Persistence.Dispose();
    }

    private static UiWindowLayout StoreDetachedSpellDeck(
        SettingsStore store, string key, float left, float top, float scale, int rows)
    {
        store.SaveSpellDeck(key, new StoredSpellDeck(0, rows, false, rows, []));
        SpellDeckGeometry g = SpellDeckGeometry.Create(scale, 1f, rows);
        var layout = new UiWindowLayout(left, top, g.Width, g.Height, false, false, false);
        store.SaveWindowLayout(key, "1600x900", WindowNames.SpellDeck, layout);
        return layout;
    }

    private static void AssertSpellDeckAt(SpellDeckRig rig, UiWindowLayout expected, string key, float scale, int rows)
    {
        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            rig.Store.LoadWindowLayout(key, "1600x900", WindowNames.SpellDeck, default));
        Assert.Equal(expected.X, saved.X, 2);
        Assert.Equal(expected.Y, saved.Y, 2);
        Assert.Equal(expected.Width, saved.Width, 2);
        Assert.Equal(expected.Height, saved.Height, 2);

        Assert.Equal(expected.X, rig.Spell.Left, 2);
        Assert.Equal(expected.Y, rig.Spell.Top, 2);
        Assert.Equal(rows, rig.Spell.Geometry.Rows);
        Assert.Equal(scale, rig.Spell.Geometry.Scale, 3);
        Assert.Equal(rig.Spell.Width, rig.Spell.Geometry.Width, 2);
        Assert.Equal(rig.Spell.Height, rig.Spell.Geometry.Height, 2);
    }

    [Fact]
    public void SpellDeck_DetachedPlacementSurvivesLogin()
    {
        var store = new SettingsStore(PathName);
        UiWindowLayout bob = StoreDetachedSpellDeck(store, "Bob", 300f, 100f, 1.2f, 5);
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);

        rig.EnterWorld("Bob");

        Assert.False(rig.Controller.Attached);
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);
    }

    [Fact]
    public void SpellDeck_DetachedToDetachedSwitchKeepsBothPlacements()
    {
        var store = new SettingsStore(PathName);
        UiWindowLayout bob = StoreDetachedSpellDeck(store, "Bob", 300f, 100f, 1.2f, 5);
        UiWindowLayout carol = StoreDetachedSpellDeck(store, "Carol", 520f, 260f, 0.9f, 2);
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);

        rig.EnterWorld("Bob");
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);

        rig.EnterWorld("Carol");
        Assert.False(rig.Controller.Attached);
        AssertSpellDeckAt(rig, carol, "Carol", 0.9f, 2);

        rig.EnterWorld("Bob");
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);
    }

    [Fact]
    public void SpellDeck_RowsAddedWhileAttached_DetachAfterRelogKeepsTheStoredScale()
    {
        // Bob's placement was saved detached with 3 rows at 1.2; since then he
        // attached, added 2 rows and logged out.
        var store = new SettingsStore(PathName);
        StoreDetachedSpellDeck(store, "Bob", 300f, 100f, 1.2f, 3);
        store.SaveSpellDeck("Bob", new StoredSpellDeck(0, 5, true, 3, []));
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        Assert.True(rig.Controller.Attached);

        rig.Dock.Detach();
        rig.Frames(2);

        SpellDeckGeometry g = SpellDeckGeometry.Create(1.2f, 1f, 5);
        AssertSpellDeckAt(rig, new UiWindowLayout(300f, 100f, g.Width, g.Height, false, false, false), "Bob", 1.2f, 5);
        Assert.Equal(5, SpellDeckSettings.Load(rig.Store, "Bob").DetachedRows);
    }

    [Fact]
    public void SpellDeck_ReflowToASmallerScreenAndBack_LeavesTheDetachedPlacement()
    {
        var store = new SettingsStore(PathName);
        UiWindowLayout bob = StoreDetachedSpellDeck(store, "Bob", 1000f, 690f, 1.2f, 5);   // 204 tall: on screen
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);

        rig.Reflow(1000, 170);   // too short for the deck: the window shrinks
        rig.Reflow(1600, 900);

        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);
    }

    [Fact]
    public void SpellDeck_SameCharacterRelogThroughDefault_WaitsForTheRestore()
    {
        var store = new SettingsStore(PathName);
        UiWindowLayout bob = StoreDetachedSpellDeck(store, "Bob", 300f, 100f, 1.2f, 5);
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);

        // Log out (the key falls back to "default", whose deck is attached), then Bob again.
        rig.Key = "default";
        rig.Frames(3);
        Assert.True(rig.Controller.Attached);
        rig.Key = "Bob";
        rig.Frames(3);
        Assert.True(rig.Sync.AwaitingRestore);   // Bob's layout has not been restored this login

        rig.EnterWorld("Bob");
        Assert.False(rig.Sync.AwaitingRestore);
        Assert.False(rig.Controller.Attached);
        AssertSpellDeckAt(rig, bob, "Bob", 1.2f, 5);
    }

    [Fact]
    public void SpellDeck_FirstDetach_GoesAboveTheDeckWhereItIsNow_AndIsSaved()
    {
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        Assert.True(rig.Controller.Attached);
        Assert.True(rig.Root.WindowManager.TryGet(WindowNames.CommandDeck, out RetailWindowHandle deck));
        deck.MoveTo(700f, 600f);
        rig.Frames(2);

        rig.Dock.Detach();
        rig.Frames(2);

        // Where it sat attached: from the orb's left edge (32), on the frame's top (4).
        SpellDeckGeometry g = SpellDeckGeometry.Create(1f, 1f, 3);
        AssertSpellDeckAt(rig, new UiWindowLayout(732f, 604f - g.Height, g.Width, g.Height, false, false, false), "Bob", 1f, 3);
    }

    [Fact]
    public void SpellDeck_DraggingTheAttachedSpellDeck_MovesAndSavesTheDeck_NotTheSpellDeck()
    {
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        Assert.True(rig.Controller.Attached);
        Assert.True(rig.Root.WindowManager.TryGet(WindowNames.CommandDeck, out RetailWindowHandle deck));
        deck.MoveTo(700f, 600f);
        rig.Frames(2);
        UiWindowLayout? spellStored = rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.SpellDeck, default);
        SpellDeckGeometry g = rig.Spell.Geometry;
        Assert.Equal((732f, 604f - g.Height), (rig.Spell.Left, rig.Spell.Top));

        // A press on the frame, left of the item socket.
        int x = (int)(rig.Spell.Left + 3f), y = (int)(rig.Spell.Top + g.Height / 2f);
        Assert.Equal(SpellDeckZone.Frame, g.HitAt(x - rig.Spell.Left, y - rig.Spell.Top, 1).Zone);
        rig.Root.OnMouseDown(UiMouseButton.Left, x, y);
        rig.Root.OnMouseMove(x + 40, y - 30);
        Assert.True(rig.Root.IsWindowMoveActive);   // the move cursor
        rig.Frames(1);
        rig.Root.OnMouseMove(x + 50, y - 20);
        rig.Frames(1);
        rig.Root.OnMouseUp(UiMouseButton.Left, x + 50, y - 20);
        rig.Frames(1);

        Assert.Equal((750f, 580f), (deck.Left, deck.Top));
        Assert.Equal((782f, 584f - g.Height), (rig.Spell.Left, rig.Spell.Top));
        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.CommandDeck, default));
        Assert.Equal((750f, 580f), (saved.X, saved.Y));
        Assert.Equal(spellStored, rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.SpellDeck, default));
    }

    [Fact]
    public void SpellDeck_SettingsAreNotSavedUnderTheDefaultKey()
    {
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        Assert.Equal("default", rig.Sync.LoadedCharacter);

        rig.Controller.AddRow();
        rig.Controller.AddLocalTab();
        rig.Controller.AddFavoriteFromSpellbook(4242u);
        rig.Dock.Detach();
        rig.Frames(2);

        Assert.Null(rig.Store.LoadSpellDeck("default"));
    }

    /// <summary>Bob adds a local tab (tab 9), shows it, puts a spell on it and adds a row.</summary>
    private static void GiveBobALocalTab(SpellDeckRig rig, uint spellId)
    {
        rig.Controller.AddLocalTab();
        rig.Controller.AddFavoriteFromSpellbook(spellId);
        rig.Controller.AddRow();
        rig.Frames(2);
    }

    private static void AssertLocalTab(SpellDeckRig rig, int tab, int rows, params uint[] spells)
    {
        Assert.Equal(SpellDeckTabs.ServerTabs + 1, rig.Controller.TabCount);
        Assert.Equal(tab, rig.Controller.Tab);
        Assert.Equal(rows, rig.Controller.RowCount);
        Assert.Equal(spells, rig.Controller.Favorites(SpellDeckTabs.ServerTabs));
    }

    [Fact]
    public void SpellDeck_LocalTabsAndTheShownTabSurviveARelog()
    {
        using (var first = new SpellDeckRig(PathName))
        {
            first.Frames(2);
            first.EnterWorld("Bob");
            GiveBobALocalTab(first, 4242u);
            AssertLocalTab(first, 8, 4, 4242u);
        }

        // A new session: the client restarts and Bob logs in again.
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        Assert.Equal(SpellDeckTabs.ServerTabs, rig.Controller.TabCount);
        rig.EnterWorld("Bob");

        AssertLocalTab(rig, 8, 4, 4242u);
        Assert.Equal(4, rig.Spell.Geometry.Rows);
        Assert.Equal([4242u], rig.Controller.Visible);
        Assert.Null(rig.Store.LoadSpellDeck("default"));
    }

    [Fact]
    public void SpellDeck_LocalTabsArePerCharacter()
    {
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        rig.EnterWorld("Bob");
        GiveBobALocalTab(rig, 4242u);

        // Bob logs out (the key falls back to "default"), then Carol logs in.
        rig.Key = "default";
        rig.Frames(3);
        Assert.Equal(SpellDeckTabs.ServerTabs, rig.Controller.TabCount);
        rig.EnterWorld("Carol");
        Assert.Equal(SpellDeckTabs.ServerTabs, rig.Controller.TabCount);
        Assert.Equal(0, rig.Controller.Tab);
        Assert.Equal(3, rig.Controller.RowCount);
        Assert.Empty(rig.Controller.Favorites(SpellDeckTabs.ServerTabs));

        // Carol's own local tab is hers alone.
        rig.Controller.AddLocalTab();
        rig.Controller.AddFavoriteFromSpellbook(5151u);
        rig.Frames(2);

        rig.EnterWorld("Bob");
        AssertLocalTab(rig, 8, 4, 4242u);
        rig.EnterWorld("Carol");
        Assert.Equal([5151u], rig.Controller.Favorites(SpellDeckTabs.ServerTabs));
        Assert.Equal(3, rig.Controller.RowCount);

        Assert.Equal([4242u], Assert.Single(rig.Store.LoadSpellDeck("Bob")!.LocalTabs));
        Assert.Equal([5151u], Assert.Single(rig.Store.LoadSpellDeck("Carol")!.LocalTabs));
        Assert.Null(rig.Store.LoadSpellDeck("default"));
    }
}
