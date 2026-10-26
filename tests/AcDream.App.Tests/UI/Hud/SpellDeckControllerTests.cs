using AcDream.App.Spells;
using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckControllerTests
{
    private sealed class Game : ISpellDeckActions
    {
        public readonly List<uint>[] Tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        public readonly List<string> Calls = [];
        public ItemSpell? Item;
        public void CastSpell(uint id) => Calls.Add($"cast {id}");
        public void CastItemSpell(uint itemId) => Calls.Add($"item {itemId}");
        public void SetFavorite(int tab, int pos, uint id)
        {
            Calls.Add($"set {tab} {pos} {id}");
            Tabs[tab].Remove(id); Tabs[tab].Insert(Math.Min(pos, Tabs[tab].Count), id);
        }
        public void RemoveFavorite(int tab, uint id) { Calls.Add($"remove {tab} {id}"); Tabs[tab].Remove(id); }
        public void ExamineSpell(uint id) => Calls.Add($"examine {id}");
        public void ShowMessage(string text) => Calls.Add("msg " + text);
    }

    private static (SpellDeckController C, Game G, List<SpellDeckSettings> Saved) Make(bool clickCasts = false)
    {
        var g = new Game();
        g.Tabs[0].AddRange([10u, 11u, 12u]);
        g.Tabs[1].AddRange([20u]);
        var saved = new List<SpellDeckSettings>();
        var c = new SpellDeckController(
            g, favorites: tab => g.Tabs[tab], itemSpell: () => g.Item,
            capacity: () => 12, settings: SpellDeckSettings.Default, save: saved.Add,
            clickCasts: () => clickCasts, canAddRow: () => true);
        c.Tick();
        return (c, g, saved);
    }

    [Fact]
    public void Hotkeys_CastRowSlots_AndOutlineThem()
    {
        var (c, g, _) = Make();
        Assert.True(c.Handle(InputAction.UseSpellSlot_2));
        Assert.True(c.Handle(InputAction.UseSpellRow2Slot1));
        Assert.Equal(["cast 11", "cast 20"], g.Calls);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 1, 20u), c.Outline);
        Assert.True(c.Handle(InputAction.UseSpellRow3Slot5));   // empty slot: handled, nothing
        Assert.True(c.Handle(InputAction.UseSpellRow9Slot1));   // row not shown: handled, nothing
        Assert.Equal(2, g.Calls.Count);
        Assert.False(c.Handle(InputAction.MovementJump));
    }

    [Fact]
    public void TabKeys_CycleRow0()
    {
        var (c, _, saved) = Make();
        c.Handle(InputAction.CombatPrevSpellTab);
        Assert.Equal(7, c.Rows[0].Tab);
        c.Handle(InputAction.CombatNextSpellTab);
        Assert.Equal(0, c.Rows[0].Tab);
        c.Handle(InputAction.CombatLastSpellTab);
        Assert.Equal(7, c.Rows[0].Tab);
        Assert.Equal(3, saved.Count);
    }

    [Fact]
    public void ClickModes_CastCounts()
    {
        var (c, g, _) = Make(clickCasts: false);
        c.Click(0, 1); c.Click(0, 1); c.DoubleClick(0, 1);   // one physical double-click
        Assert.Equal(["cast 11"], g.Calls);

        var (c2, g2, _) = Make(clickCasts: true);
        c2.Click(0, 1); c2.Click(0, 1); c2.DoubleClick(0, 1);
        Assert.Equal(["cast 11", "cast 11"], g2.Calls);
    }

    [Fact]
    public void Drop_CrossRow_SetsThenRemoves_AndOutlinesTheDestination()
    {
        var (c, g, _) = Make();
        c.Click(0, 0);   // outline spell 10
        c.Drop(new SpellDeckDragSource(0, 10u), row: 1, slot: 0);
        Assert.Equal(["set 1 0 10", "remove 0 10"], g.Calls);
        Assert.Equal(SpellDeckOutline.ForSlot(2, 1, 10u), c.Outline);
    }

    [Fact]
    public void Drop_IntoFullRow_SendsNothing_AndSays()
    {
        var (c, g, _) = Make();
        for (uint s = 30; s < 41; s++) g.Tabs[1].Add(s);    // tab 1 now holds 12
        c.Drop(SpellDeckDragSource.FromSpellbook(99u), row: 1, slot: 0);
        Assert.Equal(["msg That spell bar row is full."], g.Calls);
    }

    [Fact]
    public void DropOutside_Removes()
    {
        var (c, g, _) = Make();
        c.DropOutside(new SpellDeckDragSource(0, 12u));
        Assert.Equal(["remove 0 12"], g.Calls);
    }

    [Fact]
    public void Rows_AddRemoveSetTab()
    {
        var (c, _, saved) = Make();
        c.AddRowBelow(c.Rows[0].Id);
        Assert.Equal(4, c.Rows.Count);
        Assert.Equal(3, c.Rows[1].Tab);          // lowest tab no row shows
        foreach (var row in c.Rows.ToArray().Skip(1)) c.RemoveRow(row.Id);
        c.RemoveRow(c.Rows[0].Id);               // last row stays
        Assert.Single(c.Rows);
        c.SetRowTab(c.Rows[0].Id, 5);
        Assert.Equal(5, c.Rows[0].Tab);
        Assert.Equal(5, saved.Count);
    }

    [Fact]
    public void OutlineFollowsExternalChanges_AndItemCasts()
    {
        var (c, g, _) = Make();
        c.Click(0, 2);
        g.Tabs[0].Remove(12u);
        c.Tick();
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 10u), c.Outline);
        g.Item = new ItemSpell(0x7000u, 55u);
        Assert.True(c.Handle(InputAction.CastItemSpell));
        Assert.Equal(SpellDeckOutline.Item, c.Outline);
        Assert.True(c.CanCastOutlined);
        c.CastOutlined();
        Assert.Equal(["item 28672", "item 28672"], g.Calls);
    }

    [Fact]
    public void SpellbookAdd_GoesToTheOutlinedRow()
    {
        var (c, g, _) = Make();
        c.Handle(InputAction.UseSpellRow2Slot1);   // outline row 2 (tab 1)
        g.Calls.Clear();
        c.AddFavoriteFromSpellbook(77u);
        Assert.Equal(["set 1 1 77"], g.Calls);
    }

    [Fact]
    public void Drop_CrossRow_OfANonOutlinedSpell_LeavesTheOutline()
    {
        var (c, g, _) = Make();
        c.Click(0, 1);   // outline spell 11
        c.Drop(new SpellDeckDragSource(0, 10u), row: 1, slot: 0);
        Assert.Equal(["set 1 0 10", "remove 0 10"], g.Calls);
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 11u), c.Outline);
    }

    [Fact]
    public void AddRowBelow_IsRefused_WhenNotAllowed_AndAtTenRows()
    {
        var g = new Game();
        bool allow = false;
        var saved = new List<SpellDeckSettings>();
        var blocked = new SpellDeckController(g, tab => g.Tabs[tab], () => null, () => 12,
            SpellDeckSettings.Default, saved.Add, () => false, () => allow);
        blocked.AddRowBelow(blocked.Rows[0].Id);
        Assert.Equal(3, blocked.Rows.Count);
        Assert.Empty(saved);

        var (c, _, _) = Make();
        for (int i = 0; i < 12; i++) c.AddRowBelow(c.Rows[0].Id);
        Assert.Equal(10, c.Rows.Count);
        c.AddRowBelow(9999);   // unknown row: nothing
        Assert.Equal(10, c.Rows.Count);
    }

    [Fact]
    public void SpellbookAdd_FullRowMessage_AndAlreadyPresentOnlyOutlines()
    {
        var (c, g, _) = Make();
        for (uint s = 30; s < 41; s++) g.Tabs[1].Add(s);
        c.Handle(InputAction.UseSpellRow2Slot1);   // outline row 2 (tab 1)
        g.Calls.Clear();
        c.AddFavoriteFromSpellbook(99u);
        Assert.Equal(["msg That spell bar row is full."], g.Calls);

        g.Calls.Clear();
        c.Click(0, 0);                              // outline row 1
        c.AddFavoriteFromSpellbook(12u);            // already in tab 0
        Assert.Empty(g.Calls);
        Assert.Equal(SpellDeckOutline.ForSlot(1, 0, 12u), c.Outline);
    }

    [Fact]
    public void NextSpell_ThenCastCurrent_CastsTheNextSpell()
    {
        var (c, g, _) = Make();
        c.Click(0, 0);
        c.Handle(InputAction.CombatNextSpell);
        c.Handle(InputAction.CombatCastCurrentSpell);
        Assert.Equal(["cast 11"], g.Calls);
    }

    [Fact]
    public void DoubleClickItem_CastsOnlyInDefaultMode()
    {
        var (c, g, _) = Make(clickCasts: false);
        g.Item = new ItemSpell(0x7000u, 55u);
        c.DoubleClickItem();
        Assert.Equal(["item 28672"], g.Calls);

        var (c2, g2, _) = Make(clickCasts: true);
        g2.Item = new ItemSpell(0x7000u, 55u);
        c2.DoubleClickItem();
        Assert.Empty(g2.Calls);
    }
}
