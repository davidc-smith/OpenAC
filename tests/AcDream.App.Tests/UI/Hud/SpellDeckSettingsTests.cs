using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckSettingsTests
{
    [Fact]
    public void Default_IsThreeRowsOnTabs123_Attached()
    {
        var d = SpellDeckSettings.Default;
        Assert.True(d.Attached);
        Assert.Equal([new SpellDeckRow(1, 0), new SpellDeckRow(2, 1), new SpellDeckRow(3, 2)], d.Rows);
    }

    [Fact]
    public void Normalize_FixesBadInput()
    {
        var rows = Enumerable.Range(0, 12).Select(i => new SpellDeckRow(i % 3, i == 0 ? 9 : 1)).ToArray();
        var n = new SpellDeckSettings(rows, Attached: false).Normalize();
        Assert.Equal(10, n.Rows.Count);
        Assert.Equal(0, n.Rows[0].Tab);
        Assert.Equal(n.Rows.Count, n.Rows.Select(r => r.Id).Distinct().Count());
        Assert.All(n.Rows, r => Assert.True(r.Id > 0));
        Assert.Equal(n.Rows.Max(r => r.Id) + 1, n.NextRowId);
        Assert.Equal(SpellDeckSettings.Default.Rows, new SpellDeckSettings([], true).Normalize().Rows);
    }
}
