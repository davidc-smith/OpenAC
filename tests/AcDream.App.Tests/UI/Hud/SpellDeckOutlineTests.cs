using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The outline: <see cref="SpellDeckMark"/> = None | Item | Slot(tab, row, col) over the current tab's
/// filled shown slots in reading order.</summary>
public sealed class SpellDeckOutlineTests
{
    private static SpellDeckMarkContext Ctx(int tab, bool item, params (int Row, int Col)[] filled) =>
        new(tab, [.. filled.Select(f => new SpellDeckSlot(f.Row, f.Col))], item);

    private static SpellDeckMark S(int tab, int row, int col) => SpellDeckMark.Slot(tab, row, col);

    [Fact]
    public void Repair_PrefersTheFirstFilledSlot_ThenItem_ThenNone()
    {
        Assert.Equal(S(2, 1, 3), SpellDeckMark.Repair(Ctx(2, true, (1, 3), (2, 0))));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Repair(Ctx(2, true)));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.Repair(Ctx(2, false)));
    }

    [Fact]
    public void Validate_AFilledSlotKeepsTheOutline_ACleared_OrNotShownSlotIsRepaired()
    {
        Assert.Equal(S(0, 1, 2), SpellDeckMark.Validate(S(0, 1, 2), Ctx(0, false, (0, 0), (1, 2))));
        Assert.Equal(S(0, 0, 0), SpellDeckMark.Validate(S(0, 1, 2), Ctx(0, false, (0, 0))));
    }

    [Fact]
    public void Validate_ASlotOnAnotherTab_IsRepairedToTheCurrentTab() =>
        // Even when the new tab has the same slot filled, the outline moves to the new tab's first slot.
        Assert.Equal(S(3, 0, 1), SpellDeckMark.Validate(S(0, 0, 4), Ctx(3, true, (0, 1), (0, 4))));

    [Fact]
    public void Validate_Item_KeptWhileThereIsOne_ElseRepaired()
    {
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Validate(SpellDeckMark.Item, Ctx(0, true, (0, 1))));
        Assert.Equal(S(0, 0, 1), SpellDeckMark.Validate(SpellDeckMark.Item, Ctx(0, false, (0, 1))));
        Assert.Equal(S(0, 0, 1), SpellDeckMark.Validate(SpellDeckMark.None, Ctx(0, false, (0, 1))));
    }

    [Fact]
    public void ShowTab_IsTheNewTabsFirstFilledSlot_ElseItem_ElseNone()
    {
        Assert.Equal(S(4, 2, 0), SpellDeckMark.ShowTab(Ctx(4, true, (2, 0))));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.ShowTab(Ctx(4, true)));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.ShowTab(Ctx(4, false)));
    }

    [Fact]
    public void NextPrev_WalkTheFilledSlotsInReadingOrder_ItemFirst_Wrapping_SkippingGaps()
    {
        var ctx = Ctx(1, true, (0, 2), (1, 0), (1, 5));
        Assert.Equal(S(1, 0, 2), SpellDeckMark.Next(SpellDeckMark.Item, ctx));
        Assert.Equal(S(1, 1, 0), SpellDeckMark.Next(S(1, 0, 2), ctx));
        Assert.Equal(S(1, 1, 5), SpellDeckMark.Next(S(1, 1, 0), ctx));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Next(S(1, 1, 5), ctx));
        Assert.Equal(S(1, 1, 5), SpellDeckMark.Prev(SpellDeckMark.Item, ctx));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Prev(S(1, 0, 2), ctx));

        var noItem = Ctx(1, false, (0, 2), (1, 0));
        Assert.Equal(S(1, 0, 2), SpellDeckMark.Next(S(1, 1, 0), noItem));
        Assert.Equal(S(1, 1, 0), SpellDeckMark.Prev(S(1, 0, 2), noItem));
    }

    [Fact]
    public void NextPrev_FromNone_GoToTheEdges_AndWithNothingStay()
    {
        var ctx = Ctx(0, true, (0, 0), (0, 3));
        Assert.Equal(S(0, 0, 0), SpellDeckMark.Next(SpellDeckMark.None, ctx));
        Assert.Equal(S(0, 0, 3), SpellDeckMark.Prev(SpellDeckMark.None, ctx));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.Next(SpellDeckMark.None, Ctx(0, false)));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Next(SpellDeckMark.Item, Ctx(0, true)));
    }

    [Fact]
    public void FirstLast_AreTheEdgeFilledSlots_ElseItem_ElseNone()
    {
        var ctx = Ctx(0, true, (0, 1), (2, 2));
        Assert.Equal(S(0, 0, 1), SpellDeckMark.First(ctx));
        Assert.Equal(S(0, 2, 2), SpellDeckMark.Last(ctx));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.First(Ctx(0, true)));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.Last(Ctx(0, false)));
    }
}
