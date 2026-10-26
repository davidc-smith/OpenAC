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
        Down(deck, DeckGeometry.Default.Orb);
        Assert.Equal(["toggle"], actions.Calls);
    }

    [Fact]
    public void StudFlipsItsOption()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, DeckGeometry.Default.Stud(0));
        Down(deck, DeckGeometry.Default.Stud(1));
        Assert.Equal(
            [$"option {CharacterOptionId.AutoRepeatAttack} False", $"option {CharacterOptionId.AutoTarget} True"],
            actions.Calls);
    }

    [Fact]
    public void PowerTrackPressDragAndWheel()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        DeckRect t = DeckGeometry.Default.PowerTrack;
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseDown, Data1: (int)(t.X + t.W / 4), Data2: (int)(t.Y + 2)));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseMove, Data1: (int)(t.X + t.W + 50), Data2: (int)(t.Y + 2)));
        Up(deck, t.X + t.W + 50, t.Y + 2);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.Scroll, Data0: 1, Data1: (int)(t.X + 5), Data2: (int)(t.Y + 2)));

        Assert.Equal("power 0.25", actions.Calls[0][..10]);
        Assert.Equal("power 1.00", actions.Calls[1]);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"power {0.5f + RuntimeCombatAttackState.DesiredPowerStep:0.00}"), actions.Calls[^1]);
    }

    [Fact]
    public void ScaledControlsAreHitAtTheirScaledRects()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        DeckGeometry g = DeckGeometry.Create(1.5f, 2f);
        deck.ApplyGeometry(g);
        deck.Refresh();
        Assert.Same(g, deck.Geometry);

        for (int i = 0; i < 3; i++)
        {
            DeckRect r = g.HeightButton(i);
            Down(deck, r);
            Up(deck, r.X + r.W / 2, r.Y + r.H / 2);
        }
        Down(deck, g.PowerTrack);
        Up(deck, g.PowerTrack.X + g.PowerTrack.W / 2, g.PowerTrack.Y + g.PowerTrack.H / 2);

        Assert.Equal(
            ["press High", "release", "press Medium", "release", "press Low", "release", "power 0.50"],
            actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnMouseUpInside()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        DeckRect m = DeckGeometry.Default.HeightButton(1);
        Down(deck, m);
        Up(deck, m.X + 2, m.Y + 2);
        deck.ReleaseHeldAttack();
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnMouseUpOutside()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, DeckGeometry.Default.HeightButton(0));
        Up(deck, -50, -50);
        Assert.Equal(["press High", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnCaptureLost()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, DeckGeometry.Default.HeightButton(2));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.CaptureChanged));
        Up(deck, 0, 0);
        Assert.Equal(["press Low", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedWhenTheRowLeavesPower()
    {
        var (deck, actions, set) = Make(CombatMode.Melee);
        Down(deck, DeckGeometry.Default.HeightButton(1));
        set(Input(CombatMode.Magic));
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void AttackReleasedOnDetach()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, DeckGeometry.Default.HeightButton(1));
        deck.Detach();
        deck.Detach();
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }

    [Fact]
    public void RecastOnlyWhenEnabled()
    {
        var (none, noneActions, _) = Make(CombatMode.Magic);
        Down(none, DeckGeometry.Default.Recast);
        Assert.Empty(noneActions.Calls);

        var (deck, actions, _) = Make(CombatMode.Magic, last: "Flame Bolt I");
        Down(deck, DeckGeometry.Default.Recast);
        Assert.Equal(["recast"], actions.Calls);
    }

    [Fact]
    public void RecallStudsInOrder()
    {
        var (deck, actions, _) = Make(CombatMode.NonCombat);
        for (int i = 0; i < 5; i++) Down(deck, DeckGeometry.Default.RecallStud(i));
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
        DeckRect f = DeckGeometry.Default.Frame;
        Assert.False(deck.HitTestLocal(2, 4));

        // The frame body hits (so the window can be dragged by it) but is not a control.
        Assert.True(deck.HitTestLocal(f.X + f.W - 4, f.Y + 4));
        Assert.False(deck.HandlesClick);
        Assert.False(deck.CapturesPointerDrag);

        Assert.True(deck.HitTestLocal(DeckGeometry.Default.Orb.X + 46, DeckGeometry.Default.Orb.Y + 46));
        Assert.True(deck.HandlesClick);
        Assert.True(deck.CapturesPointerDrag);
    }

    [Fact]
    public void EveryLiveZoneHasATooltip()
    {
        Assert.Null(UiCommandDeck.TooltipFor(DeckZone.None, CommandDeckView.Empty));
        foreach (DeckZone zone in Enum.GetValues<DeckZone>())
        {
            if (zone == DeckZone.None) continue;
            HudTooltipContent? tip = UiCommandDeck.TooltipFor(zone, CommandDeckView.Empty);
            Assert.NotNull(tip);
            Assert.False(string.IsNullOrWhiteSpace(tip.Title), zone.ToString());
        }
    }

    [Fact]
    public void StudTooltipShowsItsState()
    {
        var (on, _, _) = Make(CombatMode.Melee);
        Assert.Equal("ON", UiCommandDeck.TooltipFor(DeckZone.StudRepeat, on.View)!.State);

        var model = new CommandDeckModel();
        var off = model.Update(Input(CombatMode.Melee) with { RepeatAttacks = false }, 0.0);
        Assert.Equal("OFF", UiCommandDeck.TooltipFor(DeckZone.StudRepeat, off)!.State);
    }

    private static void Move(UiCommandDeck d, DeckRect r) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseMove, Data1: (int)(r.X + r.W / 2), Data2: (int)(r.Y + r.H / 2)));

    [Fact]
    public void TooltipShowsAfterADwell()
    {
        double now = 10.0;
        CommandDeckInput input = Input(CombatMode.Melee);
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new Recorder(), icons: null, clock: () => now);
        deck.Refresh();

        Move(deck, DeckGeometry.Default.Stud(0));
        now = 10.3;
        Assert.Null(deck.VisibleTooltip);
        now = 10.5;
        Assert.Equal("Repeat attacks", deck.VisibleTooltip?.Title);

        // Moving within the same zone keeps it; a new zone starts a new dwell.
        Move(deck, DeckGeometry.Default.Stud(0));
        Assert.NotNull(deck.VisibleTooltip);
        Move(deck, DeckGeometry.Default.Stud(1));
        now = 10.9;
        Assert.Null(deck.VisibleTooltip);
        now = 10.96;
        Assert.Equal("Auto-target", deck.VisibleTooltip?.Title);

        // A press hides it until the zone changes.
        Down(deck, DeckGeometry.Default.Stud(1));
        now = 12.0;
        Assert.Null(deck.VisibleTooltip);
        Move(deck, DeckGeometry.Default.Stud(1));
        Assert.Null(deck.VisibleTooltip);
        Move(deck, DeckGeometry.Default.Stud(2));
        now = 13.0;
        Assert.Equal("Keep target in view", deck.VisibleTooltip?.Title);

        deck.OnEvent(new UiEvent(0, deck, UiEventType.HoverLeave));
        Assert.Null(deck.VisibleTooltip);
    }

    [Fact]
    public void HidingTheDeckForgetsTheHover()
    {
        double now = 10.0;
        CommandDeckInput input = Input(CombatMode.Melee);
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new Recorder(), icons: null, clock: () => now);
        deck.Refresh();
        Move(deck, DeckGeometry.Default.Stud(0));
        now = 11.0;
        Assert.NotNull(deck.VisibleTooltip);

        deck.OnHidden();
        now = 12.0;
        Assert.Null(deck.VisibleTooltip);

        Move(deck, DeckGeometry.Default.Stud(0));
        now = 13.0;
        Assert.NotNull(deck.VisibleTooltip);
        deck.Detach();
        Assert.Null(deck.VisibleTooltip);
    }

    [Fact]
    public void HidingTheDeckReleasesAHeldAttack()
    {
        var (deck, actions, _) = Make(CombatMode.Melee);
        Down(deck, DeckGeometry.Default.HeightButton(0));
        deck.OnHidden();
        Assert.Equal(["press High", "release"], actions.Calls);
    }

    [Theory]
    [InlineData(CombatMode.NonCombat, HudTooltipStyle.Card)]
    [InlineData(CombatMode.Melee, HudTooltipStyle.Compact)]
    [InlineData(CombatMode.Magic, HudTooltipStyle.Compact)]
    public void TooltipStyleFollowsTheMode(CombatMode mode, HudTooltipStyle style)
    {
        var (deck, _, _) = Make(mode);
        Assert.Equal(style, deck.TooltipStyle);
    }

    [Fact]
    public void EveryZoneHasAnAnchorInsideTheDeck()
    {
        foreach (DeckZone zone in Enum.GetValues<DeckZone>())
        {
            DeckRect r = DeckGeometry.Default.RectFor(zone);
            Assert.True(r.W > 0 && r.H > 0, zone.ToString());
            Assert.InRange(r.X, 0f, DeckGeometry.Default.Width);
            Assert.InRange(r.X + r.W, 0f, DeckGeometry.Default.Width);
        }
        Assert.Equal(DeckGeometry.Default.Stud(1), DeckGeometry.Default.RectFor(DeckZone.StudAutoTarget));
        Assert.Equal(DeckGeometry.Default.RecallStud(3), DeckGeometry.Default.RectFor(DeckZone.Recall3));
    }
}
