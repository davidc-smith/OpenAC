using AcDream.App.Spells;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The Spell Deck's drag transaction and right-click menu.</summary>
public sealed class UiSpellDeckDragTests
{
    private sealed class Game : ISpellDeckActions
    {
        public readonly List<uint>[] Tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        public readonly List<string> Calls = [];
        public void CastSpell(uint id) => Calls.Add($"cast {id}");
        public void CastItemSpell(uint itemId) => Calls.Add($"item {itemId}");
        public void SetFavorite(int tab, int pos, uint id) => Calls.Add($"set {tab} {pos} {id}");
        public void RemoveFavorite(int tab, uint id) => Calls.Add($"remove {tab} {id}");
        public void ExamineSpell(uint id) => Calls.Add($"examine {id}");
        public void ShowMessage(string text) => Calls.Add("msg " + text);
    }

    private sealed class Rig
    {
        public required UiSpellDeck Deck;
        public required SpellDeckController Controller;
        public required Game Game;
        public bool Locked;
        public bool ClickCasts;
        public bool CanAddRow = true;
        public double Now;
        public readonly List<string> Callbacks = [];
    }

    private static Rig Make(int capacity = 12)
    {
        var g = new Game();
        g.Tabs[0].AddRange([10u, 11u, 12u]);
        g.Tabs[1].AddRange([20u]);
        g.Tabs[2].AddRange([30u, 31u]);
        Rig? rig = null;
        var c = new SpellDeckController(
            g, favorites: tab => g.Tabs[tab], itemSpell: () => null,
            capacity: () => capacity, settings: SpellDeckSettings.Default, save: _ => { },
            clickCasts: () => rig!.ClickCasts, canAddRow: () => rig!.CanAddRow);
        var deck = new UiSpellDeck(
            c,
            spellIcon: id => 0x0600_0000u + id,
            spellInfo: _ => null,
            keyLabel: _ => string.Empty,
            pendingSpell: () => null,
            clock: () => rig!.Now,
            isLocked: () => rig!.Locked,
            toggleClickCasts: () => rig!.Callbacks.Add("toggle click casts"),
            setAttached: a => rig!.Callbacks.Add($"attached {a}"));
        rig = new Rig { Deck = deck, Controller = c, Game = g };
        deck.Refresh();
        return rig;
    }

    private static DeckRect Mark(Rig r, int tab) => r.Deck.Geometry.Bookmark(tab, r.Controller.TabCount);
    private static DeckRect Plus(Rig r) => r.Deck.Geometry.Plus(r.Controller.TabCount);

    private static (int X, int Y) Centre(DeckRect r) => ((int)(r.X + r.W / 2), (int)(r.Y + r.H / 2));

    /// <summary>A press as the root makes it: the hit test, then the MouseDown.</summary>
    private static void Press(UiSpellDeck d, DeckRect r)
    {
        var (x, y) = Centre(r);
        d.HitTestLocal(x, y);
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseDown, Data1: x, Data2: y));
    }

    private static bool Drop(UiSpellDeck d, object payload, int x, int y) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.DropReleased, Data1: x, Data2: y, Payload: payload));

    private static bool Drop(UiSpellDeck d, object payload, DeckRect r)
    {
        var (x, y) = Centre(r);
        return Drop(d, payload, x, y);
    }

    // ── Drag source ───────────────────────────────────────────────

    [Fact]
    public void AFilledSlotLiftsItsSpell()
    {
        Rig r = Make(capacity: 2);   // rows show tab 0 two at a time: row 1 starts with 12
        Press(r.Deck, r.Deck.Geometry.Slot(1, 0));
        Assert.True(r.Deck.IsDragSource);
        Assert.Equal(new SpellDeckDragPayload(0, 1, 0, 12u), r.Deck.GetDragPayload());
        Assert.Equal((0x0600_0000u + 12u, 32, 32), r.Deck.GetDragGhost());
        r.Controller.ShowTab(2);
        Press(r.Deck, r.Deck.Geometry.Slot(0, 1));
        Assert.Equal(new SpellDeckDragPayload(2, 0, 1, 31u), r.Deck.GetDragPayload());
    }

    [Fact]
    public void AnEmptySlotTheFrameOrABookmarkLiftNothing()
    {
        Rig r = Make();
        Press(r.Deck, r.Deck.Geometry.Slot(1, 3));
        Assert.Null(r.Deck.GetDragPayload());
        Press(r.Deck, Mark(r, 0));
        Assert.False(r.Deck.IsDragSource);
        Assert.Null(r.Deck.GetDragPayload());
        Press(r.Deck, Plus(r));
        Assert.False(r.Deck.IsDragSource);
        Press(r.Deck, r.Deck.Geometry.ItemSlot);
        Assert.Null(r.Deck.GetDragPayload());
    }

    [Fact]
    public void ALockedDeckLiftsNothing()
    {
        Rig r = Make();
        r.Locked = true;
        Press(r.Deck, r.Deck.Geometry.Slot(0, 0));
        Assert.False(r.Deck.IsDragSource);
        Assert.Null(r.Deck.GetDragPayload());
    }

    [Fact]
    public void StartingAndCancellingADragChangesNothing()
    {
        Rig r = Make();
        Press(r.Deck, r.Deck.Geometry.Slot(0, 1));
        object p = r.Deck.GetDragPayload()!;
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragBegin, Payload: p));
        r.Deck.SetDragSourceActive(true, p);
        r.Deck.SetDragSourceActive(false, p);
        Assert.Empty(r.Game.Calls);
        Assert.Equal([10u, 11u, 12u], r.Game.Tabs[0]);
    }

    // ── Drops ─────────────────────────────────────────────────────

    [Fact]
    public void ASlotDroppedOnAFilledSlotInTheSameTabSwaps()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 0, 10u), r.Deck.Geometry.Slot(0, 2)));
        Assert.Equal((12u, 10u), (r.Controller.SlotSpell(0, 0)!.Value, r.Controller.SlotSpell(0, 2)!.Value));
        Assert.DoesNotContain(r.Game.Calls, c => c.StartsWith("remove"));
    }

    [Fact]
    public void ASlotDroppedOnAnEmptySlotInAnotherRowMovesThere()
    {
        Rig r = Make(capacity: 2);   // 10 11 / 12 _
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 0, 10u), r.Deck.Geometry.Slot(1, 1)));
        Assert.Equal(["set 0 2 10"], r.Game.Calls);
        Assert.Equal(10u, r.Controller.SlotSpell(1, 1));
        Assert.Null(r.Controller.SlotSpell(0, 0));
    }

    [Fact]
    public void ASpellbookDropOnAnEmptySlotPlacesIt()
    {
        Rig r = Make(capacity: 2);
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), r.Deck.Geometry.Slot(1, 1)));
        Assert.Equal(["set 0 3 99"], r.Game.Calls);
    }

    [Fact]
    public void ADropOnAnEmptySlotPastAGapLandsInThatSlot()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), r.Deck.Geometry.Slot(1, 5)));
        Assert.Equal(["set 0 3 99"], r.Game.Calls);
        Assert.Equal(99u, r.Controller.SlotSpell(1, 5));
    }

    [Fact]
    public void ADropInTheRowBandBetweenSlotsGoesToTheSlotOnItsLeft()
    {
        Rig r = Make();
        DeckRect s = r.Deck.Geometry.Slot(1, 1);
        int x = (int)(s.X + s.W + 1), y = (int)(s.Y + s.H / 2);
        Assert.Equal(SpellDeckZone.Frame, r.Deck.Geometry.HitAt(x, y, r.Controller.TabCount).Zone);
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), x, y));
        Assert.Equal(99u, r.Controller.SlotSpell(1, 1));
    }

    [Fact]
    public void ADropAnywhereElseDoesNothing()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 0, 10u), r.Deck.Geometry.ItemSlot));
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 0, 10u), Plus(r)));
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 0, 10u), 2, 2));
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void AnotherPayloadIsNotTaken()
    {
        Rig r = Make();
        Assert.False(Drop(r.Deck, "an item", r.Deck.Geometry.Slot(0, 0)));
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void ASpellbookDropOnAFilledSlotReplacesIt()
    {
        Rig r = Make(capacity: 1);   // 3 rows of 1: tab 0's three spells fill the deck
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), r.Deck.Geometry.Slot(0, 0)));
        Assert.Equal(["remove 0 10", "set 0 0 99"], r.Game.Calls);
    }

    [Fact]
    public void ASlotDroppedOnABookmarkMovesToThatTabsFirstEmptySlot()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 1, 11u), Mark(r, 2)));
        Assert.Equal(["set 2 2 11", "remove 0 11"], r.Game.Calls);
    }

    [Fact]
    public void ASpellbookSpellDroppedOnABookmarkIsAddedThere()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), Mark(r, 1)));
        Assert.Equal(["set 1 1 99"], r.Game.Calls);
    }

    [Fact]
    public void ADropOnALocalBookmarkStaysLocal()
    {
        Rig r = Make();
        r.Controller.AddLocalTab();
        r.Controller.ShowTab(0);
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), Mark(r, 8)));
        Assert.Empty(r.Game.Calls);
        Assert.Equal([99u], r.Controller.Favorites(8));
    }

    [Fact]
    public void ASlotOnItsOwnBookmarkDoesNothing_AndADuplicateNeverReachesTheServer()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 1, 11u), Mark(r, 0)));
        Assert.Equal(11u, r.Controller.SlotSpell(0, 1));
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(20u), Mark(r, 1)));
        Assert.Equal(20u, r.Controller.LayoutOf(1).Get(0, 1));   // a second copy, kept locally
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void ARefusedBookmarkDropSaysTheTabIsFull()
    {
        Rig r = Make(capacity: 1);   // 3 rows of 1: a tab holds 3
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 0, 0, 10u), Mark(r, 2)));
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), Mark(r, 2)));   // now full
        Assert.Equal(["set 2 2 10", "remove 0 10", "msg " + SpellDeckController.FullTabMessage], r.Game.Calls);
    }

    [Fact]
    public void ReleasedOutsideUiRemovesASlotOnly()
    {
        Rig r = Make();
        r.Deck.OnDragReleasedOutsideUi(new SpellbookShortcutDragPayload(10u));
        r.Deck.OnDragReleasedOutsideUi("an item");
        Assert.Empty(r.Game.Calls);
        r.Deck.OnDragReleasedOutsideUi(new SpellDeckDragPayload(0, 0, 1, 11u));
        Assert.Equal(["remove 0 11"], r.Game.Calls);
    }

    // ── Highlight ─────────────────────────────────────────────────

    [Fact]
    public void DragEnterHighlightsAcceptAndRejectAndTheLeaveClearsIt()
    {
        Rig r = Make(capacity: 1);   // 3 rows of 1: tab 0's three spells fill the deck
        var (x, y) = Centre(r.Deck.Geometry.Slot(1, 0));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellDeckDragPayload(0, 0, 0, 10u)));
        Assert.Equal((1, 0, true), r.Deck.DropHighlight);    // a swap

        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragOver, Payload: new SpellDeckDragPayload(0, 0, 0, 10u)));
        Assert.Null(r.Deck.DropHighlight);

        (x, y) = Centre(r.Deck.Geometry.Slot(0, 0));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellbookShortcutDragPayload(99u)));
        Assert.Equal((0, 0, true), r.Deck.DropHighlight);    // a replace

        var stale = new SpellDeckDragPayload(0, 2, 0, 77u);  // its slot no longer holds it
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y, Payload: stale));
        Assert.Equal((0, 0, false), r.Deck.DropHighlight);

        Drop(r.Deck, stale, x, y);
        Assert.Null(r.Deck.DropHighlight);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void DragEnterOnABookmarkHighlightsItFromThePlan()
    {
        Rig r = Make(capacity: 1);   // 3 rows of 1: tab 0 is full, tab 1 has room
        var (x, y) = Centre(Mark(r, 1));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellDeckDragPayload(0, 0, 0, 10u)));
        Assert.Equal((1, true), r.Deck.BookmarkDropHighlight);
        Assert.Null(r.Deck.DropHighlight);

        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragOver, Payload: new SpellDeckDragPayload(0, 0, 0, 10u)));
        Assert.Null(r.Deck.BookmarkDropHighlight);

        (x, y) = Centre(Mark(r, 0));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellbookShortcutDragPayload(99u)));
        Assert.Equal((0, false), r.Deck.BookmarkDropHighlight);   // full

        (x, y) = Centre(Mark(r, 1));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellbookShortcutDragPayload(20u)));
        Assert.Equal((1, true), r.Deck.BookmarkDropHighlight);    // a duplicate is allowed

        (x, y) = Centre(r.Deck.Geometry.Slot(0, 0));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellDeckDragPayload(0, 0, 0, 10u)));
        Assert.Null(r.Deck.BookmarkDropHighlight);
        Assert.NotNull(r.Deck.DropHighlight);

        (x, y) = Centre(Plus(r));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellDeckDragPayload(0, 0, 0, 10u)));
        Assert.Null(r.Deck.BookmarkDropHighlight);
        Assert.Null(r.Deck.DropHighlight);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void DragEnterWithAnotherPayloadHighlightsNothing()
    {
        Rig r = Make();
        var (x, y) = Centre(r.Deck.Geometry.Slot(0, 0));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y, Payload: "an item"));
        Assert.Null(r.Deck.DropHighlight);
    }

    // ── Through a real root ───────────────────────────────────────

    private const float Left = 100f, Top = 300f;

    private static (UiRoot Root, Rig Rig) Mount()
    {
        Rig r = Make();
        var root = new UiRoot { Width = 800, Height = 600 };
        RetailWindowFrame.Mount(root, r.Deck, _ => (0u, 0, 0), UiSpellDeck.MountOptions(Left, Top) with { Visible = true });
        root.DragReleasedOutsideUi += (p, _, _) => r.Deck.OnDragReleasedOutsideUi(p);
        return (root, r);
    }

    private static (int X, int Y) Screen(DeckRect rect) => ((int)(Left + rect.X + rect.W / 2), (int)(Top + rect.Y + rect.H / 2));

    [Fact]
    public void DraggingASlotToAnEmptySlotMovesItOnce()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 0));
        var (tx, ty) = Screen(r.Deck.Geometry.Slot(0, 4));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y);
        Assert.NotNull(root.DragSource);
        Assert.Empty(r.Game.Calls);
        root.OnMouseMove(tx, ty);
        root.OnMouseUp(UiMouseButton.Left, tx, ty);
        Assert.Equal(["set 0 2 10"], r.Game.Calls);
        Assert.Equal(Left, r.Deck.Left, 1f);
    }

    [Fact]
    public void CtrlHeldAtTheReleaseCopiesASlot_ADuplicateNeverReachesTheServer()
    {
        var (root, r) = Mount();
        var keyboard = new AcDream.App.Tests.Plugins.FakeKeyboard();
        root.Keyboard = keyboard;
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 0));
        var (tx, ty) = Screen(r.Deck.Geometry.Slot(1, 3));   // an empty slot in another row
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y);
        root.OnMouseMove(tx, ty);
        keyboard.Held.Add(Silk.NET.Input.Key.ControlLeft);   // pressed during the drag: the release decides
        root.OnMouseUp(UiMouseButton.Left, tx, ty);

        Assert.Equal(10u, r.Controller.SlotSpell(0, 0));
        Assert.Equal(10u, r.Controller.SlotSpell(1, 3));
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void CtrlHeldAtTheReleaseCopiesOverAFilledSlot_AndWithoutItTheSlotsSwap()
    {
        var (root, r) = Mount();
        var keyboard = new AcDream.App.Tests.Plugins.FakeKeyboard();
        root.Keyboard = keyboard;
        keyboard.Held.Add(Silk.NET.Input.Key.ControlRight);
        DragSlot(root, r, (0, 0), (0, 2));
        Assert.Equal((10u, 11u, 10u), (r.Controller.SlotSpell(0, 0)!.Value, r.Controller.SlotSpell(0, 1)!.Value,
            r.Controller.SlotSpell(0, 2)!.Value));
        Assert.Equal(["remove 0 12"], r.Game.Calls);   // 12's last copy was overwritten

        keyboard.Held.Clear();
        r.Game.Calls.Clear();
        DragSlot(root, r, (0, 1), (0, 2));
        Assert.Equal((11u, 10u), (r.Controller.SlotSpell(0, 2)!.Value, r.Controller.SlotSpell(0, 1)!.Value));
    }

    [Fact]
    public void ASpellbookDropOnAnyRowsSlot_EmptyOrFilled_LandsInThatSlot()
    {
        Rig r = Make();
        Assert.Equal(10, r.Deck.Geometry.SlotsPerRow);
        foreach (var (row, col, spell) in new[] { (1, 0, 91u), (2, 9, 92u), (0, 1, 93u), (2, 0, 94u) })
        {
            Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(spell), r.Deck.Geometry.Slot(row, col)));
            Assert.Equal(spell, r.Controller.SlotSpell(row, col));
        }
        Assert.Equal((10u, 12u), (r.Controller.SlotSpell(0, 0)!.Value, r.Controller.SlotSpell(0, 2)!.Value));
    }

    [Fact]
    public void CtrlHeldTurnsTheHighlightIntoACopy_ASlotOnItsOwnBookmarkThenAccepts()
    {
        var (root, r) = Mount();
        var keyboard = new AcDream.App.Tests.Plugins.FakeKeyboard();
        root.Keyboard = keyboard;
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 0));
        var (bx, by) = Screen(Mark(r, 0));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y);
        root.OnMouseMove(bx, by);
        root.Tick(0.016, 16);
        Assert.Equal((0, false), r.Deck.BookmarkDropHighlight);   // a move onto its own tab does nothing

        keyboard.Held.Add(Silk.NET.Input.Key.ControlLeft);
        root.Tick(0.016, 16);
        Assert.Equal((0, true), r.Deck.BookmarkDropHighlight);    // a copy into the first empty slot
        root.OnMouseUp(UiMouseButton.Left, bx, by);

        Assert.Equal(10u, r.Controller.SlotSpell(0, 0));
        Assert.Equal(10u, r.Controller.SlotSpell(0, 3));
        Assert.Empty(r.Game.Calls);
    }

    private static void DragSlot(UiRoot root, Rig r, (int Row, int Col) from, (int Row, int Col) to)
    {
        var (x, y) = Screen(r.Deck.Geometry.Slot(from.Row, from.Col));
        var (tx, ty) = Screen(r.Deck.Geometry.Slot(to.Row, to.Col));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y);
        root.OnMouseMove(tx, ty);
        root.OnMouseUp(UiMouseButton.Left, tx, ty);
    }

    [Fact]
    public void AClickWithoutMovingStillOutlines()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 1));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.Null(root.DragSource);
        Assert.Equal(SpellDeckMark.Slot(0, 0, 1), r.Controller.Outline);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void DraggingASlotOffEveryWindowRemovesIt()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 1));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y);
        root.OnMouseMove(600, 50);
        root.OnMouseUp(UiMouseButton.Left, 600, 50);
        Assert.Equal(["remove 0 11"], r.Game.Calls);
    }

    // ── Right-click menu ──────────────────────────────────────────

    private static readonly string[] DeckItems =
    [
        "Add row", "Remove row", "-",
        "Click casts: off", "-", "Detach from deck",
    ];

    private static IReadOnlyList<(string Text, bool Enabled)> MenuAt(Rig r, DeckRect rect)
    {
        var (x, y) = Centre(rect);
        return r.Deck.MenuItemsAt(x, y);
    }

    [Fact]
    public void ASlotMenuHasTheSlotItemsThenTheDeckItems()
    {
        Rig r = Make();
        var items = MenuAt(r, r.Deck.Geometry.Slot(0, 1));
        Assert.Equal(["Examine spell", "Remove from tab", "-", .. DeckItems], items.Select(i => i.Text));
    }

    [Fact]
    public void AnywhereElseOnTheDeckHasTheDeckItemsOnly()
    {
        Rig r = Make();
        SpellDeckGeometry g = r.Deck.Geometry;
        DeckRect frame = new(g.Width - 3, g.Height / 2, 1, 1);
        foreach (DeckRect at in new[] { g.Slot(2, 5), g.ItemSlot, Mark(r, 0), Mark(r, 7), Plus(r), frame })
            Assert.Equal(DeckItems, MenuAt(r, at).Select(i => i.Text));
        Assert.Empty(r.Deck.MenuItemsAt(-5, -5));
    }

    [Fact]
    public void TheLastLocalBookmarkHasRemoveTabOnly()
    {
        Rig r = Make();
        r.Controller.AddLocalTab();
        r.Controller.AddLocalTab();
        Assert.Equal(DeckItems, MenuAt(r, Mark(r, 8)).Select(i => i.Text));   // local, not the last
        var items = MenuAt(r, Mark(r, 9));
        Assert.Equal([("Remove tab", true)], items);

        r.Controller.ShowTab(9);
        r.Deck.SelectMenuItem(0);
        Assert.Equal(9, r.Controller.TabCount);
        Assert.Equal(8, r.Controller.Tab);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void RemoveTabDoesNothingWhenTheTabIsNoLongerTheLast()
    {
        Rig r = Make();
        r.Controller.AddLocalTab();
        MenuAt(r, Mark(r, 8));
        r.Controller.AddLocalTab();   // tab 8 is no longer the last
        r.Deck.SelectMenuItem(0);
        Assert.Equal(10, r.Controller.TabCount);

        MenuAt(r, Mark(r, 9));
        r.Controller.RemoveLastLocalTab();   // tab 9 is gone
        r.Deck.SelectMenuItem(0);
        Assert.Equal(9, r.Controller.TabCount);
    }

    [Fact]
    public void RemoveTabDoesNothingWhenTheLastTabWasReplacedSinceTheMenuOpened()
    {
        Rig r = Make();
        r.Controller.AddLocalTab();
        r.Controller.Tabs.Apply([new(8, SpellDeckLayout.FromSlots([(0, 0, 99u)]))]);
        MenuAt(r, Mark(r, 8));
        r.Controller.RemoveLastLocalTab();
        r.Controller.AddLocalTab();   // a new tab 9, again the last local one
        r.Deck.SelectMenuItem(0);
        Assert.Equal(9, r.Controller.TabCount);

        MenuAt(r, Mark(r, 8));   // opened on this tab: it goes
        r.Deck.SelectMenuItem(0);
        Assert.Equal(8, r.Controller.TabCount);
    }

    [Fact]
    public void DeckItemsFollowTheControllerAndTheModes()
    {
        Rig r = Make();
        r.CanAddRow = false;
        r.ClickCasts = true;
        r.Controller.SetAttached(false);
        r.Controller.RemoveRow();
        r.Controller.RemoveRow();
        r.Deck.Refresh();
        var items = MenuAt(r, r.Deck.Geometry.ItemSlot);
        Assert.Equal(("Add row", false), items[0]);
        Assert.Equal(("Remove row", false), items[1]);
        Assert.Equal("Click casts: on", items[3].Text);
        Assert.Equal("Attach to deck", items[5].Text);
    }

    [Fact]
    public void SelectingAnItemRunsIt()
    {
        Rig r = Make();
        DeckRect slot = r.Deck.Geometry.Slot(0, 1);
        MenuAt(r, slot);
        r.Deck.SelectMenuItem(0);
        Assert.Equal(["examine 11"], r.Game.Calls);
        r.Deck.SelectMenuItem(2);   // separator
        Assert.Single(r.Game.Calls);
        r.Deck.SelectMenuItem(1);
        Assert.Equal(["examine 11", "remove 0 11"], r.Game.Calls);

        MenuAt(r, Mark(r, 0));
        r.Deck.SelectMenuItem(0);
        Assert.Equal(4, r.Controller.RowCount);

        var deck = MenuAt(r, r.Deck.Geometry.ItemSlot);
        r.Deck.SelectMenuItem(Index(deck, "Remove row"));
        Assert.Equal(3, r.Controller.RowCount);

        deck = MenuAt(r, Plus(r));
        r.Deck.SelectMenuItem(Index(deck, "Click casts: off"));
        r.Deck.SelectMenuItem(Index(deck, "Detach from deck"));
        Assert.Equal(["toggle click casts", "attached False"], r.Callbacks);
    }

    [Fact]
    public void SlotItemsDoNothingWhenTheSlotNoLongerHoldsTheSpell()
    {
        // Opened on spell 11 in slot (0, 1); 10 is then swapped into that slot.
        Rig r = Make();
        MenuAt(r, r.Deck.Geometry.Slot(0, 1));
        r.Controller.DropOnSlot(new SpellDeckDragSource(0, 0, 0, 10u), 0, 1, copy: false);
        r.Game.Calls.Clear();
        r.Deck.SelectMenuItem(1);   // Remove from tab
        r.Deck.SelectMenuItem(0);   // Examine spell
        Assert.Empty(r.Game.Calls);
        Assert.Equal(10u, r.Controller.SlotSpell(0, 1));
    }

    [Fact]
    public void SlotItemsDoNothingWhenTheSpellLeftTheTab()
    {
        // Opened on spell 11; 11 is removed elsewhere and the deck reconciles.
        Rig r = Make();
        MenuAt(r, r.Deck.Geometry.Slot(0, 1));
        r.Game.Tabs[0].Remove(11u);
        r.Controller.ServerFavoritesChanged();
        r.Deck.SelectMenuItem(1);
        r.Deck.SelectMenuItem(0);
        Assert.Empty(r.Game.Calls);

        // The deck now shows another tab.
        MenuAt(r, r.Deck.Geometry.Slot(0, 2));   // spell 12
        r.Controller.ShowTab(2);
        r.Deck.SelectMenuItem(1);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void ADisabledItemDoesNothing()
    {
        Rig r = Make();
        r.CanAddRow = false;
        MenuAt(r, r.Deck.Geometry.ItemSlot);
        r.Deck.SelectMenuItem(0);
        Assert.Equal(3, r.Controller.RowCount);
    }

    [Fact]
    public void ARightClickOpensTheMenu()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(Mark(r, 0));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
        Assert.Contains(r.Deck.Children, c => c is UiMarkupContextMenu { IsOpen: true });
    }

    [Fact]
    public void ClickingABookmarkThroughTheRootShowsThatTab_AndDoesNotMoveTheWindow()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(Mark(r, 2));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y + 10);
        root.OnMouseUp(UiMouseButton.Left, x + 10, y + 10);
        Assert.Equal(Left, r.Deck.Left, 1f);
        Assert.Null(root.DragSource);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.Equal(2, r.Controller.Tab);
    }

    /// <summary>A left press and release at a screen point, with the root's clock and the deck's at <paramref name="ms"/>.</summary>
    private static void ClickAt(UiRoot root, Rig r, int x, int y, long ms)
    {
        r.Now = ms / 1000.0;
        root.Tick(0.0, ms);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    [Fact]
    public void ADoubleClickOnThePlusAddsOneTab_ASlowSecondClickAddsAnother()
    {
        var (root, r) = Mount();
        for (int i = 0; i < 4; i++) r.Controller.AddLocalTab();
        Assert.Equal(12, r.Controller.TabCount);
        Assert.Equal(1f, r.Deck.Geometry.Stretch);
        var (x, y) = Screen(Plus(r));
        ClickAt(root, r, x, y, 10_000);
        ClickAt(root, r, x, y, 10_200);   // a double-click: the + is still under the pointer
        Assert.Equal(SpellDeckZone.Plus, r.Deck.Geometry.HitAt(x - Left, y - Top, r.Controller.TabCount).Zone);
        Assert.Equal(13, r.Controller.TabCount);

        ClickAt(root, r, x, y, 10_200 + UiSpellDeck.PlusRepeatMs + 100);
        Assert.Equal(14, r.Controller.TabCount);
    }

    [Fact]
    public void DraggingASlotOntoABookmarkThroughTheRootMovesIt()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 0));
        var (tx, ty) = Screen(Mark(r, 1));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y);
        root.OnMouseMove(tx, ty);
        root.OnMouseUp(UiMouseButton.Left, tx, ty);
        Assert.Equal(["set 1 1 10", "remove 0 10"], r.Game.Calls);
    }

    private static int Index(IReadOnlyList<(string Text, bool Enabled)> items, string text) =>
        items.Select(i => i.Text).ToList().IndexOf(text);
}
