using AcDream.App.Spells;
using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Input;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckControllerTests
{
    /// <summary>The server's favourite lists, applying each call with ACE's rules. <see cref="OnChange"/> runs
    /// inside every change, as the spellbook's changed event does (the runtime applies favourites at once).</summary>
    private sealed class Game : ISpellDeckActions
    {
        public readonly List<uint>[] Tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        public readonly List<string> Calls = [];
        public ItemSpell? Item;
        public Action? OnChange;
        public void CastSpell(uint id) => Calls.Add($"cast {id}");
        public void CastItemSpell(uint itemId) => Calls.Add($"item {itemId}");
        public void SetFavorite(int tab, int pos, uint id)
        {
            Calls.Add($"set {tab} {pos} {id}");
            Tabs[tab].Remove(id); Tabs[tab].Insert(Math.Min(pos, Tabs[tab].Count), id);
            OnChange?.Invoke();
        }
        public void RemoveFavorite(int tab, uint id) { Calls.Add($"remove {tab} {id}"); Tabs[tab].Remove(id); OnChange?.Invoke(); }
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

    private static SpellDeckLayout L(params (int Row, int Col, uint Spell)[] slots) => SpellDeckLayout.FromSlots(slots);

    /// <summary>Tab 0, 4 slots × 3 rows: 10 11 _ 12 / _ 13 _ _ / 10 _ _ _ (a gap and a duplicate). Tab 1: 20.</summary>
    private static readonly SpellDeckLayout Tab0 = L((0, 0, 10u), (0, 1, 11u), (0, 3, 12u), (1, 1, 13u), (2, 0, 10u));

    private static SpellDeckSettings Saved(Dictionary<int, SpellDeckLayout>? layouts = null, params uint[][] local) =>
        SpellDeckSettings.Default with
        {
            LocalTabs = [.. local.Select(l => (IReadOnlyList<uint>)l)],
            Layouts = layouts ?? new() { [0] = Tab0, [1] = L((0, 0, 20u)) },
        };

    /// <summary>The server holds each saved server layout's distinct spells (so the first reconcile changes nothing).</summary>
    private static Rig Make(bool clickCasts = false, SpellDeckSettings? settings = null)
    {
        var r = new Rig { ClickCasts = clickCasts };
        settings ??= Saved();
        foreach ((int tab, SpellDeckLayout layout) in settings.Layouts ?? new Dictionary<int, SpellDeckLayout>())
            if (tab < 8) r.G.Tabs[tab].AddRange(layout.Distinct);
        r.C = new SpellDeckController(
            r.G, favorites: tab => r.G.Tabs[tab], itemSpell: () => r.G.Item,
            capacity: () => r.PerRow, settings: settings,
            save: r.Saved.Add,
            clickCasts: () => r.ClickCasts, canAddRow: () => r.CanAddRow, seconds: () => r.Now);
        r.G.OnChange = r.C.ServerFavoritesChanged;
        r.C.Tick();
        return r;
    }

    private static SpellDeckDragSource Slot(int tab, int row, int col, uint spell) => new(tab, row, col, spell);
    private static SpellDeckDragSource Book(uint spell) => SpellDeckDragSource.FromSpellbook(spell);
    private static SpellDeckMark M(int tab, int row, int col) => SpellDeckMark.Slot(tab, row, col);

    /// <summary>The server always ends equal to the tab's distinct spells in reading order.</summary>
    private static void AssertInSync(Rig r, int tab = 0) => Assert.Equal(r.C.Favorites(tab), r.G.Tabs[tab]);

    // ── Saving and loading ────────────────────────────────────────

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
        r.C.Load(Saved() with { Tab = 5 });          // nothing to reconcile: never saves, not even later
        r.Now += 5; r.C.Tick();
        Assert.Single(r.Saved);
        Assert.Equal(5, r.C.Tab);
    }

    [Fact]
    public void Starts_OnTheSavedLayout_WithGapsAndDuplicates_WithoutSavingOrSending()
    {
        var r = Make();
        Assert.Equal((0, 3, 4, 8), (r.C.Tab, r.C.RowCount, r.C.PerRow, r.C.TabCount));
        Assert.Equal(Tab0, r.C.Layout);
        Assert.Equal(11u, r.C.SlotSpell(0, 1));
        Assert.Null(r.C.SlotSpell(0, 2));            // a gap
        Assert.Equal(10u, r.C.SlotSpell(2, 0));      // a duplicate
        Assert.Null(r.C.SlotSpell(3, 0));            // not a shown row
        Assert.Equal([10u, 11u, 12u, 13u, 10u], r.C.Visible);
        Assert.Equal([10u, 11u, 12u, 13u], r.C.Favorites(0));
        Assert.Equal(M(0, 0, 0), r.C.Outline);
        Assert.Empty(r.Saved);
        Assert.Empty(r.G.Calls);
    }

    [Fact]
    public void Construction_DoesNotReconcile_NothingIsSentOrSaved_UntilTheCharactersLoad()
    {
        // The settings at construction are a placeholder: reconciling against them would reorder the server.
        var r = new Rig();
        r.G.Tabs[0].AddRange([1u, 2u, 3u]);
        SpellDeckSettings settings = Saved(new() { [0] = L((0, 0, 2u), (0, 1, 1u)) });
        r.C = new SpellDeckController(r.G, t => r.G.Tabs[t], () => null, () => r.PerRow,
            settings, r.Saved.Add, () => false, () => true, () => 0);
        r.C.Tick();
        Assert.Empty(r.G.Calls);
        Assert.Empty(r.Saved);
        Assert.Equal(L((0, 0, 2u), (0, 1, 1u)), r.C.Layout);

        r.C.Load(settings);                          // the character's load reconciles
        Assert.Equal(L((0, 0, 2u), (0, 1, 1u), (0, 2, 3u)), r.C.Layout);
        Assert.Equal([2u, 1u, 3u], r.G.Tabs[0]);
        Assert.Single(r.Saved);
    }

    [Fact]
    public void Load_ALongV2LocalTab_SurvivesSaveAndLoad_WithEverySpellInsideTheGrid()
    {
        var r = Make();
        r.PerRow = 2;
        uint[] list = [.. Enumerable.Range(1, 10 * 2 + 1).Select(i => (uint)(500 + i))];
        r.C.Load(SpellDeckSettings.Default with { LocalTabs = [list] });
        r.C.SetAttached(false);                      // any change saves the whole settings
        string path = Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");
        try
        {
            var store = new AcDream.UI.Abstractions.Panels.Settings.SettingsStore(path);
            r.Saved[^1].Save(store, "toon");
            r.C.Load(SpellDeckSettings.Load(new AcDream.UI.Abstractions.Panels.Settings.SettingsStore(path), "toon"));
        }
        finally { File.Delete(path); }
        SpellDeckLayout back = r.C.LayoutOf(8);
        Assert.Equal(list, back.Distinct);
        Assert.All(back.Filled, f => Assert.True(f.Row < SpellDeckGeometry.MaxRows && f.Col < SpellDeckGeometry.MaxCapacity));
        Assert.Equal(7, r.C.RowCount);               // 21 spells at 3 a row
    }

    [Fact]
    public void Load_ThatRaisesTheRowCount_SavesIt()
    {
        var r = Make();
        r.C.Load(Saved(new() { [0] = Tab0, [1] = L((0, 0, 20u)), [8] = L((5, 0, 90u)) }, [90u]));
        Assert.Equal(6, r.C.RowCount);
        Assert.Equal(6, Assert.Single(r.Saved).RowCount);
    }

    [Fact]
    public void Visible_FollowsTheShownSlots_AndPerRowIsAtLeastOne()
    {
        var r = Make();
        r.PerRow = 1;
        Assert.Equal([10u, 10u], r.C.Visible);       // column 0 only
        r.PerRow = 0;
        Assert.Equal(1, r.C.PerRow);
    }

    [Fact]
    public void Load_ReplacesEverything_ReconcilesTheServerTabs_AndSavesOnlyWhenThatChangedSomething()
    {
        var r = Make();
        r.C.Load(Saved(new() { [8] = L((0, 2, 90u)) }, [90u]) with { Tab = 8, RowCount = 5, Attached = false, DetachedRows = 4 });
        Assert.Equal((8, 5, 9, false, (int?)4), (r.C.Tab, r.C.RowCount, r.C.TabCount, r.C.Attached, r.C.DetachedRows));
        Assert.Equal(M(8, 0, 2), r.C.Outline);
        // Tabs 0 and 1 had no saved layout: the server's lists were laid out, once, and saved.
        Assert.Equal(SpellDeckLayout.FromList([10u, 11u, 12u, 13u], 4), r.C.LayoutOf(0));
        Assert.Single(r.Saved);
        Assert.Equal(r.C.LayoutOf(0), r.Saved[0].Layouts![0]);
        Assert.Empty(r.G.Calls);                     // laid out in server order: nothing to send

        r.C.Load(r.Saved[0]);                        // the same again: nothing to save
        Assert.Single(r.Saved);
        r.C.Load(SpellDeckSettings.Default with { Tab = 12 });   // a local tab that is not there: tab 0
        Assert.Equal((0, 8), (r.C.Tab, r.C.TabCount));
    }

    [Fact]
    public void Load_V2Settings_MigratesTheLocalTabsInReadingOrder_AtTheCurrentSlotsPerRow()
    {
        var r = Make();
        r.PerRow = 2;
        r.C.Load(SpellDeckSettings.Default with { Tab = 8, LocalTabs = [[1u, 2u, 3u]] });
        Assert.Equal(L((0, 0, 1u), (0, 1, 2u), (1, 0, 3u)), r.C.LayoutOf(8));
        Assert.Equal(3u, r.C.SlotSpell(1, 0));
    }

    [Fact]
    public void Load_AddsRowsSoEveryTabsLowestSpellIsShown()
    {
        var r = Make();
        r.C.Load(Saved(new() { [0] = Tab0, [1] = L((0, 0, 20u)), [8] = L((5, 0, 90u)) }, [90u]));
        Assert.Equal(6, r.C.RowCount);
    }

    // ── Reconcile ─────────────────────────────────────────────────

    [Fact]
    public void AServerAddFromElsewhere_LandsInTheFirstEmptySlot_AndTheServerFollowsTheLayout_SavingOnce()
    {
        var r = Make();
        r.G.Tabs[0].Add(50u);
        r.C.ServerFavoritesChanged();
        Assert.Equal(50u, r.C.SlotSpell(0, 2));
        AssertInSync(r);
        Assert.Single(r.Saved);
        Assert.Equal(r.C.Layout, r.Saved[0].Layouts![0]);
    }

    [Fact]
    public void AServerRemovalFromElsewhere_ClearsEveryCopy_AndRepairsTheOutline()
    {
        var r = Make();
        r.C.Click(2, 0);
        r.G.Tabs[0].Remove(10u);
        r.C.ServerFavoritesChanged();
        Assert.False(r.C.Layout.Contains(10u));
        Assert.Equal(M(0, 0, 1), r.C.Outline);
        Assert.Single(r.Saved);
        Assert.Empty(r.G.Calls);
    }

    [Fact]
    public void TheEchoOfTheDecksOwnOps_ChangesNothing_SendsNothing_SavesNothing()
    {
        var r = Make();
        // The game raises the change event inside every op (OnChange): those echoes are ignored.
        r.C.DropOnSlot(Book(77u), 0, 2, copy: false);
        AssertInSync(r);
        int calls = r.G.Calls.Count, saves = r.Saved.Count;
        SpellDeckLayout layout = r.C.Layout;
        r.C.ServerFavoritesChanged();                // the echo once the ops are done
        r.C.ServerFavoritesChanged();
        Assert.Equal(layout, r.C.Layout);
        Assert.Equal(calls, r.G.Calls.Count);
        Assert.Equal(saves, r.Saved.Count);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void AReconcileThatDoesNotFit_AddsRowsFirst_ThenWidens_AndKeepsTheRestOnTheServer()
    {
        var r = Make(settings: SpellDeckSettings.Default);
        r.G.Tabs[3].AddRange(Enumerable.Range(1, 14).Select(i => (uint)(100 + i)));   // 14 > 3 rows × 4
        r.C.ServerFavoritesChanged();
        Assert.Equal(4, r.C.RowCount);
        Assert.Equal(4, r.C.MinColumns);
        Assert.Equal(4, r.Saved[^1].RowCount);      // saved with the new row count

        int max = SpellDeckGeometry.MaxRows * SpellDeckGeometry.MaxCapacity;
        r.G.Tabs[4].AddRange(Enumerable.Range(1, max + 2).Select(i => (uint)(1000 + i)));
        r.G.Calls.Clear();
        r.C.ServerFavoritesChanged();
        Assert.Equal(SpellDeckGeometry.MaxRows, r.C.RowCount);
        Assert.Equal(SpellDeckGeometry.MaxCapacity, r.C.MinColumns);
        Assert.Equal(max, r.C.LayoutOf(4).Distinct.Count);
        Assert.Equal(max + 2, r.G.Tabs[4].Count);                       // nothing removed
        Assert.DoesNotContain(r.G.Calls, c => c.StartsWith("remove"));
        Assert.Equal([1000u + (uint)max + 1, 1000u + (uint)max + 2], r.G.Tabs[4].Skip(max));
    }

    // ── Tabs ──────────────────────────────────────────────────────

    [Fact]
    public void ShowTab_ChangesTheTab_OutlinesItsFirstFilledSlot_AndSavesLater()
    {
        var r = Make(settings: Saved(new() { [0] = Tab0, [1] = L((1, 2, 20u)) }));
        r.C.ShowTab(1);
        Assert.Equal(1, r.C.Tab);
        Assert.Equal(M(1, 1, 2), r.C.Outline);
        Assert.Empty(r.Saved);
        r.Now += 1; r.C.Tick();
        Assert.Single(r.Saved);
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
        var r = Make(settings: Saved(new() { [0] = Tab0, [8] = L((0, 1, 90u)) }, [90u]));
        Assert.Equal(9, r.C.TabCount);
        Assert.True(r.C.Handle(InputAction.CombatPrevSpellTab));
        Assert.Equal(8, r.C.Tab);
        Assert.Equal(M(8, 0, 1), r.C.Outline);
        Assert.True(r.C.Handle(InputAction.CombatNextSpellTab));
        Assert.Equal(0, r.C.Tab);
        Assert.True(r.C.Handle(InputAction.CombatLastSpellTab));
        Assert.Equal(8, r.C.Tab);
        Assert.True(r.C.Handle(InputAction.CombatFirstSpellTab));
        Assert.Equal(0, r.C.Tab);
        Assert.Empty(r.Saved);
        r.Now += 1; r.C.Tick();
        Assert.Single(r.Saved);
    }

    // ── Hotkeys, outline, clicks ──────────────────────────────────

    [Fact]
    public void Hotkey_RowRSlotN_CastsWhatThatSlotHolds_AndOutlinesTheSlot()
    {
        var r = Make();
        Assert.True(r.C.Handle(InputAction.UseSpellSlot_4));       // row 0 slot 3 (past the gap)
        Assert.True(r.C.Handle(InputAction.UseSpellRow3Slot1));    // row 2 slot 0: the duplicate
        Assert.Equal(["cast 12", "cast 10"], r.G.Calls);
        Assert.Equal(M(0, 2, 0), r.C.Outline);
        Assert.Empty(r.Saved);
    }

    [Fact]
    public void Hotkey_EmptySlot_HiddenRow_OrSlotPastTheRow_IsHandledAndDoesNothing()
    {
        var r = Make();
        Assert.True(r.C.Handle(InputAction.UseSpellSlot_3));       // the gap
        Assert.True(r.C.Handle(InputAction.UseSpellRow9Slot1));    // row 8: not shown
        Assert.True(r.C.Handle(InputAction.UseSpellSlot_5));       // past the row's 4 slots
        Assert.Empty(r.G.Calls);
        Assert.Equal(M(0, 0, 0), r.C.Outline);
        Assert.False(r.C.Handle(InputAction.MovementJump));
    }

    [Fact]
    public void NextPrevFirstLast_WalkTheFilledSlotsInReadingOrder()
    {
        var r = Make();
        r.C.Handle(InputAction.CombatNextSpell);         // (0,1)
        r.C.Handle(InputAction.CombatNextSpell);         // (0,3): the gap is skipped
        Assert.Equal(M(0, 0, 3), r.C.Outline);
        r.C.Handle(InputAction.CombatCastCurrentSpell);
        r.C.Handle(InputAction.CombatLastSpell);         // (2,0)
        r.C.Handle(InputAction.CombatCastCurrentSpell);
        r.C.Handle(InputAction.CombatPrevSpell);         // (1,1)
        r.C.Handle(InputAction.CombatCastCurrentSpell);
        r.C.Handle(InputAction.CombatFirstSpell);
        r.C.Handle(InputAction.CombatCastCurrentSpell);
        Assert.Equal(["cast 12", "cast 10", "cast 13", "cast 10"], r.G.Calls);
    }

    [Fact]
    public void ClickModes_CastCounts()
    {
        var r = Make(clickCasts: false);
        r.C.Click(1, 1); r.C.Click(1, 1); r.C.DoubleClick(1, 1);   // one physical double-click
        Assert.Equal(["cast 13"], r.G.Calls);
        Assert.Equal(M(0, 1, 1), r.C.Outline);

        var r2 = Make(clickCasts: true);
        r2.C.Click(1, 1); r2.C.Click(1, 1); r2.C.DoubleClick(1, 1);
        Assert.Equal(["cast 13", "cast 13"], r2.G.Calls);

        r2.C.Click(0, 2); r2.C.Click(5, 0); r2.C.Click(0, 9);   // empty, hidden row, past the row
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
    public void TheOutlineMarksASlot_ASwapLeavesItOnTheSlot_AClearedSlotRepairs()
    {
        var r = Make();
        r.C.Click(1, 1);                                              // 13
        r.C.DropOnSlot(Slot(0, 1, 1, 13u), 0, 2, copy: false);       // 13 moves away
        Assert.Equal(M(0, 0, 0), r.C.Outline);                        // the slot emptied: repaired
        r.C.Click(0, 2);                                              // 13 again
        r.C.DropOnSlot(Slot(0, 0, 0, 10u), 0, 2, copy: false);       // swap 10 into it
        Assert.Equal(M(0, 0, 2), r.C.Outline);
        Assert.Equal(10u, r.C.OutlinedSpellId);
        r.PerRow = 2;                                                 // no longer shown: repaired
        r.C.Tick();
        Assert.Equal(M(0, 0, 0), r.C.Outline);
    }

    // ── Drops ─────────────────────────────────────────────────────

    [Fact]
    public void Spellbook_OnAnEmptySlot_Places_AndTheServerGetsItInReadingOrder()
    {
        var r = Make();
        Assert.True(r.C.CanDropOnSlot(Book(77u), 0, 2, copy: false));
        r.C.DropOnSlot(Book(77u), 0, 2, copy: false);
        Assert.Equal(77u, r.C.SlotSpell(0, 2));
        Assert.Equal(["set 0 2 77"], r.G.Calls);
        AssertInSync(r);
        Assert.Single(r.Saved);
        Assert.Equal(r.C.Layout, r.Saved[0].Layouts![0]);
    }

    [Fact]
    public void AnySlotWithGaps_Row2Slot1_HoldsTheSpellThere()
    {
        var r = Make(settings: SpellDeckSettings.Default);
        r.C.DropOnSlot(Book(77u), 1, 0, copy: false);
        Assert.Equal(77u, r.C.SlotSpell(1, 0));
        Assert.Null(r.C.SlotSpell(0, 0));
        Assert.Equal([77u], r.G.Tabs[0]);
    }

    [Fact]
    public void Spellbook_OnAFilledSlot_Replaces_AndOnlyALastCopyLeavesTheServer()
    {
        var r = Make();
        r.C.DropOnSlot(Book(77u), 2, 0, copy: false);                // over the duplicate 10
        Assert.DoesNotContain(r.G.Calls, c => c.StartsWith("remove"));
        AssertInSync(r);
        r.C.DropOnSlot(Book(78u), 0, 3, copy: false);                // over 12, its only copy
        Assert.Contains("remove 0 12", r.G.Calls);
        AssertInSync(r);
        Assert.Equal(2, r.Saved.Count);
    }

    [Fact]
    public void Slot_OnAnEmptySlot_Moves_OnAFilledSlot_Swaps()
    {
        var r = Make();
        r.C.DropOnSlot(Slot(0, 0, 1, 11u), 2, 3, copy: false);
        Assert.Null(r.C.SlotSpell(0, 1));
        Assert.Equal(11u, r.C.SlotSpell(2, 3));
        AssertInSync(r);
        Assert.Single(r.G.Calls);                                     // one moved spell is one op

        r.G.Calls.Clear();
        r.C.DropOnSlot(Slot(0, 0, 3, 12u), 1, 1, copy: false);       // 12 ↔ 13
        Assert.Equal((13u, 12u), (r.C.SlotSpell(0, 3)!.Value, r.C.SlotSpell(1, 1)!.Value));
        AssertInSync(r);
        Assert.Single(r.G.Calls);
    }

    [Fact]
    public void Copy_Duplicates_AndTheExtraCopyNeverReachesTheServer()
    {
        var r = Make();
        r.C.DropOnSlot(Slot(0, 0, 1, 11u), 2, 3, copy: true);
        Assert.Equal((11u, 11u), (r.C.SlotSpell(0, 1)!.Value, r.C.SlotSpell(2, 3)!.Value));
        Assert.Empty(r.G.Calls);
        Assert.Single(r.Saved);                                       // still saved locally

        r.C.DropOnSlot(Slot(0, 0, 0, 10u), 1, 1, copy: true);        // over 13: 13 leaves
        Assert.Equal(["remove 0 13"], r.G.Calls);
        AssertInSync(r);
    }

    [Fact]
    public void ASlotFromAnotherTab_MovesThere_DestinationFirst()
    {
        // The shown tab changed mid-drag (a tab key): the lifted spell leaves its own slot.
        var r = Make();
        r.C.ShowTab(1);
        r.C.DropOnSlot(Slot(0, 0, 3, 12u), 0, 2, copy: false);
        Assert.Equal(["set 1 1 12", "remove 0 12"], r.G.Calls);
        AssertInSync(r, 0);
        AssertInSync(r, 1);
    }

    [Fact]
    public void ADropOffTheShownSlots_OrOfAStaleSlot_DoesNothing()
    {
        var r = Make();
        Assert.False(r.C.CanDropOnSlot(Book(77u), 3, 0, copy: false));
        r.C.DropOnSlot(Book(77u), 3, 0, copy: false);                // not a shown row
        r.C.DropOnSlot(Book(77u), 0, 4, copy: false);                // past the row
        Assert.False(r.C.CanDropOnSlot(Slot(0, 0, 2, 10u), 1, 0, copy: false));
        r.C.DropOnSlot(Slot(0, 0, 2, 10u), 1, 0, copy: false);       // its slot no longer holds it
        r.C.DropOutside(Slot(0, 0, 2, 10u));
        Assert.Empty(r.G.Calls);
        Assert.Empty(r.Saved);
    }

    [Fact]
    public void Bookmark_PutsTheSpellInThatTabsFirstEmptySlot()
    {
        var r = Make();
        Assert.True(r.C.CanDropOnTab(Slot(0, 0, 3, 12u), 1, copy: false));
        r.C.DropOnTab(Slot(0, 0, 3, 12u), 1, copy: false);
        Assert.Equal(12u, r.C.LayoutOf(1).Get(0, 1));
        Assert.Null(r.C.SlotSpell(0, 3));
        Assert.Equal(["set 1 1 12", "remove 0 12"], r.G.Calls);
        Assert.Equal(0, r.C.Tab);
        Assert.Single(r.Saved);

        r.G.Calls.Clear();
        r.C.DropOnTab(Book(77u), 2, copy: false);
        Assert.Equal(["set 2 0 77"], r.G.Calls);

        r.G.Calls.Clear();
        r.C.DropOnTab(Slot(0, 0, 0, 10u), 1, copy: true);            // copied: tab 0 keeps it
        Assert.Equal(["set 1 2 10"], r.G.Calls);
        Assert.Equal(10u, r.C.SlotSpell(0, 0));
    }

    [Fact]
    public void Bookmark_OfAFullTab_SaysSo_AndItsOwnTab_DoesNothing_UnlessCopied()
    {
        var r = Make();
        r.C.DropOnTab(Slot(0, 0, 0, 10u), 0, copy: false);
        Assert.False(r.C.CanDropOnTab(Slot(0, 0, 0, 10u), 0, copy: false));
        Assert.False(r.C.CanDropOnTab(Book(20u), 8, copy: false));    // no such tab
        Assert.Empty(r.G.Calls);
        r.C.DropOnTab(Slot(0, 0, 0, 10u), 0, copy: true);             // a copy in the first empty slot
        Assert.Equal(10u, r.C.SlotSpell(0, 2));
        Assert.Empty(r.G.Calls);

        for (uint s = 30; s < 41; s++) r.C.DropOnTab(Book(s), 1, copy: false);   // tab 1 fills 12 slots
        r.G.Calls.Clear();
        Assert.False(r.C.CanDropOnTab(Book(99u), 1, copy: false));
        r.C.DropOnTab(Book(99u), 1, copy: false);
        Assert.Equal(["msg That spell tab is full."], r.G.Calls);
    }

    [Fact]
    public void DragOff_AndRemoveFromTab_ClearTheSlot_AndExamineChecksTheSlot()
    {
        var r = Make();
        r.C.DropOutside(Slot(0, 2, 0, 10u));                         // a duplicate: the server keeps 10
        Assert.Null(r.C.SlotSpell(2, 0));
        Assert.Empty(r.G.Calls);
        r.C.DropOutside(Book(12u));                                   // a spellbook spell: nothing
        r.C.Examine(0, 0, 1, 11u);
        r.C.Examine(0, 0, 1, 12u);                                    // not in that slot
        r.C.Examine(1, 0, 0, 20u);                                    // tab 1 is not shown
        r.C.RemoveFromTab(0, 0, 3, 99u);
        r.C.RemoveFromTab(0, 0, 3, 12u);
        Assert.Equal(["examine 11", "remove 0 12"], r.G.Calls);
        Assert.Equal(2, r.Saved.Count);
    }

    [Fact]
    public void SpellbookAdd_GoesToTheFirstEmptySlot_PresentOnlyOutlines_FullSays()
    {
        var r = Make();
        r.C.AddFavoriteFromSpellbook(77u);
        Assert.Equal(77u, r.C.SlotSpell(0, 2));
        Assert.Equal(["set 0 2 77"], r.G.Calls);

        r.G.Calls.Clear();
        r.C.AddFavoriteFromSpellbook(13u);
        Assert.Empty(r.G.Calls);
        Assert.Equal(M(0, 1, 1), r.C.Outline);

        r.C.ShowTab(1);
        for (uint s = 30; s < 41; s++) r.C.AddFavoriteFromSpellbook(s);
        r.G.Calls.Clear();
        r.C.AddFavoriteFromSpellbook(99u);
        Assert.Equal(["msg That spell tab is full."], r.G.Calls);
    }

    // ── Local tabs ────────────────────────────────────────────────

    [Fact]
    public void LocalTabEdits_NeverCallTheServer_AndSaveTheLayoutOncePerChange()
    {
        var r = Make(settings: Saved(new() { [0] = Tab0, [8] = L((0, 0, 90u), (0, 1, 91u)) }, [90u, 91u]) with { Tab = 8 });
        Assert.Equal(M(8, 0, 0), r.C.Outline);
        r.C.DropOnSlot(Book(92u), 1, 2, copy: false);
        r.C.DropOnSlot(Slot(8, 1, 2, 92u), 0, 0, copy: false);       // swap with 90
        r.C.DropOnSlot(Slot(8, 0, 0, 92u), 2, 3, copy: true);
        r.C.DropOutside(Slot(8, 1, 2, 90u));
        r.C.AddFavoriteFromSpellbook(93u);
        r.C.RemoveFromTab(8, 0, 2, 93u);
        Assert.Empty(r.G.Calls);
        Assert.Equal(6, r.Saved.Count);
        Assert.Equal(L((0, 0, 92u), (0, 1, 91u), (2, 3, 92u)), r.Saved[^1].Layouts![8]);
        Assert.Equal([92u, 91u], r.Saved[^1].LocalTabs[0]);
    }

    [Fact]
    public void MoveBetweenLocalAndServerTabs_SendsOnlyTheServerHalf_AndSavesOnce()
    {
        var r = Make(settings: Saved(new() { [0] = Tab0, [8] = L((0, 0, 90u)) }, [90u], []));
        r.C.DropOnTab(Slot(0, 0, 3, 12u), 8, copy: false);           // server → local
        Assert.Equal(["remove 0 12"], r.G.Calls);
        Assert.Single(r.Saved);
        Assert.Equal(L((0, 0, 90u), (0, 1, 12u)), r.Saved[^1].Layouts![8]);

        r.G.Calls.Clear();
        r.C.ShowTab(8);
        r.C.DropOnTab(Slot(8, 0, 0, 90u), 1, copy: false);           // local → server
        Assert.Equal(["set 1 0 90"], r.G.Calls);
        Assert.Equal(2, r.Saved.Count);                               // the drop's save carries the pending tab too
        Assert.Equal(8, r.Saved[^1].Tab);

        r.C.DropOnTab(Slot(8, 0, 1, 12u), 9, copy: false);           // local → local: one save for both halves
        Assert.Equal(3, r.Saved.Count);
        Assert.True(r.C.LayoutOf(8).IsEmpty);
        Assert.Equal(L((0, 0, 12u)), r.Saved[^1].Layouts![9]);
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
        var r = Make(settings: Saved(new() { [0] = Tab0, [8] = L((0, 0, 90u)), [9] = L((0, 0, 95u)) }, [90u], [95u]) with { Tab = 9 });
        Assert.True(r.C.CanRemoveLastLocalTab);
        r.C.RemoveLastLocalTab();
        Assert.Equal((9, 8), (r.C.TabCount, r.C.Tab));
        Assert.Equal(M(8, 0, 0), r.C.Outline);
        Assert.Single(r.Saved);
        Assert.False(r.Saved[^1].Layouts!.ContainsKey(9));

        r.C.ShowTab(0);
        r.C.RemoveLastLocalTab();                                 // not shown: the tab stays
        Assert.Equal((0, 8), (r.C.Tab, r.C.TabCount));
        Assert.False(r.C.CanRemoveLastLocalTab);
        r.C.RemoveLastLocalTab();                                 // server tabs cannot be removed
        Assert.Equal(8, r.C.TabCount);
        Assert.Equal(2, r.Saved.Count);
        Assert.Empty(r.G.Calls);
    }

    // ── Rows and columns ─────────────────────────────────────────

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
    public void RemoveRow_IsRefusedWhileAnyTabHoldsASpellInTheLastRow()
    {
        var r = Make();
        Assert.False(r.C.CanRemoveRow);              // tab 0 has 10 in row 3
        r.C.RemoveRow();
        Assert.Equal(3, r.C.RowCount);
        r.C.DropOutside(Slot(0, 2, 0, 10u));
        Assert.True(r.C.CanRemoveRow);
        r.C.RemoveRow();
        Assert.Equal(2, r.C.RowCount);
        Assert.False(r.C.CanRemoveRow);              // 13 is in row 2

        // A local tab counts too.
        var r2 = Make(settings: Saved(new() { [8] = L((3, 0, 90u)) }, [90u]) with { RowCount = 5 });
        r2.C.RemoveRow();
        Assert.Equal(4, r2.C.RowCount);
        Assert.False(r2.C.CanRemoveRow);
    }

    [Fact]
    public void MinColumns_IsTheRightMostOccupiedColumnOverAllTabs()
    {
        var r = Make();
        Assert.Equal(4, r.C.MinColumns);             // tab 0's column 3
        Assert.Equal(3, r.C.MinRows);
        var r2 = Make(settings: Saved(new() { [0] = Tab0, [8] = L((0, 7, 90u)) }, [90u]));
        Assert.Equal(8, r2.C.MinColumns);            // the local tab, not the shown one
    }

    // ── Attachment ────────────────────────────────────────────────

    [Fact]
    public void AttachAndDetachedRows_SaveTheWholeSettings()
    {
        var r = Make(settings: Saved(new() { [0] = Tab0, [8] = L((0, 0, 90u)) }, [90u]) with { Tab = 1 });
        r.C.SetAttached(false);
        r.C.SetAttached(false);
        r.C.SetDetachedRows(4);
        r.C.SetDetachedRows(4);
        Assert.Equal(2, r.Saved.Count);
        SpellDeckSettings s = r.Saved[^1];
        Assert.Equal((1, 3, false, (int?)4), (s.Tab, s.RowCount, s.Attached, s.DetachedRows));
        Assert.Equal([90u], Assert.Single(s.LocalTabs));
        Assert.Equal(Tab0, s.Layouts![0]);
    }
}
