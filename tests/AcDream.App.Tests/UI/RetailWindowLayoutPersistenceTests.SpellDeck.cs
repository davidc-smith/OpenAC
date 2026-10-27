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
        public readonly UiCommandDeck Deck;
        /// <summary>How many times the root reported each window resized (each report saves its placement).</summary>
        public readonly Dictionary<string, int> Resized = [];
        public string Key = "default";
        public (int Width, int Height) Screen = (1600, 900);

        public SpellDeckRig(string path)
        {
            Store = new SettingsStore(path);
            UiCommandDeck deck = MountDeck(Root);
            Deck = deck;
            Root.WindowResized += (name, _) => Resized[name] = Resized.GetValueOrDefault(name) + 1;
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
    public void SpellDeck_TabSwitchJustBeforeACharacterChange_IsSavedForTheCharacterLeft()
    {
        var store = new SettingsStore(PathName);
        store.SaveSpellDeck("Bob", new StoredSpellDeck(0, 3, false, null, []));
        store.SaveSpellDeck("Carol", new StoredSpellDeck(4, 2, false, null, []));
        using var rig = new SpellDeckRig(PathName);
        rig.Frames(2);
        rig.EnterWorld("Bob");

        rig.Controller.ShowTab(2);               // pending: the 0.5 s delay has not passed
        Assert.Equal(0, store.LoadSpellDeck("Bob")!.Tab);
        rig.Key = "Carol";
        rig.Sync.LoadIfChanged();

        Assert.Equal(2, new SettingsStore(PathName).LoadSpellDeck("Bob")!.Tab);
        StoredSpellDeck carol = new SettingsStore(PathName).LoadSpellDeck("Carol")!;
        Assert.Equal((4, 2), (carol.Tab, carol.RowCount));
        Assert.Equal(4, rig.Controller.Tab);
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

        // Where it sat attached: from the frame's left past the orb (70), on the frame's top (4).
        SpellDeckGeometry g = SpellDeckGeometry.Create(1f, 1f, 3);
        AssertSpellDeckAt(rig, new UiWindowLayout(770f, 604f - g.Height, g.Width, g.Height, false, false, false), "Bob", 1f, 3);
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
        Assert.Equal((770f, 604f - g.Height), (rig.Spell.Left, rig.Spell.Top));

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
        Assert.Equal((820f, 584f - g.Height), (rig.Spell.Left, rig.Spell.Top));
        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.CommandDeck, default));
        Assert.Equal((750f, 580f), (saved.X, saved.Y));
        Assert.Equal(spellStored, rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.SpellDeck, default));
    }

    /// <summary>Bob in the world with the attached block's deck at (700, <paramref name="deckTop"/>).</summary>
    private static (RetailWindowHandle Deck, UiWindowLayout? SpellStored) AttachedBlock(SpellDeckRig rig, float deckTop = 600f)
    {
        rig.Frames(2);
        rig.EnterWorld("Bob");
        Assert.True(rig.Controller.Attached);
        Assert.True(rig.Root.WindowManager.TryGet(WindowNames.CommandDeck, out RetailWindowHandle deck));
        deck.MoveTo(700f, deckTop);
        rig.Frames(2);
        rig.Resized.Clear();
        return (deck, rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.SpellDeck, default));
    }

    /// <summary>A press-drag-release of the left button through the root, with frames between the moves.</summary>
    private static void DragThroughRoot(SpellDeckRig rig, int x, int y, params (int Dx, int Dy)[] moves)
    {
        rig.Root.OnMouseMove(x, y);
        rig.Root.OnMouseDown(UiMouseButton.Left, x, y);
        foreach (var (dx, dy) in moves)
        {
            rig.Root.OnMouseMove(x + dx, y + dy);
            rig.Frames(1);
        }
        var (lx, ly) = moves[^1];
        rig.Root.OnMouseUp(UiMouseButton.Left, x + lx, y + ly);
        rig.Frames(2);
    }

    [Fact]
    public void SpellDeck_TheAttachedRightEdge_StretchesBothDecks_AndSavesTheDeckOnce()
    {
        using var rig = new SpellDeckRig(PathName);
        var (deck, spellStored) = AttachedBlock(rig);
        int x = (int)(rig.Spell.Left + rig.Spell.Width - 1f), y = (int)(rig.Spell.Top + rig.Spell.Height / 2f);

        rig.Root.OnMouseMove(x, y);
        rig.Root.OnMouseDown(UiMouseButton.Left, x, y);
        Assert.Equal(ResizeEdges.Right, rig.Root.ActiveResizeEdges);
        rig.Root.OnMouseMove(x + 40, y);
        rig.Frames(1);
        rig.Root.OnMouseMove(x + 84, y);
        rig.Frames(1);
        Assert.Empty(rig.Resized);   // nothing saved while the resize runs
        rig.Root.OnMouseUp(UiMouseButton.Left, x + 84, y);
        rig.Frames(2);

        // 84 more: stretch 1.2 for both; the deck's left and top stay; the Spell Deck sits on it.
        Assert.Equal((1f, 1.2f), (rig.Deck.Geometry.Scale, rig.Deck.Geometry.Stretch));
        Assert.Equal((700f, 600f, 150f), (deck.Left, deck.Top, deck.Height));
        Assert.Equal(504f, deck.Width, 2);
        SpellDeckGeometry g = rig.Spell.Geometry;
        Assert.Equal((1f, 1.2f, 3), (g.Scale, g.Stretch, g.Rows));
        Assert.Equal((770f, 604f - g.Height), (rig.Spell.Left, rig.Spell.Top));
        Assert.Equal((g.Width, g.Height), (rig.Spell.Width, rig.Spell.Height));
        Assert.Equal(1204f, rig.Spell.Left + rig.Spell.Width, 2);   // the deck's right edge

        Assert.Equal(1, rig.Resized[WindowNames.CommandDeck]);
        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.CommandDeck, default));
        Assert.Equal((700f, 600f, 150f), (saved.X, saved.Y, saved.Height));
        Assert.Equal(504f, saved.Width, 2);
        Assert.Equal(spellStored, rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.SpellDeck, default));

        // A second resize starts from the new size.
        x = (int)(rig.Spell.Left + rig.Spell.Width - 1f);
        DragThroughRoot(rig, x, y, (-42, 0));
        Assert.Equal(1.1f, rig.Deck.Geometry.Stretch, 3);
        Assert.Equal(1.1f, rig.Spell.Geometry.Stretch, 3);
        Assert.Equal(2, rig.Resized[WindowNames.CommandDeck]);
    }

    [Fact]
    public void SpellDeck_TheAttachedTopRightCorner_ScalesBothDecks_KeepingTheBlocksBottom()
    {
        using var rig = new SpellDeckRig(PathName);
        var (deck, spellStored) = AttachedBlock(rig);
        int x = (int)(rig.Spell.Left + rig.Spell.Width - 1f), y = (int)rig.Spell.Top + 1;

        // 70 more on the Spell Deck's 350: scale 1.2 for both.
        DragThroughRoot(rig, x, y, (30, 0), (70, 0));

        Assert.Equal((1.2f, 1f), (rig.Deck.Geometry.Scale, rig.Deck.Geometry.Stretch));
        Assert.Equal(700f, deck.Left);
        Assert.Equal(750f, deck.Top + deck.Height, 2);   // the block's bottom stays
        Assert.Equal(570f, deck.Top, 2);
        SpellDeckGeometry g = rig.Spell.Geometry;
        Assert.Equal((1.2f, 1f, 3), (g.Scale, g.Stretch, g.Rows));
        Assert.Equal(700f + 70f * 1.2f, rig.Spell.Left, 2);
        Assert.Equal(570f + 4f * 1.2f, rig.Spell.Top + rig.Spell.Height, 2);
        Assert.Equal(deck.Left + deck.Width, rig.Spell.Left + rig.Spell.Width, 2);

        Assert.Equal(1, rig.Resized[WindowNames.CommandDeck]);
        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.CommandDeck, default));
        Assert.Equal(570f, saved.Y, 2);
        Assert.Equal(180f, saved.Height, 2);
        Assert.Equal(spellStored, rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.SpellDeck, default));
    }

    [Fact]
    public void SpellDeck_TheAttachedCornerScaleStopsWhereTheBlockStillFitsOnScreen()
    {
        using var rig = new SpellDeckRig(PathName);
        // The Spell Deck's top on the screen's top: no room to grow upwards.
        var (deck, _) = AttachedBlock(rig, deckTop: SpellDeckGeometry.BaseHeight(3) - 4f);
        Assert.Equal(0f, rig.Spell.Top, 2);
        int x = (int)(rig.Spell.Left + rig.Spell.Width - 1f), y = (int)rig.Spell.Top + 1;

        DragThroughRoot(rig, x, y, (70, 0));

        Assert.Equal(1f, rig.Deck.Geometry.Scale);
        Assert.Equal(1f, rig.Spell.Geometry.Scale);
        Assert.Equal(SpellDeckGeometry.BaseHeight(3) - 4f, deck.Top, 2);
        Assert.Equal(0f, rig.Spell.Top, 2);
    }

    [Fact]
    public void SpellDeck_TheDetachedSpellDeckResizesAlone()
    {
        using var rig = new SpellDeckRig(PathName);
        var (deck, _) = AttachedBlock(rig);
        rig.Dock.Detach();
        rig.Frames(2);
        rig.Resized.Clear();
        int x = (int)(rig.Spell.Left + rig.Spell.Width - 1f), y = (int)(rig.Spell.Top + rig.Spell.Height / 2f);

        DragThroughRoot(rig, x, y, (84, 0));

        Assert.Equal(1.2f, rig.Spell.Geometry.Stretch, 3);
        Assert.Equal(DeckGeometry.Default, rig.Deck.Geometry);
        Assert.Equal((700f, 600f), (deck.Left, deck.Top));
        Assert.False(rig.Resized.ContainsKey(WindowNames.CommandDeck));
        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            rig.Store.LoadWindowLayout("Bob", "1600x900", WindowNames.SpellDeck, default));
        Assert.Equal(rig.Spell.Width, saved.Width, 2);
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
