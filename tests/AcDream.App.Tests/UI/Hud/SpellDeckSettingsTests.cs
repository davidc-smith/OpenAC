using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckSettingsTests
{
    [Fact]
    public void Default_IsTabZero_ThreeRows_Attached_NoLocalTabs()
    {
        var d = SpellDeckSettings.Default;
        Assert.Equal((0, 3, true, (int?)null), (d.Tab, d.RowCount, d.Attached, d.DetachedRows));
        Assert.Empty(d.LocalTabs);
    }

    [Fact]
    public void Normalize_ClampsRows_TruncatesAndCleansLocalTabs()
    {
        var local = Enumerable.Range(0, 10).Select(_ => (IReadOnlyList<uint>)new uint[] { 5, 0, 5, 7 }).ToArray();
        var n = new SpellDeckSettings(3, 99, false, 50, local).Normalize();
        Assert.Equal(10, n.RowCount);
        Assert.Equal(10, n.DetachedRows);
        Assert.Equal(8, n.LocalTabs.Count);
        Assert.All(n.LocalTabs, t => Assert.Equal([5u, 7u], t));
        Assert.Equal(1, new SpellDeckSettings(0, 0, true, null, []).Normalize().RowCount);
    }

    [Fact]
    public void Normalize_TabMustExist()
    {
        Assert.Equal(0, new SpellDeckSettings(8, 3, true, null, []).Normalize().Tab);
        Assert.Equal(0, new SpellDeckSettings(-1, 3, true, null, []).Normalize().Tab);
        Assert.Equal(8, new SpellDeckSettings(8, 3, true, null, [[1u]]).Normalize().Tab);
        Assert.Equal(0, new SpellDeckSettings(9, 3, true, null, [[1u]]).Normalize().Tab);
    }

    [Fact]
    public void LoadSave_RoundTripsThroughStore()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            new SpellDeckSettings(9, 4, false, 6, [[1u, 2u], []]).Save(store, "toon");
            var back = SpellDeckSettings.Load(new SettingsStore(path), "toon");
            Assert.Equal((9, 4, false, (int?)6), (back.Tab, back.RowCount, back.Attached, back.DetachedRows));
            Assert.Equal([1u, 2u], back.LocalTabs[0]);
            Assert.Empty(back.LocalTabs[1]);
            Assert.Equal(SpellDeckSettings.Default, SpellDeckSettings.Load(store, "nobody"));
        }
        finally { File.Delete(path); }
    }

    private static SpellDeckLayout L(params (int Row, int Col, uint Spell)[] slots) => SpellDeckLayout.FromSlots(slots);

    [Fact]
    public void V3_LayoutsRoundTripThroughTheStore_PerTab_WithGapsAndDuplicates()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(path);
            var layouts = new Dictionary<int, SpellDeckLayout>
            {
                [0] = L((1, 0, 5u), (0, 3, 6u), (2, 4, 5u)),
                [8] = L((0, 1, 9u)),
            };
            new SpellDeckSettings(8, 3, true, null, [[9u]], layouts).Save(store, "toon");
            var back = SpellDeckSettings.Load(new SettingsStore(path), "toon");
            IReadOnlyDictionary<int, SpellDeckLayout> got = back.LayoutsAt(perRow: 10);
            Assert.Equal([0, 8], got.Keys.Order());
            Assert.Equal(layouts[0], got[0]);
            Assert.Equal(layouts[8], got[8]);
            Assert.Single(back.LocalTabs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void V2_DataWithoutLayouts_MigratesTheLocalTabsInReadingOrder_AtTheGivenSlotsPerRow()
    {
        var v2 = new SpellDeckSettings(0, 3, true, null, [[1u, 2u, 3u, 4u, 5u], []]).Normalize();
        Assert.Null(v2.Layouts);
        IReadOnlyDictionary<int, SpellDeckLayout> got = v2.LayoutsAt(perRow: 2);
        Assert.Equal([8], got.Keys);     // the empty local tab has no layout; server tabs reconcile from the server
        Assert.Equal(SpellDeckLayout.FromList([1u, 2u, 3u, 4u, 5u], 2), got[8]);
        Assert.Equal(2u, got[8].Get(0, 1));
        Assert.Equal(3u, got[8].Get(1, 0));
    }

    [Fact]
    public void V2_FileWithoutLayouts_LoadsWithNullLayouts()
    {
        string path = Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");
        try
        {
            new SettingsStore(path).SaveSpellDeck("toon", new StoredSpellDeck(8, 2, true, null, [[7u, 8u]]));
            var back = SpellDeckSettings.Load(new SettingsStore(path), "toon");
            Assert.Null(back.Layouts);
            Assert.Equal(SpellDeckLayout.FromList([7u, 8u], 10), back.LayoutsAt(10)[8]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Normalize_DropsLayoutsOfTabsThatDoNotExist_EmptyOnes_AndSlotsPastTheLimits()
    {
        var layouts = new Dictionary<int, SpellDeckLayout>
        {
            [0] = L((0, 0, 1u), (SpellDeckGeometry.MaxRows, 0, 2u), (0, SpellDeckSettings.MaxSavedColumns, 3u)),
            [3] = SpellDeckLayout.Empty,
            [9] = L((0, 0, 4u)),     // no tab 9: only one local tab
        };
        var n = new SpellDeckSettings(0, 3, true, null, [[]], layouts).Normalize();
        Assert.Equal([0], n.Layouts!.Keys);
        Assert.Equal(L((0, 0, 1u)), n.Layouts[0]);
    }

    [Fact]
    public void Normalize_KeepsServerTabColumnsPastTheWidestDeck_SoNoSpellIsLost()
    {
        // Saved by v2, whose widest deck held 27 slots a row: columns 25 and 26 are past v3's 25.
        // A server tab keeps them (unshown); its reconcile re-places spells it cannot show.
        var layouts = new Dictionary<int, SpellDeckLayout>
        {
            [0] = L((0, 0, 1u), (0, 25, 2u), (1, 26, 3u), (2, SpellDeckSettings.MaxSavedColumns - 1, 4u)),
        };
        var n = new SpellDeckSettings(0, 3, true, null, [], layouts).Normalize();
        Assert.Equal(layouts[0], n.Layouts![0]);
        Assert.True(SpellDeckSettings.MaxSavedColumns > SpellDeckGeometry.MaxCapacity);
    }

    [Fact]
    public void Normalize_RePlacesALocalTabsSlotsOutsideTheGrid_IntoTheFirstEmptySlots_InReadingOrder()
    {
        var layouts = new Dictionary<int, SpellDeckLayout>
        {
            [8] = L((0, 0, 1u), (0, 26, 2u), (SpellDeckGeometry.MaxRows, 0, 3u), (0, SpellDeckGeometry.MaxCapacity - 1, 4u)),
        };
        var n = new SpellDeckSettings(8, 3, true, null, [[1u, 2u, 3u, 4u]], layouts).Normalize();
        Assert.Equal(L((0, 0, 1u), (0, 1, 2u), (0, 2, 3u), (0, SpellDeckGeometry.MaxCapacity - 1, 4u)), n.Layouts![8]);
    }

    [Fact]
    public void V2_ALongLocalList_IsLaidOutWideEnoughToFitTheRows()
    {
        uint[] list = [.. Enumerable.Range(1, 10 * 2 + 1).Select(i => (uint)i)];
        SpellDeckLayout l = new SpellDeckSettings(0, 3, true, null, [list]).Normalize().LayoutsAt(perRow: 2)[8];
        Assert.Equal(list, l.Distinct);
        Assert.Equal(2, l.MaxCol);                   // ceil(21 / 10) = 3 a row
        Assert.True(l.MaxRow < SpellDeckGeometry.MaxRows);

        uint[] huge = [.. Enumerable.Range(1, 300).Select(i => (uint)i)];
        SpellDeckLayout h = new SpellDeckSettings(0, 3, true, null, [huge]).Normalize().LayoutsAt(perRow: 2)[8];
        Assert.True(h.MaxCol < SpellDeckGeometry.MaxCapacity);
        Assert.True(h.MaxRow < SpellDeckGeometry.MaxRows);
        Assert.Equal(SpellDeckGeometry.MaxRows * SpellDeckGeometry.MaxCapacity, h.Distinct.Count);   // only a full grid leaves spells out
    }
}
