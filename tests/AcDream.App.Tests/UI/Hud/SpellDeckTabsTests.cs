using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckTabsTests
{
    private sealed class Server : ISpellDeckActions
    {
        public readonly List<uint>[] Tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        public readonly List<string> Calls = [];
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

    private static (SpellDeckTabs T, Server S, List<int> Saves) Make(params uint[][] local)
    {
        var s = new Server();
        s.Tabs[0].AddRange([10u, 11u]);
        var saves = new List<int>();
        var t = new SpellDeckTabs(tab => s.Tabs[tab], s, () => saves.Add(saves.Count + 1));
        t.Load([.. local.Select(l => (IReadOnlyList<uint>)l)]);
        return (t, s, saves);
    }

    [Fact]
    public void Count_IsEightServerTabsPlusLocal_AndGetReadsEach()
    {
        var (t, _, saves) = Make([30u, 31u], []);
        Assert.Equal(10, t.Count);
        Assert.Equal(2, t.LocalCount);
        Assert.Equal([10u, 11u], t.Get(0));
        Assert.Equal([30u, 31u], t.Get(8));
        Assert.Empty(t.Get(9));
        Assert.Empty(t.Get(10));     // past the end: nothing
        Assert.Empty(t.Get(-1));
        Assert.False(t.IsLocal(7));
        Assert.True(t.IsLocal(8));
        Assert.Empty(saves);         // loading is not a change
    }

    [Fact]
    public void ServerTabEdits_GoToTheServerActions_AndNeverSave()
    {
        var (t, s, saves) = Make([30u]);
        t.Set(0, 0, 11u);
        t.Remove(0, 10u);
        Assert.Equal(["set 0 0 11", "remove 0 10"], s.Calls);
        Assert.Equal([11u], t.Get(0));
        Assert.Empty(saves);
    }

    [Fact]
    public void LocalTabEdits_MutateTheList_SaveOncePerChange_AndNeverCallTheServer()
    {
        var (t, s, saves) = Make([30u, 31u, 32u]);
        t.Set(8, 0, 32u);              // reorder: as the server does it (remove, insert at the position)
        Assert.Equal([32u, 30u, 31u], t.Get(8));
        t.Set(8, 99, 40u);             // insert past the end: appended
        Assert.Equal([32u, 30u, 31u, 40u], t.Get(8));
        t.Remove(8, 30u);
        Assert.Equal([32u, 31u, 40u], t.Get(8));
        Assert.Equal([1, 2, 3], saves);
        t.Remove(8, 999u);             // not there: no change, no save
        t.Set(8, -1, 50u);             // bad position: nothing, as the server's spellbook
        t.Set(12, 0, 50u);             // no such tab: nothing
        Assert.Equal(3, saves.Count);
        Assert.Empty(s.Calls);
        Assert.Equal([[32u, 31u, 40u]], t.Local);
    }

    [Fact]
    public void AddLocal_UpToEight_AndRemoveLastLocal_DiscardsItsSpells()
    {
        var (t, s, saves) = Make();
        Assert.Equal(8, t.Count);
        Assert.False(t.RemoveLastLocal());   // no local tab: server tabs cannot be removed
        for (int i = 0; i < 8; i++) Assert.True(t.AddLocal());
        Assert.False(t.CanAddLocal);
        Assert.False(t.AddLocal());
        Assert.Equal(16, t.Count);
        Assert.Equal(8, saves.Count);

        t.Set(15, 0, 77u);
        Assert.True(t.RemoveLastLocal());
        Assert.Equal(15, t.Count);
        Assert.True(t.AddLocal());
        Assert.Empty(t.Get(15));              // the removed tab's spells went with it
        Assert.Equal(11, saves.Count);
        Assert.Empty(s.Calls);
    }

    [Fact]
    public void Load_TakesACopy_AndCapsAtEightLocalTabs()
    {
        var source = new List<uint> { 1u };
        var (t, _, _) = Make();
        t.Load([source, .. Enumerable.Range(0, 9).Select(_ => (IReadOnlyList<uint>)[])]);
        source.Add(2u);
        Assert.Equal([1u], t.Get(8));
        Assert.Equal(16, t.Count);
    }
}
