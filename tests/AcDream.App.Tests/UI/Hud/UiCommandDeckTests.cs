using System.Globalization;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiCommandDeckTests
{
    private sealed class Recorder : ICommandDeckActions
    {
        public readonly List<string> Calls = [];
        public void ToggleCombat() => Calls.Add("toggle");
        public void PressAttack(AttackHeight height) => Calls.Add("press " + height);
        public void ReleaseAttack() => Calls.Add("release");
        public void SetDesiredPower(float power) => Calls.Add(string.Create(CultureInfo.InvariantCulture, $"power {power:0.00}"));
        public void SetOption(CharacterOptionId option, bool value) => Calls.Add($"option {option} {value}");
        public void Recall(RuntimePortalCommand command) => Calls.Add("recall " + command);
        public void Recast() => Calls.Add("recast");
    }

    private static CommandDeckInput Input(CombatMode mode, string? last = null) =>
        default(CommandDeckInput) with
        {
            Mode = mode, DesiredPower = 0.5f, LastSpellName = last, SessionKey = 1,
            RepeatAttacks = true,
        };

    private static (UiCommandDeck Deck, Recorder Actions, Action<CommandDeckInput> Set) Make(CombatMode mode, string? last = null)
    {
        var model = new CommandDeckModel();
        CommandDeckInput input = Input(mode, last);
        var actions = new Recorder();
        var deck = new UiCommandDeck(() => input, model, actions, icons: null, clock: () => 0.0);
        deck.Refresh();
        return (deck, actions, next => { input = next; deck.Refresh(); });
    }

    private static void Down(UiCommandDeck d, DeckRect r) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseDown, Data1: (int)(r.X + r.W / 2), Data2: (int)(r.Y + r.H / 2)));

    private static void Up(UiCommandDeck d, float x, float y) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseUp, Data1: (int)x, Data2: (int)y));

    [Fact]
    public void OrbTogglesCombat()
    {
        var (deck, actions, _) = Make(CombatMode.NonCombat);
        Down(deck, CommandDeckLayout.Orb);
        Assert.Equal(["toggle"], actions.Calls);
    }

    [Fact]
    public void StudFlipsItsOption()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.Stud(0));
        Down(deck, CommandDeckLayout.Stud(1));
        Assert.Equal(
            [$"option {CharacterOptionId.AutoRepeatAttack} False", $"option {CharacterOptionId.AutoTarget} True"],
            actions.Calls);
    }

    [Fact]
    public void PowerTrackPressDragAndWheel()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        DeckRect t = CommandDeckLayout.PowerTrack;
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseDown, Data1: (int)(t.X + t.W / 4), Data2: (int)(t.Y + 2)));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseMove, Data1: (int)(t.X + t.W + 50), Data2: (int)(t.Y + 2)));
        Up(deck, t.X + t.W + 50, t.Y + 2);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.Scroll, Data0: 1, Data1: (int)(t.X + 5), Data2: (int)(t.Y + 2)));

        Assert.Equal("power 0.25", actions.Calls[0][..10]);
        Assert.Equal("power 1.00", actions.Calls[1]);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"power {0.5f + RuntimeCombatAttackState.DesiredPowerStep:0.00}"), actions.Calls[^1]);
    }

    [Fact]
    public void AttackReleasedOnMouseUpInside()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        DeckRect m = CommandDeckLayout.HeightButton(1);
        Down(deck, m);
        Up(deck, m.X + 2, m.Y + 2);
        deck.ReleaseHeldAttack();
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnMouseUpOutside()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(0));
        Up(deck, -50, -50);
        Assert.Equal(["press High", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnCaptureLost()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(2));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.CaptureChanged));
        Up(deck, 0, 0);
        Assert.Equal(["press Low", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedWhenTheRowLeavesPower()
    {
        var (deck, actions, set) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(1));
        set(Input(CombatMode.Magic));
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnDetach()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, CommandDeckLayout.HeightButton(1));
        deck.Detach();
        deck.Detach();
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void RecastOnlyWhenEnabled()
    {
        var (none, noneActions, _) = Make(CombatMode.Magic);
        Down(none, CommandDeckLayout.Recast);
        Assert.Empty(noneActions.Calls);

        var (deck, actions, _) = Make(CombatMode.Magic, last: "Flame Bolt I");
        Down(deck, CommandDeckLayout.Recast);
        Assert.Equal(["recast"], actions.Calls);
    }

    [Fact]
    public void RecallStudsInOrder()
    {
        var (deck, actions, _) = Make(CombatMode.NonCombat);
        for (int i = 0; i < 5; i++) Down(deck, CommandDeckLayout.RecallStud(i));
        Assert.Equal(
        [
            "recall RecallLifestone", "recall RecallMarketplace", "recall RecallHouse",
            "recall RecallMansion", "recall RecallAllegiance",
        ], actions.Calls);
    }

    [Fact]
    public void OnlyTheCornersOutsideFrameAndOrbFallThrough()
    {
        var (deck, _, _) = Make(CombatMode.Melee);
        DeckRect f = CommandDeckLayout.Frame;
        Assert.False(deck.HitTestLocal(2, 4));

        // The frame body hits (so the window can be dragged by it) but is not a control.
        Assert.True(deck.HitTestLocal(f.X + f.W - 4, f.Y + 4));
        Assert.False(deck.HandlesClick);
        Assert.False(deck.CapturesPointerDrag);

        Assert.True(deck.HitTestLocal(CommandDeckLayout.Orb.X + 46, CommandDeckLayout.Orb.Y + 46));
        Assert.True(deck.HandlesClick);
        Assert.True(deck.CapturesPointerDrag);
    }

    [Fact]
    public void EveryLiveZoneHasATooltip()
    {
        foreach (DeckZone zone in Enum.GetValues<DeckZone>())
        {
            if (zone == DeckZone.None) continue;
            Assert.False(string.IsNullOrWhiteSpace(UiCommandDeck.TooltipFor(zone, CommandDeckView.Empty)), zone.ToString());
        }
    }
}
