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
        public void Recast() { }
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
            LastSpellName = "Flame Bolt", RepeatAttacks = true,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 1.0);
        deck.Refresh();

        var renderer = MarkupDrawProbe.Draw(deck, CommandDeckLayout.Width, CommandDeckLayout.Height);

        int quads = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                quads++;
                float x = s.Verts[i], y = s.Verts[i + 1];
                Assert.InRange(x, -0.01f, CommandDeckLayout.Width + 0.01f);
                Assert.InRange(y, -0.01f, CommandDeckLayout.Height + 0.01f);
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

        DeckRect r = CommandDeckLayout.RectFor(zone);
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
        DeckRect r = CommandDeckLayout.RectFor(DeckZone.Recall0);
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
}
