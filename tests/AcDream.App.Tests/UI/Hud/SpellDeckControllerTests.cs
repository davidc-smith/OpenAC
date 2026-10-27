using AcDream.App.Spells;
using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Input;

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

    private sealed class Rig
    {
        public SpellDeckController C = null!;
        public readonly Game G = new();
        public readonly List<SpellDeckSettings> Saved = [];
        public int PerRow = 4;
        public bool ClickCasts;
        public bool CanAddRow = true;
        public double Now;
    }

    /// <summary>4 slots per row, 3 rows (capacity 12). Tab 0 holds 10-15 (rows 0 and 1), tab 1 holds 20.</summary>
    private static Rig Make(bool clickCasts = false, SpellDeckSettings? settings = null)
    {
        var r = new Rig { ClickCasts = clickCasts };
        r.G.Tabs[0].AddRange([10u, 11u, 12u, 13u, 14u, 15u]);
        r.G.Tabs[1].AddRange([20u]);
        r.C = new SpellDeckController(
            r.G, favorites: tab => r.G.Tabs[tab], itemSpell: () => r.G.Item,
            capacity: () => r.PerRow, settings: settings ?? SpellDeckSettings.Default,
            save: r.Saved.Add,
            clickCasts: () => r.ClickCasts, canAddRow: () => r.CanAddRow, seconds: () => r.Now);
        r.C.Tick();
        return r;
    }

    private static SpellDeckSettings WithLocal(params uint[][] local) =>
        SpellDeckSettings.Default with { LocalTabs = [.. local.Select(l => (IReadOnlyList<uint>)l)] };

    [Fact]
    public void TabSwitches_Coalesce_IntoOneSave_AfterTheDelay()
    {
        var r = Make();
        for (int i = 0; i < 10; i++) { r.Now += 0.05; r.C.NextTab(); }
        r.C.Tick();
        Assert.Empty(r.Saved);                       // still switching
        r.Now += SpellDeckController.TabSaveDelaySeconds;
        r.C.Tick();
        Assert.Single(r.Saved);
        Assert.Equal(r.C.Tab, r.Saved[0].Tab);
        Assert.Equal(2, r.C.Tab);                    // 10 steps round 8 tabs
        r.Now += 5; r.C.Tick();
        Assert.Single(r.Saved);                      // nothing pending any more
    }

    [Fact]
    public void FlushPendingSave_WritesTheTab_AndLoadDropsAnyPendingOne()
    {
        var r = Make();
        r.C.NextTab();
        r.C.NextTab();
        Assert.Empty(r.Saved);
        r.C.FlushPendingSave();
        Assert.Single(r.Saved);
        Assert.Equal(2, r.Saved[0].Tab);
        r.C.FlushPendingSave();                      // nothing pending: no second write
        Assert.Single(r.Saved);

        r.C.NextTab();
        r.C.Load(SpellDeckSettings.Default with { Tab = 5 });   // never saves, not even later
        r.Now += 5; r.C.Tick();
        Assert.Single(r.Saved);
        Assert.Equal(5, r.C.Tab);
    }

    // ── Tab, rows, slices ─────────────────────────────────────────

    [Fact]
    public void Starts_OnTheSettingsTab_WithItsRowCount_AndRowsAreSlices()
    {
        var r = Make();
        Assert.Equal(0, r.C.Tab);
        Assert.Equal(3, r.C.RowCount);
        Assert.Equal(4, r.C.PerRow);
        Assert.Equal(8, r.C.TabCount);
        Assert.Equal([10u, 11u, 12u, 13u], r.C.RowSpells(0));
        Assert.Equal([14u, 15u], r.C.RowSpells(1));
        Assert.Empty(r.C.RowSpells(2));
        Assert.Empty(r.C.RowSpells(3));   // not a shown row
        Assert.Equal(SpellDeckMark.Spell(0, 10u), r.C.Outline);
    }

    [Fact]
    public void Visible_IsTheFirstRowsTimesPerRow_AndPerRowFollowsTheCapacity()
    {
        var r = Make();
        r.PerRow = 1;
        Assert.Equal([10u, 11u, 12u], r.C.Visible);
        Assert.Equal([12u], r.C.RowSpells(2));
        r.PerRow = 0;                    // never below one slot
        Assert.Equal(1, r.C.PerRow);
    }

    [Fact]
    public void ShowTab_ChangesTheTab_OutlinesItsFirstSpell_AndSavesOnce()
    {
        var r = Make();
        r.C.ShowTab(1);
        Assert.Equal(1, r.C.Tab);
        Assert.Equal(SpellDeckMark.Spell(1, 20u), r.C.Outline);
        Assert.Empty(r.Saved);           // saved later, from Tick
        r.Now += 1; r.C.Tick();
        Assert.Single(r.Saved);
        Assert.Equal(1, r.Saved[^1].Tab);
        r.C.ShowTab(1);                  // already shown: nothing
        r.C.ShowTab(8);                  // no such tab: nothing
        r.C.ShowTab(-1);
        r.Now += 1; r.C.Tick();
        Assert.Single(r.Saved);
        r.C.ShowTab(2);                  // an empty tab: the item, else nothing
        Assert.Equal(SpellDeckMark.None, r.C.Outline);
    }

    [Fact]
    public void TabKeys_WrapOverTheTabsThatExist()
    {
        var r = Make(settings: WithLocal([90u]));
        Assert.Equal(9, r.C.TabCount);
        Assert.True(r.C.Handle(InputAction.CombatPrevSpellTab));
        Assert.Equal(8, r.C.Tab);
        Assert.Equal(SpellDeckMark.Spell(8, 90u), r.C.Outline);
        Assert.True(r.C.Handle(InputAction.CombatNextSpellTab));
        Assert.Equal(0, r.C.Tab);
        Assert.True(r.C.Handle(InputAction.CombatLastSpellTab));
        Assert.Equal(8, r.C.Tab);
        Assert.True(r.C.Handle(InputAction.CombatFirstSpellTab));
        Assert.Equal(0, r.C.Tab);
        r.C.NextTab(); r.C.PrevTab(); r.C.LastTab(); r.C.FirstTab();
        Assert.Equal(0, r.C.Tab);
        Assert.Empty(r.Saved);
        r.Now += 1; r.C.Tick();
        Assert.Single(r.Saved);
    }

    // ── Hotkeys ───────────────────────────────────────────────────

    [Fact]
    public void Hotkey_RowRSlotN_CastsSpellRTimesNPlusN_AndOutlinesIt()
    {
        var r = Make();
        Assert.True(r.C.Handle(InputAction.UseSpellSlot_2));       // row 0 slot 1
        Assert.True(r.C.Handle(InputAction.UseSpellRow2Slot1));    // row 1 slot 0: spell 4
        Assert.Equal(["cast 11", "cast 14"], r.G.Calls);
        Assert.Equal(SpellDeckMark.Spell(0, 14u), r.C.Outline);
        Assert.Empty(r.Saved);
    }

    [Fact]
    public void Hotkey_EmptySlot_HiddenRow_OrSlotPastTheRow_IsHandledAndDoesNothing()
    {
        var r = Make();
        Assert.True(r.C.Handle(InputAction.UseSpellRow2Slot3));    // row 1 slot 2: past the tab's end
        Assert.True(r.C.Handle(InputAction.UseSpellRow9Slot1));    // row 8: not shown
        Assert.True(r.C.Handle(InputAction.UseSpellSlot_5));       // row 0 slot 4: past the row's 4 slots (not row 1's first)
        Assert.Empty(r.G.Calls);
        Assert.Equal(SpellDeckMark.Spell(0, 10u), r.C.Outline);
        Assert.False(r.C.Handle(InputAction.MovementJump));
    }

    [Fact]
    public void NextSpell_ThenCastCurrent_WalksTheVisibleSpells()
    {
        var r = Make();
        r.C.Handle(InputAction.CombatNextSpell);
        r.C.Handle(InputAction.CombatCastCurrentSpell);
        r.C.Handle(InputAction.CombatLastSpell);
        r.C.Handle(InputAction.CombatCastCurrentSpell);
        r.C.Handle(InputAction.CombatPrevSpell);
        r.C.Handle(InputAction.CombatFirstSpell);
        r.C.Handle(InputAction.CombatCastCurrentSpell);
        Assert.Equal(["cast 11", "cast 15", "cast 10"], r.G.Calls);
    }

    // ── Pointer ───────────────────────────────────────────────────

    [Fact]
    public void ClickModes_CastCounts()
    {
        var r = Make(clickCasts: false);
        r.C.Click(1, 1); r.C.Click(1, 1); r.C.DoubleClick(1, 1);   // one physical double-click on spell 5
        Assert.Equal(["cast 15"], r.G.Calls);
        Assert.Equal(SpellDeckMark.Spell(0, 15u), r.C.Outline);

        var r2 = Make(clickCasts: true);
        r2.C.Click(1, 1); r2.C.Click(1, 1); r2.C.DoubleClick(1, 1);
        Assert.Equal(["cast 15", "cast 15"], r2.G.Calls);

        r2.C.Click(1, 3); r2.C.Click(5, 0); r2.C.Click(0, 9);   // empty, hidden row, past the row
        Assert.Equal(2, r2.G.Calls.Count);
    }

    [Fact]
    public void Item_ClickAndHotkey_OutlineAndCast_ByMode()
    {
        var r = Make(clickCasts: false);
        r.G.Item = new ItemSpell(0x7000u, 55u);
        r.C.ClickItem();
        Assert.Equal(SpellDeckMark.Item, r.C.Outline);
        Assert.Empty(r.G.Calls);
        r.C.DoubleClickItem();
        Assert.True(r.C.Handle(InputAction.CastItemSpell));
        Assert.True(r.C.CanCastOutlined);
        Assert.Equal(55u, r.C.OutlinedSpellId);
        r.C.CastOutlined();
        Assert.Equal(["item 28672", "item 28672", "item 28672"], r.G.Calls);

        var r2 = Make(clickCasts: true);
        r2.G.Item = new ItemSpell(0x7000u, 55u);
        r2.C.DoubleClickItem();
        Assert.Empty(r2.G.Calls);
    }

    [Fact]
    public void CommandDeck_OutlinedSpell_CastsAndClearsWithNothing()
    {
        var r = Make();
        Assert.Equal(10u, r.C.OutlinedSpellId);
        r.C.CastOutlined();
        Assert.Equal(["cast 10"], r.G.Calls);
        r.C.ShowTab(2);
        Assert.False(r.C.CanCastOutlined);
        Assert.Null(r.C.OutlinedSpellId);
        r.C.CastOutlined();
        Assert.Single(r.G.Calls);
    }

    [Fact]
    public void Outline_FollowsAReorder_AndRepairsWhenItsSpellLeaves()
    {
        var r = Make();
        r.C.Click(1, 0);                          // spell 14
        r.G.Tabs[0].Remove(14u); r.G.Tabs[0].Insert(0, 14u);
        r.C.Tick();
        Assert.Equal(SpellDeckMark.Spell(0, 14u), r.C.Outline);
        r.G.Tabs[0].Remove(14u);
        r.C.Tick();
        Assert.Equal(SpellDeckMark.Spell(0, 10u), r.C.Outline);
    }

    // ── Drops ─────────────────────────────────────────────────────

    [Fact]
    public void DropOnSlot_Reorder_AtRowTimesPerRowPlusSlot_KeepsTheOutline()
    {
        var r = Make();
        Assert.True(r.C.CanDropOnSlot(new SpellDeckDragSource(0, 10u), 1, 1));
        r.C.DropOnSlot(new SpellDeckDragSource(0, 10u), row: 1, slot: 1);   // position 5
        Assert.Equal(["set 0 5 10"], r.G.Calls);
        Assert.Equal(SpellDeckMark.Spell(0, 10u), r.C.Outline);
        Assert.Empty(r.Saved);
    }

    [Fact]
    public void DropOnSlot_InsertFromSpellbook_AndRefusedWhenTheTabIsFull()
    {
        var r = Make();
        r.C.DropOnSlot(SpellDeckDragSource.FromSpellbook(77u), row: 2, slot: 3);   // position 11, clamped to 6
        Assert.Equal(["set 0 6 77"], r.G.Calls);
        for (uint s = 30; s < 35; s++) r.G.Tabs[0].Add(s);                      // tab 0 holds 12 = 3·4
        r.G.Calls.Clear();
        Assert.False(r.C.CanDropOnSlot(SpellDeckDragSource.FromSpellbook(99u), 0, 0));
        r.C.DropOnSlot(SpellDeckDragSource.FromSpellbook(99u), row: 0, slot: 0);
        Assert.Equal(["msg That spell tab is full."], r.G.Calls);
        r.G.Calls.Clear();
        r.C.DropOnSlot(new SpellDeckDragSource(0, 30u), row: 0, slot: 0);        // a reorder is always allowed
        Assert.Equal(["set 0 0 30"], r.G.Calls);
        Assert.False(r.C.CanDropOnSlot(new SpellDeckDragSource(0, 30u), 3, 0));
        r.C.DropOnSlot(new SpellDeckDragSource(0, 30u), row: 3, slot: 0);        // not a shown row: nothing
        r.C.DropOnSlot(new SpellDeckDragSource(0, 30u), row: 0, slot: 4);        // past the row: nothing
        Assert.Single(r.G.Calls);
    }

    [Fact]
    public void DropOnSlot_OfASlotFromAnotherTab_MovesIt()
    {
        // The shown tab changed mid-drag (a tab key): the lifted spell leaves its own tab.
        var r = Make();
        r.C.ShowTab(1);
        r.C.DropOnSlot(new SpellDeckDragSource(0, 10u), row: 0, slot: 0);
        Assert.Equal(["set 1 0 10", "remove 0 10"], r.G.Calls);
    }

    [Fact]
    public void DropOnTab_MovesASlotSpell_AddsASpellbookSpell_AndTheOutlineDoesNotFollow()
    {
        var r = Make();
        r.C.Click(0, 1);                                              // outline 11
        Assert.True(r.C.CanDropOnTab(new SpellDeckDragSource(0, 11u), 1));
        r.C.DropOnTab(new SpellDeckDragSource(0, 11u), 1);
        Assert.Equal(["set 1 1 11", "remove 0 11"], r.G.Calls);
        Assert.Equal(SpellDeckMark.Spell(0, 10u), r.C.Outline);        // repaired, not followed
        Assert.Equal(0, r.C.Tab);
        r.G.Calls.Clear();
        r.C.DropOnTab(SpellDeckDragSource.FromSpellbook(77u), 2);
        Assert.Equal(["set 2 0 77"], r.G.Calls);
        Assert.Empty(r.Saved);
    }

    [Fact]
    public void DropOnTab_OwnTabOrAlreadyThere_DoesNothing_AndAFullTabIsRefused()
    {
        var r = Make();
        r.C.DropOnTab(new SpellDeckDragSource(0, 10u), 0);
        r.C.DropOnTab(SpellDeckDragSource.FromSpellbook(20u), 1);
        Assert.False(r.C.CanDropOnTab(SpellDeckDragSource.FromSpellbook(20u), 8));
        r.C.DropOnTab(SpellDeckDragSource.FromSpellbook(20u), 8);      // no such tab
        Assert.Empty(r.G.Calls);
        for (uint s = 30; s < 41; s++) r.G.Tabs[1].Add(s);           // tab 1 holds 12
        Assert.False(r.C.CanDropOnTab(new SpellDeckDragSource(0, 10u), 1));
        r.C.DropOnTab(new SpellDeckDragSource(0, 10u), 1);
        Assert.Equal(["msg That spell tab is full."], r.G.Calls);
    }

    [Fact]
    public void DropOutside_RemovesASlotSpell_AndIgnoresASpellbookOne()
    {
        var r = Make();
        r.C.DropOutside(new SpellDeckDragSource(0, 12u));
        r.C.DropOutside(SpellDeckDragSource.FromSpellbook(12u));
        Assert.Equal(["remove 0 12"], r.G.Calls);
    }

    [Fact]
    public void RemoveFromTab_AndExamine_ActOnlyWhileTheTabShowsTheSpell()
    {
        var r = Make();
        r.C.Examine(0, 11u);
        r.C.Examine(1, 20u);           // tab 1 is not shown
        r.C.RemoveFromTab(0, 99u);     // not in the tab
        r.C.RemoveFromTab(0, 11u);
        Assert.Equal(["examine 11", "remove 0 11"], r.G.Calls);
    }

    [Fact]
    public void SpellbookAdd_GoesToTheEndOfTheCurrentTab_FullSays_PresentOnlyOutlines()
    {
        var r = Make();
        r.C.ShowTab(1);
        r.C.AddFavoriteFromSpellbook(77u);
        Assert.Equal(["set 1 1 77"], r.G.Calls);

        r.G.Calls.Clear();
        r.C.Click(0, 1);                                       // outline 77
        r.C.AddFavoriteFromSpellbook(20u);                     // already there: outline it
        Assert.Empty(r.G.Calls);
        Assert.Equal(SpellDeckMark.Spell(1, 20u), r.C.Outline);

        for (uint s = 30; s < 40; s++) r.G.Tabs[1].Add(s);   // tab 1 holds 12
        r.C.AddFavoriteFromSpellbook(99u);
        Assert.Equal(["msg That spell tab is full."], r.G.Calls);
    }

    // ── Local tabs ────────────────────────────────────────────────

    [Fact]
    public void LocalTabEdits_NeverCallTheServer_AndSaveTheLocalListOncePerChange()
    {
        var r = Make(settings: WithLocal([90u, 91u]) with { Tab = 8 });
        Assert.Equal(SpellDeckMark.Spell(8, 90u), r.C.Outline);
        r.C.DropOnSlot(SpellDeckDragSource.FromSpellbook(92u), row: 0, slot: 0);
        r.C.DropOnSlot(new SpellDeckDragSource(8, 92u), row: 0, slot: 2);
        r.C.DropOutside(new SpellDeckDragSource(8, 90u));
        r.C.AddFavoriteFromSpellbook(93u);
        r.C.RemoveFromTab(8, 93u);
        Assert.Empty(r.G.Calls);
        Assert.Equal(5, r.Saved.Count);
        Assert.Equal([91u, 92u], r.Saved[^1].LocalTabs[0]);
        Assert.Equal([91u, 92u], r.C.Favorites(8));
    }

    [Fact]
    public void MoveBetweenLocalAndServerTabs_SendsOnlyTheServerHalf_AndSavesOnce()
    {
        var r = Make(settings: WithLocal([90u], []));
        r.C.DropOnTab(new SpellDeckDragSource(0, 10u), 8);        // server → local
        Assert.Equal(["remove 0 10"], r.G.Calls);
        Assert.Single(r.Saved);
        Assert.Equal([90u, 10u], r.Saved[^1].LocalTabs[0]);

        r.G.Calls.Clear();
        r.C.ShowTab(8);
        r.C.DropOnTab(new SpellDeckDragSource(8, 90u), 1);        // local → server
        Assert.Equal(["set 1 1 90"], r.G.Calls);
        Assert.Equal(2, r.Saved.Count);                           // the drop's save carries the pending tab too
        Assert.Equal([10u], r.Saved[^1].LocalTabs[0]);

        r.C.DropOnTab(new SpellDeckDragSource(8, 10u), 9);        // local → local: one save for both halves
        Assert.Equal(3, r.Saved.Count);
        Assert.Empty(r.Saved[^1].LocalTabs[0]);
        Assert.Equal([10u], r.Saved[^1].LocalTabs[1]);
    }

    [Fact]
    public void AddLocalTab_ShowsTheNewTab_UpToSixteen_SavingOnceEach()
    {
        var r = Make();
        Assert.True(r.C.CanAddLocalTab);
        r.C.AddLocalTab();
        Assert.Equal(9, r.C.TabCount);
        Assert.Equal(8, r.C.Tab);
        Assert.Single(r.Saved);
        Assert.Equal((8, 1), (r.Saved[^1].Tab, r.Saved[^1].LocalTabs.Count));
        for (int i = 0; i < 9; i++) r.C.AddLocalTab();
        Assert.Equal(16, r.C.TabCount);
        Assert.Equal(15, r.C.Tab);
        Assert.False(r.C.CanAddLocalTab);
        Assert.Equal(8, r.Saved.Count);
        Assert.Empty(r.G.Calls);
    }

    [Fact]
    public void RemoveLastLocalTab_ShowsThePreviousTabWhenItWasShown()
    {
        var r = Make(settings: WithLocal([90u], [95u]) with { Tab = 9 });
        Assert.True(r.C.CanRemoveLastLocalTab);
        r.C.RemoveLastLocalTab();
        Assert.Equal(9, r.C.TabCount);
        Assert.Equal(8, r.C.Tab);
        Assert.Equal(SpellDeckMark.Spell(8, 90u), r.C.Outline);
        Assert.Single(r.Saved);
        Assert.Equal((8, 1), (r.Saved[^1].Tab, r.Saved[^1].LocalTabs.Count));

        r.C.ShowTab(0);
        r.C.RemoveLastLocalTab();                                 // not shown: the tab stays
        Assert.Equal(0, r.C.Tab);
        Assert.Equal(8, r.C.TabCount);
        Assert.False(r.C.CanRemoveLastLocalTab);
        r.C.RemoveLastLocalTab();                                 // server tabs cannot be removed
        Assert.Equal(8, r.C.TabCount);
        Assert.Equal(2, r.Saved.Count);
        Assert.Empty(r.G.Calls);
    }

    // ── Rows ──────────────────────────────────────────────────────

    [Fact]
    public void AddRow_UpToTenAndOnlyWhenAllowed()
    {
        var r = Make();
        r.CanAddRow = false;
        Assert.False(r.C.CanAddRow());
        r.C.AddRow();
        Assert.Equal(3, r.C.RowCount);
        Assert.Empty(r.Saved);
        r.CanAddRow = true;
        for (int i = 0; i < 12; i++) r.C.AddRow();
        Assert.Equal(10, r.C.RowCount);
        Assert.Equal(7, r.Saved.Count);
        Assert.Equal(10, r.Saved[^1].RowCount);
    }

    [Fact]
    public void RemoveRow_FollowsTheModelRule_OverAllTabsIncludingLocal()
    {
        int max = SpellDeckGeometry.MaxCapacity;
        var r = Make(settings: WithLocal([.. Enumerable.Range(1000, max + 1).Select(i => (uint)i)]));
        Assert.True(r.C.CanRemoveRow);            // 3 rows → 2 still hold max + 1
        r.C.RemoveRow();
        Assert.Equal(2, r.C.RowCount);
        Assert.False(r.C.CanRemoveRow);           // 1 row cannot hold the local tab
        r.C.RemoveRow();
        Assert.Equal(2, r.C.RowCount);
        Assert.Single(r.Saved);
        Assert.Equal(2, r.Saved[^1].RowCount);
    }

    [Fact]
    public void MinSlotsPerRow_IsOverAllTabs()
    {
        var r = Make(settings: WithLocal([.. Enumerable.Range(1000, 10).Select(i => (uint)i)]));
        Assert.Equal(4, r.C.MinSlotsPerRow);      // ceil(10 / 3): the local tab, not the shown one
        r.C.AddRow();
        Assert.Equal(3, r.C.MinSlotsPerRow);      // ceil(10 / 4)
    }

    // ── Settings ──────────────────────────────────────────────────

    [Fact]
    public void Load_ReplacesTabRowsAndLocalTabs_WithoutSaving()
    {
        var r = Make();
        r.C.Load(WithLocal([90u]) with { Tab = 8, RowCount = 5, Attached = false, DetachedRows = 4 });
        Assert.Equal((8, 5, 9, false, (int?)4), (r.C.Tab, r.C.RowCount, r.C.TabCount, r.C.Attached, r.C.DetachedRows));
        Assert.Equal(SpellDeckMark.Spell(8, 90u), r.C.Outline);
        Assert.Empty(r.Saved);
        r.C.Load(SpellDeckSettings.Default with { Tab = 12 });   // a local tab that is not there: tab 0
        Assert.Equal((0, 8), (r.C.Tab, r.C.TabCount));
    }

    [Fact]
    public void AttachAndDetachedRows_SaveTheWholeSettings()
    {
        var r = Make(settings: WithLocal([90u]) with { Tab = 1 });
        r.C.SetAttached(false);
        r.C.SetAttached(false);
        r.C.SetDetachedRows(4);
        r.C.SetDetachedRows(4);
        Assert.Equal(2, r.Saved.Count);
        SpellDeckSettings s = r.Saved[^1];
        Assert.Equal((1, 3, false, (int?)4), (s.Tab, s.RowCount, s.Attached, s.DetachedRows));
        Assert.Equal([90u], Assert.Single(s.LocalTabs));
    }
}
