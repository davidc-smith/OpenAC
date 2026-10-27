using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiCommandDeckDrawTests
{
    private sealed class NoActions : ICommandDeckActions
    {
        public void ToggleCombat() { }
        public void PressAttack(AttackHeight height) { }
        public void ReleaseAttack() { }
        public void SetDesiredPower(float power) { }
        public void SetOption(CharacterOptionId option, bool value) { }
        public void Recall(RuntimePortalCommand command) { }
        public void CastOutlined() { }
    }

    [Theory]
    [InlineData(CombatMode.NonCombat, true)]
    [InlineData(CombatMode.NonCombat, false)]
    [InlineData(CombatMode.Melee, true)]
    [InlineData(CombatMode.Melee, false)]
    [InlineData(CombatMode.Missile, true)]
    [InlineData(CombatMode.Missile, false)]
    [InlineData(CombatMode.Magic, true)]
    [InlineData(CombatMode.Magic, false)]
    public void DrawsEveryModeInsideTheDeck(CombatMode mode, bool known)
    {
        DeckVitalInput Vital(float p, uint cur) => known ? new(p, cur, 100) : new(null, null, null);
        var input = default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, PowerBarLevel = 0.3f, SessionKey = 1,
            Health = Vital(0.2f, 20), Stamina = Vital(0.6f, 60), Mana = Vital(1f, 100),
            OutlinedSpellName = "Flame Bolt", RepeatAttacks = true,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();

        var renderer = MarkupDrawProbe.Draw(deck, DeckGeometry.Default.Width, DeckGeometry.Default.Height);

        int quads = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                quads++;
                float x = s.Verts[i], y = s.Verts[i + 1];
                Assert.InRange(x, -0.01f, DeckGeometry.Default.Width + 0.01f);
                Assert.InRange(y, -0.01f, DeckGeometry.Default.Height + 0.01f);
            }
        Assert.True(quads > 0);
    }

    [Theory]
    [InlineData(CombatMode.NonCombat, true)]
    [InlineData(CombatMode.NonCombat, false)]
    [InlineData(CombatMode.Melee, true)]
    [InlineData(CombatMode.Melee, false)]
    [InlineData(CombatMode.Missile, true)]
    [InlineData(CombatMode.Missile, false)]
    [InlineData(CombatMode.Magic, true)]
    [InlineData(CombatMode.Magic, false)]
    public void DrawsEveryModeInsideTheDeck_Scaled(CombatMode mode, bool known)
    {
        DeckVitalInput Vital(float p, uint cur) => known ? new(p, cur, 100) : new(null, null, null);
        var input = default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, PowerBarLevel = 0.3f, SessionKey = 1,
            Health = Vital(0.2f, 20), Stamina = Vital(0.6f, 60), Mana = Vital(1f, 100),
            OutlinedSpellName = "Flame Bolt", RepeatAttacks = true,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        deck.ApplyGeometry(DeckGeometry.Create(1.5f, 2f));

        var renderer = MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);

        int quads = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                quads++;
                float x = s.Verts[i], y = s.Verts[i + 1];
                Assert.InRange(x, -0.01f, deck.Width + 0.01f);
                Assert.InRange(y, -0.01f, deck.Height + 0.01f);
            }
        Assert.True(quads > 0);
    }

    [Theory]
    [InlineData(CombatMode.NonCombat, DeckZone.Recall2)]
    [InlineData(CombatMode.NonCombat, DeckZone.Orb)]
    [InlineData(CombatMode.Melee, DeckZone.StudRepeat)]
    [InlineData(CombatMode.Melee, DeckZone.Health)]
    public void DrawsAVisibleTooltipInTheOverlay(CombatMode mode, DeckZone zone)
    {
        double now = 1.0;
        var input = default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, SessionKey = 1,
            Health = new(0.5f, 50, 100), RepeatAttacks = true,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => now)
        {
            Left = 300f, Top = 400f,
        };
        deck.Refresh();

        Assert.Empty(MarkupDrawProbe.DrawOverlays(deck).DebugSpriteSegmentVerts);

        DeckRect r = DeckGeometry.Default.RectFor(zone);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseMove, Data1: (int)(r.X + r.W / 2), Data2: (int)(r.Y + r.H / 2)));
        now = 2.0;
        Assert.NotNull(deck.VisibleTooltip);

        var renderer = MarkupDrawProbe.DrawOverlays(deck);
        int verts = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts) verts += s.Verts.Count;
        Assert.True(verts > 0);
    }

    [Fact]
    public void CachedTooltipDrawsTheSameEveryFrame()
    {
        double now = 1.0;
        var input = default(CommandDeckInput) with { Mode = CombatMode.NonCombat, SessionKey = 1 };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => now);
        deck.Refresh();
        DeckRect r = DeckGeometry.Default.RectFor(DeckZone.Recall0);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseMove, Data1: (int)(r.X + r.W / 2), Data2: (int)(r.Y + r.H / 2)));
        now = 2.0;

        static List<float> Verts(UiCommandDeck d)
        {
            var all = new List<float>();
            foreach (var s in MarkupDrawProbe.DrawOverlays(d).DebugSpriteSegmentVerts) all.AddRange(s.Verts);
            return all;
        }
        List<float> first = Verts(deck), second = Verts(deck);
        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void LongCastingTextIsShortenedToStopBeforeCast()
    {
        static float Measure(string text) => text.Length * 8f;
        float room = DeckGeometry.Default.Cast.X - DeckGeometry.Default.CastingText.X - 6f;

        float width = DeckGeometry.Default.CastingText.W;

        string fitted = UiCommandDeck.FitCastingText("Incantation of Coordination", Measure, width);

        Assert.EndsWith("...", fitted);
        Assert.True(Measure(fitted) <= room);
        Assert.Equal("Flame Bolt", UiCommandDeck.FitCastingText("Flame Bolt", Measure, width));
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void JoinedBelowTheSpellDeck_DrawsSquareTopCorners_OtherwiseRounded(float scale)
    {
        var input = default(CommandDeckInput) with { Mode = CombatMode.Magic, SessionKey = 1 };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();
        deck.ApplyGeometry(DeckGeometry.Create(scale, 1f));

        Assert.False(deck.SquareTopCorners);
        Assert.False(TopLeftCornerFilled(deck));

        bool shown = true;
        deck.JoinedAbove = () => shown;
        Assert.True(deck.SquareTopCorners);
        Assert.True(TopLeftCornerFilled(deck));

        shown = false;   // attached, but the Spell Deck is hidden (peace, melee, missile)
        Assert.False(deck.SquareTopCorners);
        Assert.False(TopLeftCornerFilled(deck));

        deck.JoinedAbove = null;
        Assert.False(deck.SquareTopCorners);
    }

    /// <summary>Whether the frame's dark edge reaches into its very top-left corner (an opaque
    /// vertex within the anti-aliasing band of the corner point).</summary>
    private static bool TopLeftCornerFilled(UiCommandDeck deck)
    {
        TextRenderer renderer = MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);
        DeckRect f = deck.Geometry.Frame;
        float cx = f.X - 2f * deck.Geometry.Scale, cy = f.Y - 2f * deck.Geometry.Scale;
        System.Numerics.Vector4 c = DeckChrome.FrameEdge;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u) continue;
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                if (s.Verts[i + 7] <= 0f || MathF.Abs(s.Verts[i + 4] - c.X) > 0.01f
                    || MathF.Abs(s.Verts[i + 5] - c.Y) > 0.01f || MathF.Abs(s.Verts[i + 6] - c.Z) > 0.01f)
                    continue;
                if (MathF.Abs(s.Verts[i] - cx) < 0.75f && MathF.Abs(s.Verts[i + 1] - cy) < 0.75f) return true;   // the solid vertex half a pixel in
            }
        }
        return false;
    }

    [Fact]
    public void CastingRowDrawsTheCastWordInTheSmallSemiBoldFace()
    {
        var baked = new List<(float Px, BundledUiFontWeight Weight)>();
        var fonts = new DeckFonts((px, weight) => { baked.Add((px, weight)); return null!; });
        var input = default(CommandDeckInput) with { Mode = CombatMode.Magic, SessionKey = 1, OutlinedSpellName = "Flame Bolt" };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0, fonts);
        deck.Refresh();
        Assert.Equal(DeckContextRow.Casting, deck.View.Row);

        MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);

        Assert.Contains((DeckFonts.SmallPixelHeight, BundledUiFontWeight.SemiBold), baked);
    }
}
