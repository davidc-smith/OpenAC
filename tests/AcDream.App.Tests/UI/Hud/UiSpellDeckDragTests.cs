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
            clock: () => 0.0,
            isLocked: () => rig!.Locked,
            toggleClickCasts: () => rig!.Callbacks.Add("toggle click casts"),
            setAttached: a => rig!.Callbacks.Add($"attached {a}"));
        rig = new Rig { Deck = deck, Controller = c, Game = g };
        deck.Refresh();
        return rig;
    }

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
        Rig r = Make();
        Press(r.Deck, r.Deck.Geometry.Slot(2, 1));
        Assert.True(r.Deck.IsDragSource);
        Assert.Equal(new SpellDeckDragPayload(2, 31u), r.Deck.GetDragPayload());
        Assert.Equal((0x0600_0000u + 31u, 32, 32), r.Deck.GetDragGhost());
    }

    [Fact]
    public void AnEmptySlotTheFrameOrTheChipLiftNothing()
    {
        Rig r = Make();
        Press(r.Deck, r.Deck.Geometry.Slot(1, 3));
        Assert.Null(r.Deck.GetDragPayload());
        Press(r.Deck, r.Deck.Geometry.TabChip(0));
        Assert.False(r.Deck.IsDragSource);
        Assert.Null(r.Deck.GetDragPayload());
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
    public void ADropOnASlotInTheSameTabReorders()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 10u), r.Deck.Geometry.Slot(0, 2)));
        Assert.Equal(["set 0 2 10"], r.Game.Calls);
    }

    [Fact]
    public void ADropOnAnotherRowMovesAcrossTabs()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 11u), r.Deck.Geometry.Slot(1, 0)));
        Assert.Equal(["set 1 0 11", "remove 0 11"], r.Game.Calls);
    }

    [Fact]
    public void ASpellbookDropInserts()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), r.Deck.Geometry.Slot(2, 1)));
        Assert.Equal(["set 2 1 99"], r.Game.Calls);
    }

    [Fact]
    public void ADropOnAnEmptySlotGoesToTheEnd()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), r.Deck.Geometry.Slot(1, 5)));
        Assert.Equal(["set 1 1 99"], r.Game.Calls);
    }

    [Fact]
    public void ADropInTheRowBandBetweenSlotsGoesToTheEnd()
    {
        Rig r = Make();
        DeckRect s = r.Deck.Geometry.Slot(0, 4);
        int x = (int)(s.X + s.W + 1), y = (int)(s.Y + s.H / 2);
        Assert.Equal(SpellDeckZone.Frame, r.Deck.Geometry.HitAt(x, y).Zone);
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), x, y));
        Assert.Equal(["set 0 3 99"], r.Game.Calls);
    }

    [Fact]
    public void ADropAnywhereElseDoesNothing()
    {
        Rig r = Make();
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 10u), r.Deck.Geometry.ItemSlot));
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 10u), r.Deck.Geometry.TabChip(0)));
        Assert.True(Drop(r.Deck, new SpellDeckDragPayload(0, 10u), 2, 2));
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
    public void ARefusedDropSaysTheRowIsFull()
    {
        Rig r = Make(capacity: 3);
        Assert.True(Drop(r.Deck, new SpellbookShortcutDragPayload(99u), r.Deck.Geometry.Slot(0, 0)));
        Assert.Equal(["msg " + SpellDeckController.FullRowMessage], r.Game.Calls);
    }

    [Fact]
    public void ReleasedOutsideUiRemovesASlotOnly()
    {
        Rig r = Make();
        r.Deck.OnDragReleasedOutsideUi(new SpellbookShortcutDragPayload(10u));
        r.Deck.OnDragReleasedOutsideUi("an item");
        Assert.Empty(r.Game.Calls);
        r.Deck.OnDragReleasedOutsideUi(new SpellDeckDragPayload(0, 11u));
        Assert.Equal(["remove 0 11"], r.Game.Calls);
    }

    // ── Highlight ─────────────────────────────────────────────────

    [Fact]
    public void DragEnterHighlightsAcceptAndRejectAndTheLeaveClearsIt()
    {
        Rig r = Make(capacity: 3);
        var (x, y) = Centre(r.Deck.Geometry.Slot(1, 0));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellbookShortcutDragPayload(99u)));
        Assert.Equal((1, 0, true), r.Deck.DropHighlight);

        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragOver, Payload: new SpellbookShortcutDragPayload(99u)));
        Assert.Null(r.Deck.DropHighlight);

        (x, y) = Centre(r.Deck.Geometry.Slot(0, 1));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: x, Data2: y,
            Payload: new SpellbookShortcutDragPayload(99u)));
        Assert.Equal((0, 1, false), r.Deck.DropHighlight);   // tab 0 already fills the row

        Drop(r.Deck, new SpellbookShortcutDragPayload(99u), x, y);
        Assert.Null(r.Deck.DropHighlight);
        Assert.DoesNotContain(r.Game.Calls, c => !c.StartsWith("msg"));
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
    public void DraggingASlotToAnotherSlotReordersOnce()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 0));
        var (tx, ty) = Screen(r.Deck.Geometry.Slot(0, 2));
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
    public void AClickWithoutMovingStillOutlines()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(r.Deck.Geometry.Slot(0, 1));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.Null(root.DragSource);
        Assert.Equal(SpellDeckOutline.ForSlot(r.Controller.Rows[0].Id, 0, 11u), r.Controller.Outline);
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

    private static readonly string[] RowItems =
    [
        "Add row below", "Remove this row", "-",
        "Show tab 1 (shown)", "Show tab 2", "Show tab 3", "Show tab 4",
        "Show tab 5", "Show tab 6", "Show tab 7", "Show tab 8", "-",
        "Click casts: off", "-", "Detach from deck",
    ];

    private static IReadOnlyList<(string Text, bool Enabled)> MenuAt(Rig r, DeckRect rect)
    {
        var (x, y) = Centre(rect);
        return r.Deck.MenuItemsAt(x, y);
    }

    [Fact]
    public void ASlotMenuHasTheSlotItemsThenTheRowItems()
    {
        Rig r = Make();
        var items = MenuAt(r, r.Deck.Geometry.Slot(0, 1));
        Assert.Equal(["Examine spell", "Remove from row", "-", .. RowItems], items.Select(i => i.Text));
    }

    [Fact]
    public void AChipMenuHasTheRowItemsOnly()
    {
        Rig r = Make();
        var items = MenuAt(r, r.Deck.Geometry.TabChip(1));
        Assert.Equal(RowItems.Select(t => t.Replace("Show tab 1 (shown)", "Show tab 1").Replace("Show tab 2", "Show tab 2 (shown)")),
            items.Select(i => i.Text));
    }

    [Fact]
    public void NoMenuOffTheRows()
    {
        Rig r = Make();
        Assert.Empty(MenuAt(r, r.Deck.Geometry.ItemSlot));
    }

    [Fact]
    public void RowItemsFollowTheControllerAndTheModes()
    {
        Rig r = Make();
        r.CanAddRow = false;
        r.ClickCasts = true;
        r.Controller.SetAttached(false);
        r.Controller.RemoveRow(r.Controller.Rows[2].Id);
        r.Controller.RemoveRow(r.Controller.Rows[1].Id);
        r.Deck.Refresh();
        var items = MenuAt(r, r.Deck.Geometry.TabChip(0));
        Assert.Equal(("Add row below", false), items[0]);
        Assert.Equal(("Remove this row", false), items[1]);
        Assert.Equal("Click casts: on", items[12].Text);
        Assert.Equal("Attach to deck", items[14].Text);
    }

    [Fact]
    public void SelectingAnItemRunsIt()
    {
        Rig r = Make();
        DeckRect slot = r.Deck.Geometry.Slot(0, 1);
        var items = MenuAt(r, slot);
        r.Deck.SelectMenuItem(0);
        Assert.Equal(["examine 11"], r.Game.Calls);
        r.Deck.SelectMenuItem(2);   // separator
        Assert.Single(r.Game.Calls);
        r.Deck.SelectMenuItem(1);
        Assert.Equal(["examine 11", "remove 0 11"], r.Game.Calls);

        int rowId = r.Controller.Rows[0].Id;
        MenuAt(r, slot);
        r.Deck.SelectMenuItem(Index(items, "Show tab 5"));
        Assert.Equal(4, r.Controller.Rows[0].Tab);

        MenuAt(r, r.Deck.Geometry.TabChip(0));
        r.Deck.SelectMenuItem(0);
        Assert.Equal(4, r.Controller.Rows.Count);
        Assert.Equal(rowId, r.Controller.Rows[0].Id);

        var chip = MenuAt(r, r.Deck.Geometry.TabChip(1));
        r.Deck.SelectMenuItem(Index(chip, "Remove this row"));
        Assert.Equal(3, r.Controller.Rows.Count);

        chip = MenuAt(r, r.Deck.Geometry.TabChip(0));
        r.Deck.SelectMenuItem(Index(chip, "Click casts: off"));
        r.Deck.SelectMenuItem(Index(chip, "Detach from deck"));
        Assert.Equal(["toggle click casts", "attached False"], r.Callbacks);
    }

    [Fact]
    public void SlotItemsActOnTheSpellTheMenuOpenedOn_NotTheSlot()
    {
        // Opened on spell 11; another spell is put in front, so 11 moves to slot 2.
        Rig r = Make();
        MenuAt(r, r.Deck.Geometry.Slot(0, 1));
        r.Game.Tabs[0].Insert(0, 99u);
        r.Deck.SelectMenuItem(1);   // Remove from row
        Assert.Equal(["remove 0 11"], r.Game.Calls);
        r.Deck.SelectMenuItem(0);   // Examine spell
        Assert.Equal(["remove 0 11", "examine 11"], r.Game.Calls);
    }

    [Fact]
    public void SlotItemsDoNothingWhenTheSpellLeftTheRow()
    {
        // Opened on spell 11; 11 is removed elsewhere, so 12 takes its slot.
        Rig r = Make();
        MenuAt(r, r.Deck.Geometry.Slot(0, 1));
        r.Game.Tabs[0].Remove(11u);
        r.Deck.SelectMenuItem(1);
        r.Deck.SelectMenuItem(0);
        Assert.Empty(r.Game.Calls);

        // The row now shows another tab.
        MenuAt(r, r.Deck.Geometry.Slot(0, 1));   // spell 12
        r.Controller.SetRowTab(r.Controller.Rows[0].Id, 2);
        r.Deck.SelectMenuItem(1);
        Assert.Empty(r.Game.Calls);
    }

    [Fact]
    public void ADisabledItemDoesNothing()
    {
        Rig r = Make();
        r.CanAddRow = false;
        MenuAt(r, r.Deck.Geometry.TabChip(0));
        r.Deck.SelectMenuItem(0);
        Assert.Equal(3, r.Controller.Rows.Count);
    }

    [Fact]
    public void ARightClickOpensTheMenu()
    {
        var (root, r) = Mount();
        var (x, y) = Screen(r.Deck.Geometry.TabChip(0));
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Right, x, y);
        root.OnMouseUp(UiMouseButton.Right, x, y);
        Assert.Contains(r.Deck.Children, c => c is UiMarkupContextMenu { IsOpen: true });
    }

    private static int Index(IReadOnlyList<(string Text, bool Enabled)> items, string text) =>
        items.Select(i => i.Text).ToList().IndexOf(text);
}
