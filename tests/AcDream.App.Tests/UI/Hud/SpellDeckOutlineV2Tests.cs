using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>v2 outline: <see cref="SpellDeckMark"/> = None | Item | Spell(tab, spellId) over the current tab's visible spells.</summary>
public sealed class SpellDeckOutlineV2Tests
{
    private static SpellDeckMarkContext Ctx(int tab, bool item, params uint[] visible) => new(tab, visible, item);

    [Fact]
    public void Repair_PrefersFirstVisibleSpell_ThenItem_ThenNone()
    {
        Assert.Equal(SpellDeckMark.Spell(2, 5u), SpellDeckMark.Repair(Ctx(2, true, 5, 6)));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Repair(Ctx(2, true)));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.Repair(Ctx(2, false)));
    }

    [Fact]
    public void Validate_ReorderedSpell_KeepsTheOutline()
    {
        var m = SpellDeckMark.Spell(0, 6u);
        Assert.Equal(m, SpellDeckMark.Validate(m, Ctx(0, false, 6, 5)));
        Assert.Equal(m, SpellDeckMark.Validate(m, Ctx(0, false, 5, 6)));
    }

    [Fact]
    public void Validate_SpellThatLeftTheTabOrVisibleRange_IsRepaired() =>
        Assert.Equal(SpellDeckMark.Spell(0, 5u), SpellDeckMark.Validate(SpellDeckMark.Spell(0, 6u), Ctx(0, false, 5)));

    [Fact]
    public void Validate_SpellOnAnotherTab_IsRepairedToTheCurrentTab() =>
        // Even when the new tab also holds the spell, the outline is the new tab's first spell.
        Assert.Equal(SpellDeckMark.Spell(3, 7u), SpellDeckMark.Validate(SpellDeckMark.Spell(0, 6u), Ctx(3, true, 7, 6)));

    [Fact]
    public void Validate_Item_KeptWhileThereIsOne_ElseRepaired()
    {
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Validate(SpellDeckMark.Item, Ctx(0, true, 5)));
        Assert.Equal(SpellDeckMark.Spell(0, 5u), SpellDeckMark.Validate(SpellDeckMark.Item, Ctx(0, false, 5)));
    }

    [Fact]
    public void Validate_None_RepairsWhenSomethingBecomesAvailable()
    {
        Assert.Equal(SpellDeckMark.Spell(1, 4u), SpellDeckMark.Validate(SpellDeckMark.None, Ctx(1, false, 4)));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.Validate(SpellDeckMark.None, Ctx(1, false)));
    }

    [Fact]
    public void ShowTab_MovesToThatTabsFirstSpell_EvenIfTheOldSpellIsThere()
    {
        Assert.Equal(SpellDeckMark.Spell(4, 8u), SpellDeckMark.ShowTab(Ctx(4, true, 8, 6)));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.ShowTab(Ctx(4, true)));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.ShowTab(Ctx(4, false)));
    }

    [Fact]
    public void Next_WalksItemThenSpells_AndWraps()
    {
        var ctx = Ctx(0, true, 5, 6);
        Assert.Equal(SpellDeckMark.Spell(0, 5u), SpellDeckMark.Next(SpellDeckMark.Item, ctx));
        Assert.Equal(SpellDeckMark.Spell(0, 6u), SpellDeckMark.Next(SpellDeckMark.Spell(0, 5u), ctx));
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Next(SpellDeckMark.Spell(0, 6u), ctx));
    }

    [Fact]
    public void Prev_WalksBackwards_AndWraps()
    {
        var ctx = Ctx(0, true, 5, 6);
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.Prev(SpellDeckMark.Spell(0, 5u), ctx));
        Assert.Equal(SpellDeckMark.Spell(0, 6u), SpellDeckMark.Prev(SpellDeckMark.Item, ctx));
    }

    [Fact]
    public void NextPrev_WithoutItem_WrapsOverSpellsOnly()
    {
        var ctx = Ctx(0, false, 5, 6);
        Assert.Equal(SpellDeckMark.Spell(0, 5u), SpellDeckMark.Next(SpellDeckMark.Spell(0, 6u), ctx));
        Assert.Equal(SpellDeckMark.Spell(0, 6u), SpellDeckMark.Prev(SpellDeckMark.Spell(0, 5u), ctx));
    }

    [Fact]
    public void NextPrev_FromNone_GoToFirstAndLast()
    {
        var ctx = Ctx(0, true, 5, 6);
        Assert.Equal(SpellDeckMark.Spell(0, 5u), SpellDeckMark.Next(SpellDeckMark.None, ctx));
        Assert.Equal(SpellDeckMark.Spell(0, 6u), SpellDeckMark.Prev(SpellDeckMark.None, ctx));
    }

    [Fact]
    public void NextPrev_FromAStaleSpell_GoToFirstAndLast()
    {
        var ctx = Ctx(1, false, 5, 6);
        Assert.Equal(SpellDeckMark.Spell(1, 5u), SpellDeckMark.Next(SpellDeckMark.Spell(0, 9u), ctx));
        Assert.Equal(SpellDeckMark.Spell(1, 6u), SpellDeckMark.Prev(SpellDeckMark.Spell(0, 9u), ctx));
    }

    [Fact]
    public void NextPrev_NothingToWalk_IsUnchanged() =>
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.Next(SpellDeckMark.None, Ctx(0, false)));

    [Fact]
    public void FirstLast_AreTheFirstAndLastVisibleSpell()
    {
        var ctx = Ctx(2, true, 5, 6, 7);
        Assert.Equal(SpellDeckMark.Spell(2, 5u), SpellDeckMark.First(ctx));
        Assert.Equal(SpellDeckMark.Spell(2, 7u), SpellDeckMark.Last(ctx));
    }

    [Fact]
    public void FirstLast_NoSpells_FallBackToItemThenNone()
    {
        Assert.Equal(SpellDeckMark.Item, SpellDeckMark.First(Ctx(2, true)));
        Assert.Equal(SpellDeckMark.None, SpellDeckMark.Last(Ctx(2, false)));
    }

    [Fact]
    public void Kinds_AreReportedAndSpellCarriesTabAndId()
    {
        Assert.Equal(SpellDeckMarkKind.None, SpellDeckMark.None.Kind);
        Assert.Equal(SpellDeckMarkKind.Item, SpellDeckMark.Item.Kind);
        var s = SpellDeckMark.Spell(11, 42u);
        Assert.Equal((SpellDeckMarkKind.Spell, 11, 42u), (s.Kind, s.Tab, s.SpellId));
    }
}
