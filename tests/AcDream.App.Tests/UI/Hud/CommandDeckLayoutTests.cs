using AcDream.App.UI.Hud;
using AcDream.Core.Combat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class CommandDeckLayoutTests
{
    private static CommandDeckView View(CombatMode mode) =>
        new CommandDeckModel().Update(default(CommandDeckInput) with { Mode = mode, SessionKey = 1 }, 0);

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    [Fact]
    public void FixedSize_AndEveryRectInside()
    {
        Assert.Equal(420f, CommandDeckLayout.Width);
        Assert.Equal(150f, CommandDeckLayout.Height);
        foreach (DeckRect r in CommandDeckLayout.AllRects())
        {
            Assert.True(r.X >= 0 && r.Y >= 0, r.ToString());
            Assert.True(r.X + r.W <= CommandDeckLayout.Width, r.ToString());
            Assert.True(r.Y + r.H <= CommandDeckLayout.Height, r.ToString());
        }
    }

    [Theory]
    [InlineData(CombatMode.NonCombat)]
    [InlineData(CombatMode.Melee)]
    [InlineData(CombatMode.Magic)]
    public void OrbInEveryMode(CombatMode mode)
    {
        var (x, y) = Centre(CommandDeckLayout.Orb);
        Assert.Equal(DeckZone.Orb, CommandDeckLayout.ZoneAt(View(mode), x, y));
    }

    [Fact]
    public void RowZonesFollowTheRow()
    {
        var (hx, hy) = Centre(CommandDeckLayout.HeightButton(0));
        var (rx, ry) = Centre(CommandDeckLayout.Recast);
        var (lx, ly) = Centre(CommandDeckLayout.RecallStud(2));

        Assert.Equal(DeckZone.High, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), hx, hy));
        Assert.Equal(DeckZone.Recast, CommandDeckLayout.ZoneAt(View(CombatMode.Magic), rx, ry));
        Assert.Equal(DeckZone.Recall2, CommandDeckLayout.ZoneAt(View(CombatMode.NonCombat), lx, ly));

        Assert.NotEqual(DeckZone.High, CommandDeckLayout.ZoneAt(View(CombatMode.Magic), hx, hy));
        Assert.NotEqual(DeckZone.Recast, CommandDeckLayout.ZoneAt(View(CombatMode.NonCombat), rx, ry));
        Assert.NotEqual(DeckZone.Recall2, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), lx, ly));
    }

    [Fact]
    public void StudsOnlyInCombat()
    {
        var (x, y) = Centre(CommandDeckLayout.Stud(0));
        Assert.Equal(DeckZone.StudRepeat, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), x, y));
        Assert.Equal(DeckZone.None, CommandDeckLayout.ZoneAt(View(CombatMode.NonCombat), x, y));
    }

    [Fact]
    public void FrameEdgeIsNone()
    {
        DeckRect f = CommandDeckLayout.Frame;
        Assert.Equal(DeckZone.None, CommandDeckLayout.ZoneAt(View(CombatMode.Melee), f.X + f.W - 4, f.Y + 4));
    }

    [Fact]
    public void PowerAtMapsTheTrack()
    {
        DeckRect t = CommandDeckLayout.PowerTrack;
        Assert.Equal(0f, CommandDeckLayout.PowerAt(t.X - 10));
        Assert.Equal(1f, CommandDeckLayout.PowerAt(t.X + t.W + 10));
        Assert.Equal(0.5f, CommandDeckLayout.PowerAt(t.X + t.W / 2f), 3);
    }
}
