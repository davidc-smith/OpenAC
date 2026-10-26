using AcDream.App.UI.Hud;
using AcDream.Core.Combat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class DeckGeometryTests
{
    private static readonly DeckGeometry G = DeckGeometry.Default;

    private static CommandDeckView View(CombatMode mode) =>
        new CommandDeckModel().Update(default(CommandDeckInput) with { Mode = mode, SessionKey = 1 }, 0);

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    public static TheoryData<float, float> Sizes()
    {
        var data = new TheoryData<float, float>();
        foreach (float scale in new[] { 0.75f, 1f, 1.5f })
            foreach (float stretch in new[] { 1f, 1.5f, 2f })
                data.Add(scale, stretch);
        return data;
    }

    [Fact]
    public void DefaultMatchesTheOriginalLayout()
    {
        Assert.Equal((420f, 150f), (G.Width, G.Height));
        Assert.Equal(new DeckRect(70f, 4f, 350f, 142f), G.Frame);
        Assert.Equal(new DeckRect(32f, 29f, 92f, 92f), G.Orb);
        Assert.Equal(new DeckRect(134f, 16f, 274f, 20f), G.HealthBar);
        Assert.Equal(new DeckRect(134f, 41f, 274f, 20f), G.StaminaBar);
        Assert.Equal(new DeckRect(134f, 66f, 274f, 20f), G.ManaBar);
        Assert.Equal(new DeckRect(134f, 92f, 274f, 22f), G.ContextRow);
        Assert.Equal(new DeckRect(134f, 92f, 60f, 22f), G.RowLabel);
        Assert.Equal(new DeckRect(200f, 98f, 118f, 10f), G.PowerTrack);
        Assert.Equal(new DeckRect(326f, 92f, 26f, 22f), G.HeightButton(0));
        Assert.Equal(new DeckRect(382f, 92f, 26f, 22f), G.HeightButton(2));
        Assert.Equal(new DeckRect(382f, 92f, 26f, 22f), G.Recast);
        Assert.Equal(new DeckRect(200f, 92f, 176f, 22f), G.CastingText);
        Assert.Equal(new DeckRect(260f, 92f, 26f, 22f), G.RecallStud(2));
        Assert.Equal(new DeckRect(134f, 122f, 60f, 14f), G.JumpLabel);
        Assert.Equal(new DeckRect(200f, 125f, 208f, 8f), G.JumpTrack);
        Assert.Equal(new DeckRect(2f, 70f, 26f, 22f), G.Stud(1));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void EveryRectInside(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        foreach (DeckRect r in g.AllRects())
        {
            Assert.True(r.X >= 0 && r.Y >= 0, r.ToString());
            Assert.True(r.X + r.W <= g.Width + 0.01f, r.ToString());
            Assert.True(r.Y + r.H <= g.Height + 0.01f, r.ToString());
        }
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void RightHandControlsStayAgainstTheRightEdge(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        float right = g.HealthBar.X + g.HealthBar.W;
        Assert.Equal(right, g.HeightButton(2).X + g.HeightButton(2).W, 2);
        Assert.Equal(right, g.Recast.X + g.Recast.W, 2);
        Assert.Equal(right, g.JumpTrack.X + g.JumpTrack.W, 2);
        Assert.Equal(g.Width - 12f * g.Scale, right, 2);
        Assert.True(g.PowerTrack.X + g.PowerTrack.W < g.HeightButton(0).X);
        Assert.True(g.CastingText.X + g.CastingText.W < g.Recast.X);
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ControlsDoNotOverlap(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        DeckRect[] power = [g.PowerTrack, g.HeightButton(0), g.HeightButton(1), g.HeightButton(2)];
        DeckRect[] recall = [g.RecallStud(0), g.RecallStud(1), g.RecallStud(2), g.RecallStud(3), g.RecallStud(4)];
        DeckRect[] studs = [g.Stud(0), g.Stud(1), g.Stud(2)];
        foreach (DeckRect[] group in new[] { power, recall, studs, [g.CastingText, g.Recast] })
            for (int i = 0; i < group.Length; i++)
                for (int j = i + 1; j < group.Length; j++)
                    Assert.False(Overlaps(group[i], group[j]), $"{group[i]} / {group[j]}");
    }

    private static bool Overlaps(DeckRect a, DeckRect b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    [Fact]
    public void StretchLengthensTheBarsTracksAndCastingLine_NotTheFixedParts()
    {
        DeckGeometry wide = DeckGeometry.Create(1f, 2f);
        Assert.Equal(840f, wide.Width);
        Assert.Equal(150f, wide.Height);
        Assert.Equal(G.HealthBar.W + 420f, wide.HealthBar.W);
        Assert.Equal(G.JumpTrack.W + 420f, wide.JumpTrack.W);
        Assert.Equal(G.PowerTrack.W + 420f, wide.PowerTrack.W);
        Assert.Equal(G.CastingText.W + 420f, wide.CastingText.W);
        Assert.Equal(G.Orb, wide.Orb);
        Assert.Equal(G.RecallStud(4), wide.RecallStud(4));
        Assert.Equal(G.Stud(0), wide.Stud(0));
        Assert.Equal(G.RowLabel, wide.RowLabel);
        Assert.Equal(G.Recast.W, wide.Recast.W);
    }

    [Fact]
    public void ScaleMultipliesEverything()
    {
        DeckGeometry big = DeckGeometry.Create(1.5f, 1f);
        Assert.Equal((630f, 225f), (big.Width, big.Height));
        Assert.Equal(new DeckRect(48f, 43.5f, 138f, 138f), big.Orb);
        Assert.Equal(G.PowerTrack.W * 1.5f, big.PowerTrack.W, 3);
    }

    [Theory]
    [InlineData(0.5f, 0.75f)]
    [InlineData(0.77f, 0.75f)]
    [InlineData(0.83f, 0.85f)]
    [InlineData(0.97f, 1f)]
    [InlineData(1.03f, 1f)]
    [InlineData(1.04f, 1.05f)]
    [InlineData(1.26f, 1.25f)]
    [InlineData(2f, 1.5f)]
    [InlineData(float.NaN, 1f)]
    public void SnapScale(float raw, float expected) =>
        Assert.Equal(expected, DeckGeometry.SnapScale(raw), 3);

    [Fact]
    public void FromSize_ReadsScaleFromHeightAndStretchFromWidth()
    {
        DeckGeometry g = DeckGeometry.FromSize(630f, 187.5f);
        Assert.Equal(1.25f, g.Scale, 3);
        Assert.Equal(1.2f, g.Stretch, 3);
        Assert.Equal(630f, g.Width, 2);
        Assert.Equal(187.5f, g.Height, 2);
    }

    [Theory]
    [InlineData(5000f, 150f, 1f, 2f)]        // too wide: stretch limited
    [InlineData(100f, 150f, 0.75f, 1f)]      // too narrow even at the smallest scale
    [InlineData(399f, 150f, 0.95f, 1f)]      // narrow width lowers the scale
    [InlineData(420f, 9999f, 1f, 1f)]        // too tall: 1.5 does not fit 420 wide, so 1.0
    [InlineData(float.NaN, float.NaN, 1f, 1f)]
    public void FromSize_LimitsInvalidSizes(float width, float height, float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.FromSize(width, height);
        Assert.Equal(scale, g.Scale, 3);
        Assert.Equal(stretch, g.Stretch, 3);
    }

    [Fact]
    public void WithWidth_KeepsTheScale()
    {
        DeckGeometry g = DeckGeometry.Create(1.25f, 1f).WithWidth(700f);
        Assert.Equal(1.25f, g.Scale, 3);
        Assert.Equal(700f, g.Width, 2);
        Assert.Equal(187.5f, g.Height, 2);
    }

    [Fact]
    public void WithCornerSize_KeepsTheStretch_AndFollowsTheAxisThatMovedMost()
    {
        DeckGeometry start = DeckGeometry.Create(1f, 1.5f);   // 630 x 150
        DeckGeometry byHeight = start.WithCornerSize(632f, 195f);
        Assert.Equal(1.3f, byHeight.Scale, 3);
        Assert.Equal(1.5f, byHeight.Stretch, 3);

        DeckGeometry byWidth = start.WithCornerSize(787.5f, 151f);
        Assert.Equal(1.25f, byWidth.Scale, 3);
        Assert.Equal(1.5f, byWidth.Stretch, 3);
    }

    [Theory]
    [InlineData(1.5f, 2f, 9999f, 9999f, 1.5f, 2f)]      // fits: unchanged
    [InlineData(1.35f, 1f, 600f, 200f, 1.3f, 1f)]       // too tall: the step under 200 / 150
    [InlineData(1.45f, 1f, 600f, 999f, 1.4f, 1f)]       // too wide at stretch 1: the step under 600 / 420
    [InlineData(1.25f, 2f, 600f, 999f, 1.25f, 1.14f)]   // too wide: the stretch gives way first
    [InlineData(1f, 1f, 100f, 50f, 0.75f, 1f)]          // no room at all: the smallest size
    public void FitWithin_StepsTheScaleDownAndNarrowsTheStretch(
        float scale, float stretch, float roomW, float roomH, float fitScale, float fitStretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch).FitWithin(roomW, roomH);
        Assert.Equal(fitScale, g.Scale, 3);
        Assert.Equal(fitStretch, g.Stretch, 2);
        if (fitScale > DeckGeometry.MinScale)
            Assert.True(g.Width <= roomW + 0.01f && g.Height <= roomH + 0.01f, $"{g.Width} x {g.Height}");
    }

    [Theory]
    [InlineData(CombatMode.NonCombat)]
    [InlineData(CombatMode.Melee)]
    [InlineData(CombatMode.Magic)]
    public void OrbInEveryMode(CombatMode mode)
    {
        var (x, y) = Centre(G.Orb);
        Assert.Equal(DeckZone.Orb, G.ZoneAt(View(mode), x, y));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void RowZonesFollowTheRow(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        var (hx, hy) = Centre(g.HeightButton(0));
        var (rx, ry) = Centre(g.Recast);
        var (lx, ly) = Centre(g.RecallStud(2));

        Assert.Equal(DeckZone.High, g.ZoneAt(View(CombatMode.Melee), hx, hy));
        Assert.Equal(DeckZone.Recast, g.ZoneAt(View(CombatMode.Magic), rx, ry));
        Assert.Equal(DeckZone.Recall2, g.ZoneAt(View(CombatMode.NonCombat), lx, ly));

        Assert.NotEqual(DeckZone.High, g.ZoneAt(View(CombatMode.Magic), hx, hy));
        Assert.NotEqual(DeckZone.Recast, g.ZoneAt(View(CombatMode.NonCombat), rx, ry));
        Assert.NotEqual(DeckZone.Recall2, g.ZoneAt(View(CombatMode.Melee), lx, ly));
    }

    [Fact]
    public void StudsOnlyInCombat()
    {
        var (x, y) = Centre(G.Stud(0));
        Assert.Equal(DeckZone.StudRepeat, G.ZoneAt(View(CombatMode.Melee), x, y));
        Assert.Equal(DeckZone.None, G.ZoneAt(View(CombatMode.NonCombat), x, y));
    }

    [Fact]
    public void FrameEdgeIsNone()
    {
        DeckRect f = G.Frame;
        Assert.Equal(DeckZone.None, G.ZoneAt(View(CombatMode.Melee), f.X + f.W - 4, f.Y + 4));
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void PowerAtMapsTheTrack(float scale, float stretch)
    {
        DeckGeometry g = DeckGeometry.Create(scale, stretch);
        DeckRect t = g.PowerTrack;
        Assert.Equal(0f, g.PowerAt(t.X - 10));
        Assert.Equal(1f, g.PowerAt(t.X + t.W + 10));
        Assert.Equal(0.5f, g.PowerAt(t.X + t.W / 2f), 3);
    }

    [Fact]
    public void RecastIsAnIconButtonAndTheCastingLineEndsBeforeIt()
    {
        DeckRect recast = G.Recast, text = G.CastingText;
        Assert.Equal(G.RecallStud(0).W, recast.W);
        Assert.True(text.X + text.W < recast.X);
        Assert.True(text.W >= 170f);
    }
}
