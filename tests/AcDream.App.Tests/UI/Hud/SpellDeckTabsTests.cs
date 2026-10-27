using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckTabsTests
{
    /// <summary>The server's favourite lists, applying each call with ACE's rules: a Set of a spell
    /// already in the tab is ignored.
    /// <see cref="OnChange"/> runs inside every change, as the spellbook's changed event does.</summary>
    private sealed class Server : ISpellDeckActions
    {
        public readonly List<uint>[] Tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        public readonly List<string> Calls = [];
        public Action? OnChange;
        public void CastSpell(uint id) => Calls.Add($"cast {id}");
        public void CastItemSpell(uint itemId) => Calls.Add($"item {itemId}");
        public void SetFavorite(int tab, int pos, uint id)
        {
            Calls.Add($"set {tab} {pos} {id}");
            if (Tabs[tab].Contains(id)) return;     // ACE's AddSpellToBar ignores a spell already in the bar
            Tabs[tab].Insert(Math.Min(pos, Tabs[tab].Count), id);
            OnChange?.Invoke();
        }
        public void RemoveFavorite(int tab, uint id) { Calls.Add($"remove {tab} {id}"); Tabs[tab].Remove(id); OnChange?.Invoke(); }
        public void ExamineSpell(uint id) => Calls.Add($"examine {id}");
        public void ShowMessage(string text) => Calls.Add("msg " + text);
    }

    private static SpellDeckLayout L(params (int Row, int Col, uint Spell)[] slots) => SpellDeckLayout.FromSlots(slots);

    /// <summary>No spell left the server: every remove is of a spell set again by the next call (a move:
    /// ACE ignores a Set of a spell already in the bar, so a move is a remove then a set).</summary>
    private static void AssertNoSpellLeft(IReadOnlyList<string> calls)
    {
        for (int i = 0; i < calls.Count; i++)
        {
            if (!calls[i].StartsWith("remove ")) continue;
            string[] remove = calls[i].Split(' ');
            Assert.True(i + 1 < calls.Count, calls[i]);
            string[] set = calls[i + 1].Split(' ');
            Assert.Equal(("set", remove[1], remove[2]), (set[0], set[1], set[3]));
        }
    }

    private static (SpellDeckTabs T, Server S, List<int> Saves) Make(int localCount = 0, Dictionary<int, SpellDeckLayout>? layouts = null)
    {
        var s = new Server();
        s.Tabs[0].AddRange([10u, 11u]);
        var saves = new List<int>();
        // The source hands out the live list, as the spellbook does: the tabs must not hold on to it.
        var t = new SpellDeckTabs(tab => s.Tabs[tab], s, () => saves.Add(saves.Count + 1));
        t.Load(layouts ?? [], localCount);
        return (t, s, saves);
    }

    [Fact]
    public void Count_IsEightServerTabsPlusLocal_AndLayoutReadsEach()
    {
        var (t, _, saves) = Make(2, new() { [8] = L((0, 1, 30u)) });
        Assert.Equal(10, t.Count);
        Assert.Equal(2, t.LocalCount);
        Assert.Equal(L((0, 1, 30u)), t.Layout(8));
        Assert.True(t.Layout(9).IsEmpty);
        Assert.True(t.Layout(10).IsEmpty);     // past the end: nothing
        Assert.True(t.Layout(-1).IsEmpty);
        Assert.False(t.IsLocal(7));
        Assert.True(t.IsLocal(8));
        Assert.Equal(10, t.All.Count);
        Assert.Equal([8], t.Layouts.Keys);
        Assert.Equal([[30u], []], t.Local);
        Assert.Empty(saves);                   // loading is not a change
    }

    [Fact]
    public void ServerTabEdit_SendsExactlyTheLayoutsSyncOps_AndSavesOnce()
    {
        var (t, s, saves) = Make(layouts: new() { [0] = L((0, 0, 10u), (0, 1, 11u)) });
        // 11 to row 1, 12 new at (0, 3), a duplicate of 10 at (2, 2): the server gets 10, 12, 11.
        SpellDeckLayout next = L((0, 0, 10u), (0, 3, 12u), (1, 0, 11u), (2, 2, 10u));
        IReadOnlyList<SpellDeckSyncOp> expected = next.SyncOps([10u, 11u]);
        Assert.True(t.Apply([new(0, next)]));
        Assert.Equal(expected.Select(o => o.IsRemove ? $"remove 0 {o.SpellId}" : $"set 0 {o.Position} {o.SpellId}"), s.Calls);
        Assert.Equal([10u, 12u, 11u], s.Tabs[0]);   // the extra copy never reached the server
        Assert.Equal(next, t.Layout(0));
        Assert.Single(saves);
    }

    [Fact]
    public void ServerTabEdit_RemovingTheLastCopy_RemovesItOnTheServer_ButNotWhileACopyIsLeft()
    {
        var (t, s, _) = Make(layouts: new() { [0] = L((0, 0, 10u), (0, 1, 11u), (1, 0, 10u)) });
        t.Apply([new(0, L((0, 1, 11u), (1, 0, 10u)))]);          // a copy of 10 is left: 11 now reads first
        Assert.Equal([11u, 10u], s.Tabs[0]);
        Assert.Equal(["remove 0 10", "set 0 1 10"], s.Calls);      // a move: 10 stays on the server
        t.Apply([new(0, L((0, 1, 11u)))]);
        Assert.Equal(["remove 0 10"], s.Calls.Skip(2));
        Assert.Equal([11u], s.Tabs[0]);
    }

    [Fact]
    public void ServerTabEdit_KeepsServerSpellsTheLayoutNeverHeld_AfterItsOwnSpells()
    {
        // 77 is on the server but not in the layout (reconcile could not show it): an edit leaves it there.
        var (t, s, _) = Make(layouts: new() { [0] = L((0, 0, 10u), (0, 1, 11u)) });
        s.Tabs[0].Add(77u);
        t.Apply([new(0, L((0, 0, 11u), (0, 1, 10u)))]);
        Assert.Equal([11u, 10u, 77u], s.Tabs[0]);
    }

    [Fact]
    public void LocalTabEdits_ChangeTheLayout_SaveOncePerChange_AndNeverCallTheServer()
    {
        var (t, s, saves) = Make(1);
        Assert.True(t.Apply([new(8, L((1, 2, 30u), (0, 0, 30u)))]));
        Assert.Equal(L((0, 0, 30u), (1, 2, 30u)), t.Layout(8));
        Assert.False(t.Apply([new(8, L((0, 0, 30u), (1, 2, 30u)))]));   // the same layout: no change, no save
        Assert.False(t.Apply([new(12, L((0, 0, 1u)))]));                // no such tab: nothing
        Assert.Single(saves);
        Assert.Empty(s.Calls);
        Assert.Equal([[30u]], t.Local);
    }

    [Fact]
    public void AMoveFromALocalTabToAServerTab_SendsOnlyTheServerHalf_AndSavesOnce()
    {
        var (t, s, saves) = Make(1, new() { [8] = L((0, 0, 30u)), [0] = L((0, 0, 10u), (0, 1, 11u)) });
        t.Apply([new(0, L((0, 0, 10u), (0, 1, 11u), (0, 2, 30u))), new(8, SpellDeckLayout.Empty)]);
        Assert.Equal(["set 0 2 30"], s.Calls);
        Assert.Single(saves);
        Assert.True(t.Layout(8).IsEmpty);
    }

    // ── Reconcile ────────────────────────────────────────────────

    [Fact]
    public void Reconcile_FillsTheServerListIntoEmptySlots_ClearsSpellsTheServerLost_AndSavesOnce()
    {
        var (t, s, saves) = Make(layouts: new() { [0] = L((0, 1, 10u), (1, 0, 99u), (2, 0, 99u)), [1] = L((0, 0, 5u)) });
        s.Tabs[1].Add(5u);
        Assert.True(t.Reconcile(rows: 3, cols: 4));
        Assert.Equal(L((0, 0, 11u), (0, 1, 10u)), t.Layout(0));   // 99 left the server; 11 took the first empty slot
        Assert.Equal(L((0, 0, 5u)), t.Layout(1));
        Assert.Single(saves);
        // The layout reads 11, 10: the server is put in that order.
        Assert.Equal([11u, 10u], s.Tabs[0]);
    }

    [Fact]
    public void Reconcile_IsIdempotent_TheEchoOfItsOwnOpsSendsNothing_AndSavesNothing()
    {
        var (t, s, saves) = Make(layouts: new() { [0] = L((0, 1, 10u), (2, 3, 11u)) });
        s.Tabs[0].Reverse();                              // reordered elsewhere: 11, 10
        int reentered = 0;
        s.OnChange = () => { reentered++; Assert.False(t.Reconcile(3, 4)); };   // ignored while its own ops go out
        t.Reconcile(3, 4);
        Assert.Equal([10u, 11u], s.Tabs[0]);
        Assert.True(reentered > 0);
        int calls = s.Calls.Count, saved = saves.Count;
        s.OnChange = null;
        Assert.False(t.Reconcile(3, 4));                  // the echo: nothing to do
        Assert.Equal(calls, s.Calls.Count);
        Assert.Equal(saved, saves.Count);
    }

    [Fact]
    public void Reconcile_GrowsRowsFirst_ThenColumns_AndSpellsThatStillDoNotFit_StayOnTheServer_AfterTheLayouts()
    {
        var (t, s, _) = Make();
        int max = SpellDeckGeometry.MaxRows * SpellDeckGeometry.MaxCapacity;
        s.Tabs[0].Clear();
        s.Tabs[0].AddRange(Enumerable.Range(1, 2 * 3 + 1).Select(i => (uint)i));   // 7 spells, 2 rows × 3 slots
        t.Reconcile(rows: 2, cols: 3);
        Assert.Equal(2, t.Layout(0).MaxRow);              // a third row, not a wider deck
        Assert.Equal(2, t.Layout(0).MaxCol);
        Assert.Equal(7u, t.Layout(0).Get(2, 0));

        var (t2, s2, _) = Make();
        uint[] many = [.. Enumerable.Range(1, max + 3).Select(i => (uint)i)];
        s2.Tabs[0].Clear();
        s2.Tabs[0].AddRange([.. many.Reverse()]);         // reversed, so the layout's order differs from the server's
        s2.Calls.Clear();
        t2.Reconcile(rows: 3, cols: 4);
        SpellDeckLayout l = t2.Layout(0);
        Assert.Equal(SpellDeckGeometry.MaxRows - 1, l.MaxRow);
        Assert.Equal(SpellDeckGeometry.MaxCapacity - 1, l.MaxCol);
        Assert.Equal(max, l.Distinct.Count);
        uint[] unshown = [.. s2.Tabs[0].Where(id => !l.Contains(id))];
        Assert.Equal(3, unshown.Length);
        AssertNoSpellLeft(s2.Calls);                                    // never removed from the server
        Assert.Equal([.. l.Distinct, .. unshown], s2.Tabs[0]);          // kept after the layout's spells
        Assert.Equal([3u, 2u, 1u], unshown);                            // in server order

        // An edit afterwards still keeps them.
        t2.Apply([new(0, l.Clear(0, 0))]);
        Assert.Equal(unshown, s2.Tabs[0].Skip(max - 1));
    }

    [Fact]
    public void Reconcile_SkipsWhileEveryServerTabIsEmpty_AsAtLoginOrLogout_AndNeverTouchesLocalTabs()
    {
        var (t, s, saves) = Make(1, new() { [0] = L((0, 0, 10u)), [8] = L((0, 0, 30u)) });
        s.Tabs[0].Clear();
        Assert.False(t.Reconcile(3, 4));
        Assert.Equal(L((0, 0, 10u)), t.Layout(0));
        Assert.Equal(L((0, 0, 30u)), t.Layout(8));
        Assert.Empty(saves);
        Assert.Empty(s.Calls);
    }

    // ── Local tabs ───────────────────────────────────────────────

    [Fact]
    public void AddLocal_UpToEight_AndRemoveLastLocal_DiscardsItsLayout()
    {
        var (t, s, saves) = Make();
        Assert.Equal(8, t.Count);
        Assert.False(t.RemoveLastLocal());   // no local tab: server tabs cannot be removed
        for (int i = 0; i < 8; i++) Assert.True(t.AddLocal());
        Assert.False(t.CanAddLocal);
        Assert.False(t.AddLocal());
        Assert.Equal(16, t.Count);
        Assert.Equal(8, saves.Count);

        t.Apply([new(15, L((0, 0, 77u)))]);
        Assert.True(t.RemoveLastLocal());
        Assert.Equal(15, t.Count);
        Assert.True(t.AddLocal());
        Assert.True(t.Layout(15).IsEmpty);   // the removed tab's spells went with it
        Assert.Equal(11, saves.Count);
        Assert.Empty(s.Calls);
    }

    [Fact]
    public void Load_CapsAtEightLocalTabs_AndDropsLayoutsOfTabsThatDoNotExist()
    {
        var (t, _, _) = Make();
        t.Load(new Dictionary<int, SpellDeckLayout> { [9] = L((0, 0, 1u)), [20] = L((0, 0, 2u)) }, 12);
        Assert.Equal(16, t.Count);
        Assert.Equal(L((0, 0, 1u)), t.Layout(9));
        Assert.Equal([9], t.Layouts.Keys);
    }
}
