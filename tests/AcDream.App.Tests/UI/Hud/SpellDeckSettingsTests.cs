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
}
