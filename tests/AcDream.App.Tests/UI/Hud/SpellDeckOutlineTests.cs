using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckOutlineTests
{
    private static SpellDeckOutlineContext Ctx(bool item, params (int Id, int Tab, uint[] Spells)[] rows) =>
        new(rows.Select(r => new SpellDeckRow(r.Id, r.Tab)).ToArray(),
            tab => rows.FirstOrDefault(r => r.Tab == tab).Spells ?? [],
            item);

    [Fact]
    public void Repair_PrefersRow0_ThenItem_ThenNone()
    {
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u), SpellDeckOutline.Repair(Ctx(true, (1, 0, [5, 6]))));
        Assert.Equal(SpellDeckOutline.Item, SpellDeckOutline.Repair(Ctx(true, (1, 0, []), (2, 3, [9]))));
        Assert.Equal(SpellDeckOutline.None, SpellDeckOutline.Repair(Ctx(false, (1, 0, []))));
    }

    [Fact]
    public void Validate_FollowsTheSpell_AndRepairsWhenItLeaves()
    {
        var o = SpellDeckOutline.ForSlot(1, 0, 6u);
        Assert.Equal(o, SpellDeckOutline.Validate(o, Ctx(false, (1, 0, [6, 5]))));      // reordered
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u),
            SpellDeckOutline.Validate(o, Ctx(false, (1, 0, [5]))));                     // removed
        Assert.Equal(SpellDeckOutline.None, SpellDeckOutline.Validate(o, Ctx(false)));   // row gone
    }

    [Fact]
    public void Validate_RowTabChanged_GoesToThatRowsFirstSpell()
    {
        var o = SpellDeckOutline.ForSlot(2, 1, 6u);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 4, 9u),
            SpellDeckOutline.Validate(o, Ctx(false, (1, 0, [5]), (2, 4, [9, 10]))));
    }

    [Fact]
    public void Validate_ItemGone_Repairs_NoneFilledWhenAvailable()
    {
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u),
            SpellDeckOutline.Validate(SpellDeckOutline.Item, Ctx(false, (1, 0, [5]))));
        Assert.Equal(SpellDeckOutline.Item,
            SpellDeckOutline.Validate(SpellDeckOutline.None, Ctx(true, (1, 0, []))));
    }

    [Fact]
    public void Next_WrapsAndVisitsTheItemBeforeRow0()
    {
        var ctx = Ctx(true, (1, 0, [5, 6]));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 6u), SpellDeckOutline.Next(SpellDeckOutline.ForSlot(1, 0, 5u), ctx));
        Assert.Equal(SpellDeckOutline.Item, SpellDeckOutline.Next(SpellDeckOutline.ForSlot(1, 0, 6u), ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u), SpellDeckOutline.Next(SpellDeckOutline.Item, ctx));
        Assert.Equal(SpellDeckOutline.Item, SpellDeckOutline.Prev(SpellDeckOutline.ForSlot(1, 0, 5u), ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 6u), SpellDeckOutline.Prev(SpellDeckOutline.Item, ctx));
    }

    [Fact]
    public void Next_InOtherRows_WrapsWithoutTheItem()
    {
        var ctx = Ctx(true, (1, 0, [5]), (2, 3, [7, 8]));
        Assert.Equal(SpellDeckOutline.ForSlot(2, 3, 7u), SpellDeckOutline.Next(SpellDeckOutline.ForSlot(2, 3, 8u), ctx));
    }

    [Fact]
    public void FromNone_NextIsFirst_PrevIsLast()
    {
        var ctx = Ctx(false, (1, 0, [5, 6, 7]));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 5u), SpellDeckOutline.Next(SpellDeckOutline.None, ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 7u), SpellDeckOutline.Prev(SpellDeckOutline.None, ctx));
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 7u), SpellDeckOutline.Last(SpellDeckOutline.Item, ctx));
    }

    [Fact]
    public void SameTabOnTwoRows_AreDistinct()
    {
        var ctx = Ctx(false, (1, 0, [5, 6]), (2, 0, [5, 6]));
        var row2 = SpellDeckOutline.ForSlot(2, 0, 5u);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 0, 6u), SpellDeckOutline.Next(row2, ctx));
        Assert.Equal(row2, SpellDeckOutline.Validate(row2, ctx));
    }
}
