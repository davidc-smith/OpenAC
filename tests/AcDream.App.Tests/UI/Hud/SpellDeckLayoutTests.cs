using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>Per-tab slot layout: gaps and duplicates locally, a dense distinct list on the server.</summary>
public sealed class SpellDeckLayoutTests
{
    private static SpellDeckLayout L(params (int Row, int Col, uint Spell)[] slots) => SpellDeckLayout.FromSlots(slots);

    private static (int, int, uint)[] Slots(SpellDeckLayout layout) =>
        layout.Filled.Select(s => (s.Row, s.Col, s.SpellId)).ToArray();

    // --- Get / Place / Clear ---

    [Fact]
    public void Empty_HasNothing()
    {
        Assert.True(SpellDeckLayout.Empty.IsEmpty);
        Assert.Equal(0u, SpellDeckLayout.Empty.Get(0, 0));
        Assert.Empty(SpellDeckLayout.Empty.Filled);
        Assert.Empty(SpellDeckLayout.Empty.Distinct);
    }

    [Fact]
    public void Place_PutsTheSpellInAnyEmptySlot_AndLeavesTheOriginalUnchanged()
    {
        SpellDeckLayout a = SpellDeckLayout.Empty;
        SpellDeckLayout b = a.Place(1, 0, 42);
        Assert.Equal(42u, b.Get(1, 0));
        Assert.Equal(0u, b.Get(0, 0));
        Assert.True(a.IsEmpty);
    }

    [Fact]
    public void Place_OnAFilledSlot_ChangesNothing()
    {
        SpellDeckLayout a = L((0, 0, 1));
        Assert.Same(a, a.Place(0, 0, 2));
    }

    [Fact]
    public void Place_TheSameSpellTwice_KeepsBothCopies()
    {
        SpellDeckLayout a = L((0, 0, 7)).Place(1, 3, 7);
        Assert.Equal([(0, 0, 7u), (1, 3, 7u)], Slots(a));
    }

    [Fact]
    public void Place_RejectsNegativeSlotsAndSpellZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDeckLayout.Empty.Place(-1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDeckLayout.Empty.Place(0, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDeckLayout.Empty.Place(0, 0, 0));
    }

    [Fact]
    public void Get_OutsideOrNegative_IsEmpty()
    {
        SpellDeckLayout a = L((0, 0, 1));
        Assert.Equal(0u, a.Get(-1, 0));
        Assert.Equal(0u, a.Get(5, 5));
    }

    [Fact]
    public void Clear_EmptiesTheSlot()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2)).Clear(0, 0);
        Assert.Equal([(0, 1, 2u)], Slots(a));
    }

    [Fact]
    public void Clear_AnEmptySlot_ChangesNothing()
    {
        SpellDeckLayout a = L((0, 0, 1));
        Assert.Same(a, a.Clear(3, 3));
    }

    [Fact]
    public void ClearingOneCopy_KeepsTheSpellInDistinct()
    {
        SpellDeckLayout a = L((0, 0, 5), (2, 4, 5), (0, 1, 6)).Clear(0, 0);
        Assert.Equal([6u, 5u], a.Distinct);
        Assert.True(a.Contains(5));
    }

    [Fact]
    public void ClearingTheLastCopy_RemovesTheSpellFromDistinct()
    {
        SpellDeckLayout a = L((0, 0, 5), (0, 1, 6)).Clear(0, 0);
        Assert.Equal([6u], a.Distinct);
        Assert.False(a.Contains(5));
    }

    // --- Replace / Move / Swap / Copy ---

    [Fact]
    public void Replace_OverwritesAFilledSlot()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2)).Replace(0, 0, 9);
        Assert.Equal([(0, 0, 9u), (0, 1, 2u)], Slots(a));
    }

    [Fact]
    public void Replace_OnAnEmptySlot_Places()
    {
        SpellDeckLayout a = SpellDeckLayout.Empty.Replace(2, 2, 9);
        Assert.Equal([(2, 2, 9u)], Slots(a));
    }

    [Fact]
    public void Move_ToAnEmptySlot_MovesTheSpell()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2)).Move(0, 0, 1, 0);
        Assert.Equal([(0, 1, 2u), (1, 0, 1u)], Slots(a));
    }

    [Fact]
    public void Move_ToAFilledSlot_OverwritesIt()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2)).Move(0, 0, 0, 1);
        Assert.Equal([(0, 1, 1u)], Slots(a));
    }

    [Fact]
    public void Move_FromAnEmptySlotOrOntoItself_ChangesNothing()
    {
        SpellDeckLayout a = L((0, 0, 1));
        Assert.Same(a, a.Move(0, 3, 0, 4));
        Assert.Same(a, a.Move(0, 0, 0, 0));
    }

    [Fact]
    public void Swap_AcrossRows_ExchangesTheSpells()
    {
        SpellDeckLayout a = L((0, 2, 1), (3, 0, 2)).Swap(0, 2, 3, 0);
        Assert.Equal([(0, 2, 2u), (3, 0, 1u)], Slots(a));
        Assert.Equal([2u, 1u], a.Distinct);
    }

    [Fact]
    public void Swap_WithAnEmptySlot_IsAMove()
    {
        SpellDeckLayout a = L((0, 0, 1)).Swap(0, 0, 1, 1);
        Assert.Equal([(1, 1, 1u)], Slots(a));
    }

    [Fact]
    public void Copy_DuplicatesTheSpell_AndKeepsTheSource()
    {
        SpellDeckLayout a = L((0, 0, 1)).Copy(0, 0, 1, 4);
        Assert.Equal([(0, 0, 1u), (1, 4, 1u)], Slots(a));
        Assert.Equal([1u], a.Distinct);
    }

    [Fact]
    public void Copy_OntoAFilledSlot_OverwritesIt()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2)).Copy(0, 0, 0, 1);
        Assert.Equal([(0, 0, 1u), (0, 1, 1u)], Slots(a));
    }

    [Fact]
    public void Copy_FromAnEmptySlot_ChangesNothing()
    {
        SpellDeckLayout a = L((0, 0, 1));
        Assert.Same(a, a.Copy(2, 2, 0, 1));
    }

    // --- Filled / Distinct ---

    [Fact]
    public void Filled_IsInReadingOrder_RowByRowLeftToRight()
    {
        SpellDeckLayout a = L((1, 0, 3), (0, 5, 2), (0, 1, 1), (2, 2, 4));
        Assert.Equal([(0, 1, 1u), (0, 5, 2u), (1, 0, 3u), (2, 2, 4u)], Slots(a));
    }

    [Fact]
    public void Distinct_IsEachSpellAtItsFirstSlotInReadingOrder()
    {
        SpellDeckLayout a = L((0, 0, 5), (0, 1, 6), (1, 0, 5), (1, 1, 7), (2, 0, 6));
        Assert.Equal([5u, 6u, 7u], a.Distinct);
    }

    [Fact]
    public void Distinct_FollowsTheEarliestCopy_NotThePlacementOrder()
    {
        SpellDeckLayout a = SpellDeckLayout.Empty.Place(1, 0, 5).Place(0, 3, 6).Place(0, 0, 5);
        Assert.Equal([5u, 6u], a.Distinct);
    }

    // --- MaxRow / MaxCol / FirstEmpty ---

    [Fact]
    public void Extents_OfAnEmptyLayout_AreMinusOne()
    {
        Assert.Equal(-1, SpellDeckLayout.Empty.MaxRow);
        Assert.Equal(-1, SpellDeckLayout.Empty.MaxCol);
    }

    [Fact]
    public void Extents_AreTheOccupiedRowAndColumnMaxima()
    {
        SpellDeckLayout a = L((0, 7, 1), (2, 1, 2));
        Assert.Equal(2, a.MaxRow);
        Assert.Equal(7, a.MaxCol);
    }

    [Fact]
    public void FirstEmpty_IsTheFirstGapInReadingOrder()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2), (0, 3, 3));
        Assert.Equal(new SpellDeckSlot(0, 2), a.FirstEmpty(rows: 2, cols: 4));
    }

    [Fact]
    public void FirstEmpty_WrapsToTheNextRow()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2));
        Assert.Equal(new SpellDeckSlot(1, 0), a.FirstEmpty(rows: 2, cols: 2));
    }

    [Fact]
    public void FirstEmpty_IsNull_WhenRowsByColsAreFull()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2), (1, 0, 3), (1, 1, 4), (2, 0, 9));
        Assert.Null(a.FirstEmpty(rows: 2, cols: 2));
        Assert.Null(SpellDeckLayout.Empty.FirstEmpty(rows: 0, cols: 5));
    }

    // --- FromList ---

    [Fact]
    public void FromList_LaysTheListOutInReadingOrder()
    {
        SpellDeckLayout a = SpellDeckLayout.FromList([1, 2, 3, 4, 5], cols: 2);
        Assert.Equal([(0, 0, 1u), (0, 1, 2u), (1, 0, 3u), (1, 1, 4u), (2, 0, 5u)], Slots(a));
        Assert.Equal([1u, 2u, 3u, 4u, 5u], a.Distinct);
    }

    [Fact]
    public void FromList_RejectsNoColumns() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SpellDeckLayout.FromList([1], cols: 0));

    // --- Equality ---

    [Fact]
    public void Layouts_WithTheSameSlots_AreEqual()
    {
        Assert.Equal(L((0, 0, 1), (1, 1, 2)), SpellDeckLayout.Empty.Place(1, 1, 2).Place(0, 0, 1));
        Assert.NotEqual(L((0, 0, 1)), L((0, 1, 1)));
    }

    // --- Reconcile ---

    [Fact]
    public void Reconcile_ClearsEveryCopyOfASpellTheServerNoLongerHas()
    {
        SpellDeckReconcile r = L((0, 0, 1), (0, 1, 2), (1, 0, 1)).Reconcile([2], rows: 2, cols: 4);
        Assert.Equal([(0, 1, 2u)], Slots(r.Layout));
        Assert.Empty(r.Unplaced);
    }

    [Fact]
    public void Reconcile_PlacesMissingServerSpellsInTheFirstEmptySlots()
    {
        SpellDeckReconcile r = L((0, 0, 1), (0, 2, 2)).Reconcile([1, 2, 3, 4], rows: 2, cols: 3);
        Assert.Equal([(0, 0, 1u), (0, 1, 3u), (0, 2, 2u), (1, 0, 4u)], Slots(r.Layout));
        Assert.Empty(r.Unplaced);
    }

    [Fact]
    public void Reconcile_ReportsSpellsThatDidNotFit_InServerOrder()
    {
        SpellDeckReconcile r = L((0, 0, 1)).Reconcile([1, 2, 3, 4], rows: 1, cols: 2);
        Assert.Equal([(0, 0, 1u), (0, 1, 2u)], Slots(r.Layout));
        Assert.Equal([3u, 4u], r.Unplaced);
    }

    [Fact]
    public void Reconcile_WithAReorderedServerList_KeepsTheLayoutPositions()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 1, 2), (1, 0, 3));
        SpellDeckReconcile r = a.Reconcile([3, 1, 2], rows: 2, cols: 2);
        Assert.Equal(a, r.Layout);
        Assert.Empty(r.Unplaced);
    }

    [Fact]
    public void Reconcile_KeepsSpellsOutsideRowsByCols()
    {
        SpellDeckLayout a = L((4, 9, 1));
        SpellDeckReconcile r = a.Reconcile([1, 2], rows: 1, cols: 1);
        Assert.Equal([(0, 0, 2u), (4, 9, 1u)], Slots(r.Layout));
    }

    [Fact]
    public void Reconcile_KeepsDuplicatesOfAServerSpell()
    {
        SpellDeckLayout a = L((0, 0, 1), (0, 3, 1));
        Assert.Equal(a, a.Reconcile([1], rows: 1, cols: 4).Layout);
    }

    [Fact]
    public void Reconcile_OfAnEmptyLayout_FillsInServerOrder()
    {
        SpellDeckReconcile r = SpellDeckLayout.Empty.Reconcile([5, 6, 7], rows: 2, cols: 2);
        Assert.Equal(SpellDeckLayout.FromList([5, 6, 7], cols: 2), r.Layout);
    }

    // --- SyncOps ---

    [Fact]
    public void SyncOps_InSync_IsNothing() =>
        Assert.Empty(L((0, 0, 1), (1, 0, 2), (1, 1, 1)).SyncOps([1, 2]));

    [Fact]
    public void SyncOps_NewSpell_IsOneSetAtItsDistinctPosition() =>
        Assert.Equal([SpellDeckSyncOp.Set(1, 9)], L((0, 0, 1), (0, 3, 9), (1, 0, 2)).SyncOps([1, 2]));

    [Fact]
    public void SyncOps_LastCopyGone_IsOneRemove() =>
        Assert.Equal([SpellDeckSyncOp.Remove(2)], L((0, 0, 1), (0, 2, 3)).SyncOps([1, 2, 3]));

    [Fact]
    public void SyncOps_RemovesComeFirst()
    {
        IReadOnlyList<SpellDeckSyncOp> ops = L((0, 0, 9), (0, 1, 1)).SyncOps([1, 2]);
        Assert.Equal([SpellDeckSyncOp.Remove(2), SpellDeckSyncOp.Set(0, 9)], ops);
    }

    [Fact]
    public void SyncOps_MovingOneSpellToTheEnd_IsOneSet() =>
        Assert.Equal([SpellDeckSyncOp.Set(2, 1)], L((0, 0, 2), (0, 1, 3), (1, 5, 1)).SyncOps([1, 2, 3]));

    [Fact]
    public void SyncOps_ExtraCopies_NeverReachTheServer() =>
        Assert.Empty(L((0, 0, 4), (0, 1, 4), (2, 2, 4)).SyncOps([4]));

    [Fact]
    public void SyncOps_ToFavoriteOp_CarriesTheTab()
    {
        Assert.Equal(SpellDeckFavoriteOp.Set(3, 1, 9), SpellDeckSyncOp.Set(1, 9).ToFavoriteOp(3));
        Assert.Equal(SpellDeckFavoriteOp.Remove(3, 9), SpellDeckSyncOp.Remove(9).ToFavoriteOp(3));
    }

    /// <summary>The client's (and ACE's) rules: Set removes the spell if present, then inserts it at min(pos, count).</summary>
    private static List<uint> Apply(IReadOnlyList<uint> server, IEnumerable<SpellDeckSyncOp> ops)
    {
        var list = server.ToList();
        foreach (SpellDeckSyncOp op in ops)
        {
            if (op.IsRemove) { list.Remove(op.SpellId); continue; }
            Assert.True(op.Position >= 0);
            int pos = Math.Min(op.Position, list.Count);
            list.Remove(op.SpellId);
            list.Insert(Math.Min(pos, list.Count), op.SpellId);
        }
        return list;
    }

    [Fact]
    public void SyncOps_AppliedToTheServerList_AlwaysGiveDistinct()
    {
        for (int seed = 0; seed < 2000; seed++)
        {
            var rng = new Random(seed);
            int pool = rng.Next(1, 16);
            uint Spell() => (uint)rng.Next(1, pool + 1);

            var layout = SpellDeckLayout.Empty;
            int slots = rng.Next(0, 30);
            for (int i = 0; i < slots; i++)
                layout = layout.Replace(rng.Next(0, 4), rng.Next(0, 8), Spell());

            List<uint> server = Enumerable.Range(1, pool).Select(i => (uint)i)
                .Where(_ => rng.Next(3) > 0).OrderBy(_ => rng.Next()).ToList();

            IReadOnlyList<SpellDeckSyncOp> ops = layout.SyncOps(server);
            Assert.Equal(layout.Distinct, Apply(server, ops));

            // Removals first, then sets; each spell touched at most once; no more sets than out-of-place spells.
            int firstSet = ops.ToList().FindIndex(o => !o.IsRemove);
            Assert.True(firstSet < 0 || ops.Skip(firstSet).All(o => !o.IsRemove), $"seed {seed}");
            Assert.Equal(ops.Count, ops.Select(o => o.SpellId).Distinct().Count());
            Assert.True(ops.Count(o => !o.IsRemove) <= layout.Distinct.Count);

            // Already applied: nothing more to do.
            Assert.Empty(layout.SyncOps(layout.Distinct));
        }
    }

    [Fact]
    public void SyncOps_SetsAreMinimal_ForAPureReorder()
    {
        // n spells, one moved: always a single Set.
        for (int seed = 0; seed < 500; seed++)
        {
            var rng = new Random(seed);
            int n = rng.Next(2, 20);
            List<uint> server = Enumerable.Range(1, n).Select(i => (uint)i).ToList();
            var target = server.ToList();
            int from = rng.Next(n), to = rng.Next(n);
            uint moved = target[from];
            target.RemoveAt(from);
            target.Insert(to, moved);
            SpellDeckLayout layout = SpellDeckLayout.FromList(target, cols: 5);
            IReadOnlyList<SpellDeckSyncOp> ops = layout.SyncOps(server);
            Assert.True(ops.Count <= 1, $"seed {seed}: {ops.Count} ops");
            Assert.Equal(target, Apply(server, ops));
        }
    }
}
