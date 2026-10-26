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
}
