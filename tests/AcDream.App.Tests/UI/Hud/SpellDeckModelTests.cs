using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>Drop planning over slot layouts, and the rows/columns every tab needs.</summary>
public sealed class SpellDeckModelTests
{
    private static SpellDeckLayout L(params (int Row, int Col, uint Spell)[] slots) => SpellDeckLayout.FromSlots(slots);

    private static Func<int, SpellDeckLayout> Tabs(params (int Tab, SpellDeckLayout Layout)[] tabs) =>
        tab => tabs.FirstOrDefault(t => t.Tab == tab).Layout ?? SpellDeckLayout.Empty;

    private static SpellDeckDragSource Slot(int tab, int row, int col, uint spell) => new(tab, row, col, spell);
    private static readonly SpellDeckDragSource Book7 = SpellDeckDragSource.FromSpellbook(7u);

    // ── Drop on a slot ───────────────────────────────────────────

    [Fact]
    public void Spellbook_OnAnEmptySlot_Places_OnAFilledSlot_Replaces_OnItsOwnSpell_DoesNothing()
    {
        var tabs = Tabs((0, L((0, 0, 5u), (1, 2, 7u))));
        SpellDeckEdit place = SpellDeckModel.PlanDropOnSlot(Book7, 0, 1, 1, copy: false, tabs)!;
        Assert.Equal(SpellDeckEditKind.Place, place.Kind);
        Assert.Equal([new SpellDeckTabLayout(0, L((0, 0, 5u), (1, 1, 7u), (1, 2, 7u)))], place.Layouts);

        SpellDeckEdit replace = SpellDeckModel.PlanDropOnSlot(Book7, 0, 0, 0, copy: false, tabs)!;
        Assert.Equal(SpellDeckEditKind.Replace, replace.Kind);
        Assert.Equal([new SpellDeckTabLayout(0, L((0, 0, 7u), (1, 2, 7u)))], replace.Layouts);

        SpellDeckEdit same = SpellDeckModel.PlanDropOnSlot(Book7, 0, 1, 2, copy: false, tabs)!;
        Assert.Equal(SpellDeckEditKind.None, same.Kind);
        Assert.Empty(same.Layouts);
    }

    [Fact]
    public void SlotInTheSameTab_OnAnEmptySlot_Moves_OnAFilledSlot_Swaps_OnItself_DoesNothing()
    {
        var tabs = Tabs((2, L((0, 0, 5u), (0, 1, 6u))));
        SpellDeckEdit move = SpellDeckModel.PlanDropOnSlot(Slot(2, 0, 0, 5u), 2, 3, 4, copy: false, tabs)!;
        Assert.Equal(SpellDeckEditKind.Move, move.Kind);
        Assert.Equal([new SpellDeckTabLayout(2, L((0, 1, 6u), (3, 4, 5u)))], move.Layouts);

        SpellDeckEdit swap = SpellDeckModel.PlanDropOnSlot(Slot(2, 0, 0, 5u), 2, 0, 1, copy: false, tabs)!;
        Assert.Equal(SpellDeckEditKind.Swap, swap.Kind);
        Assert.Equal([new SpellDeckTabLayout(2, L((0, 0, 6u), (0, 1, 5u)))], swap.Layouts);

        Assert.Equal(SpellDeckEditKind.None, SpellDeckModel.PlanDropOnSlot(Slot(2, 0, 0, 5u), 2, 0, 0, false, tabs)!.Kind);
    }

    [Fact]
    public void Copy_KeepsTheSource_AndOverwritesAFilledTarget()
    {
        var tabs = Tabs((0, L((0, 0, 5u), (0, 1, 6u))));
        SpellDeckEdit dup = SpellDeckModel.PlanDropOnSlot(Slot(0, 0, 0, 5u), 0, 2, 2, copy: true, tabs)!;
        Assert.Equal(SpellDeckEditKind.Copy, dup.Kind);
        Assert.Equal([new SpellDeckTabLayout(0, L((0, 0, 5u), (0, 1, 6u), (2, 2, 5u)))], dup.Layouts);

        SpellDeckEdit over = SpellDeckModel.PlanDropOnSlot(Slot(0, 0, 0, 5u), 0, 0, 1, copy: true, tabs)!;
        Assert.Equal([new SpellDeckTabLayout(0, L((0, 0, 5u), (0, 1, 5u)))], over.Layouts);
    }

    [Fact]
    public void SlotFromAnotherTab_MovesOrSwapsAcross_DestinationFirst_OrCopies()
    {
        var tabs = Tabs((0, L((0, 0, 5u))), (8, L((1, 1, 9u))));
        SpellDeckEdit move = SpellDeckModel.PlanDropOnSlot(Slot(0, 0, 0, 5u), 8, 0, 0, false, tabs)!;
        Assert.Equal(SpellDeckEditKind.Move, move.Kind);
        Assert.Equal([new SpellDeckTabLayout(8, L((0, 0, 5u), (1, 1, 9u))), new SpellDeckTabLayout(0, SpellDeckLayout.Empty)], move.Layouts);

        SpellDeckEdit swap = SpellDeckModel.PlanDropOnSlot(Slot(0, 0, 0, 5u), 8, 1, 1, false, tabs)!;
        Assert.Equal(SpellDeckEditKind.Swap, swap.Kind);
        Assert.Equal([new SpellDeckTabLayout(8, L((1, 1, 5u))), new SpellDeckTabLayout(0, L((0, 0, 9u)))], swap.Layouts);

        SpellDeckEdit copy = SpellDeckModel.PlanDropOnSlot(Slot(0, 0, 0, 5u), 8, 1, 1, true, tabs)!;
        Assert.Equal(SpellDeckEditKind.Copy, copy.Kind);
        Assert.Equal([new SpellDeckTabLayout(8, L((1, 1, 5u)))], copy.Layouts);
    }

    [Fact]
    public void AStaleSlotSource_PlansNothing()
    {
        var tabs = Tabs((0, L((0, 0, 5u))));
        Assert.Null(SpellDeckModel.PlanDropOnSlot(Slot(0, 0, 0, 6u), 0, 1, 1, false, tabs));
        Assert.Null(SpellDeckModel.PlanDropOnTab(Slot(0, 0, 3, 5u), 1, false, 3, 4, tabs));
        Assert.Null(SpellDeckModel.PlanClear(Slot(0, 0, 3, 5u), tabs));
    }

    // ── Drop on a bookmark ───────────────────────────────────────

    [Fact]
    public void Bookmark_PutsTheSpellInThatTabsFirstEmptySlot_MovingASlotSpell()
    {
        var tabs = Tabs((0, L((0, 0, 5u))), (1, L((0, 0, 1u), (0, 2, 3u))));
        SpellDeckEdit book = SpellDeckModel.PlanDropOnTab(Book7, 1, false, rows: 2, cols: 3, tabs)!;
        Assert.Equal(SpellDeckEditKind.Place, book.Kind);
        Assert.Equal([new SpellDeckTabLayout(1, L((0, 0, 1u), (0, 1, 7u), (0, 2, 3u)))], book.Layouts);

        SpellDeckEdit move = SpellDeckModel.PlanDropOnTab(Slot(0, 0, 0, 5u), 1, false, 2, 3, tabs)!;
        Assert.Equal(SpellDeckEditKind.Move, move.Kind);
        Assert.Equal([new SpellDeckTabLayout(1, L((0, 0, 1u), (0, 1, 5u), (0, 2, 3u))), new SpellDeckTabLayout(0, SpellDeckLayout.Empty)],
            move.Layouts);

        SpellDeckEdit copy = SpellDeckModel.PlanDropOnTab(Slot(0, 0, 0, 5u), 1, true, 2, 3, tabs)!;
        Assert.Equal(SpellDeckEditKind.Copy, copy.Kind);
        Assert.Single(copy.Layouts);
    }

    [Fact]
    public void Bookmark_OfAFullTab_IsRefused_AndASlotOnItsOwnTab_DoesNothing_UnlessCopied()
    {
        var tabs = Tabs((0, L((0, 0, 5u), (0, 1, 6u))));
        Assert.Equal(SpellDeckEditKind.RefusedFull, SpellDeckModel.PlanDropOnTab(Book7, 0, false, 1, 2, tabs)!.Kind);
        Assert.False(SpellDeckModel.PlanDropOnTab(Book7, 0, false, 1, 2, tabs)!.Accepted);
        Assert.Equal(SpellDeckEditKind.None, SpellDeckModel.PlanDropOnTab(Slot(0, 0, 0, 5u), 0, false, 2, 2, tabs)!.Kind);
        SpellDeckEdit dup = SpellDeckModel.PlanDropOnTab(Slot(0, 0, 0, 5u), 0, true, 2, 2, tabs)!;
        Assert.Equal([new SpellDeckTabLayout(0, L((0, 0, 5u), (0, 1, 6u), (1, 0, 5u)))], dup.Layouts);
    }

    // ── Clear ────────────────────────────────────────────────────

    [Fact]
    public void Clear_EmptiesOnlyThatSlot_AndASpellbookSourcePlansNothing()
    {
        var tabs = Tabs((0, L((0, 0, 5u), (1, 0, 5u))));
        SpellDeckEdit clear = SpellDeckModel.PlanClear(Slot(0, 1, 0, 5u), tabs)!;
        Assert.Equal(SpellDeckEditKind.Clear, clear.Kind);
        Assert.Equal([new SpellDeckTabLayout(0, L((0, 0, 5u)))], clear.Layouts);
        Assert.Null(SpellDeckModel.PlanClear(Book7, tabs));
    }

    // ── Rows and columns every tab needs ─────────────────────────

    [Fact]
    public void MinColumnsAndRows_AreTheOccupiedExtentsOverAllTabs_AtLeastOne()
    {
        SpellDeckLayout[] all = [L((0, 3, 1u)), SpellDeckLayout.Empty, L((4, 1, 2u))];
        Assert.Equal(4, SpellDeckModel.MinColumns(all));
        Assert.Equal(5, SpellDeckModel.MinRows(all));
        Assert.Equal(1, SpellDeckModel.MinColumns([SpellDeckLayout.Empty]));
        Assert.Equal(1, SpellDeckModel.MinRows([]));
    }

    [Fact]
    public void CanRemoveRow_OnlyWhenNoTabHoldsASpellInTheLastRow()
    {
        SpellDeckLayout[] all = [L((0, 0, 1u)), L((2, 0, 2u))];
        Assert.False(SpellDeckModel.CanRemoveRow(3, all));
        Assert.True(SpellDeckModel.CanRemoveRow(4, all));
        Assert.False(SpellDeckModel.CanRemoveRow(1, [SpellDeckLayout.Empty]));
    }
}
