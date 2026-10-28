using AcDream.Core.Combat;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Targeting;
using Silk.NET.Input;
using static AcDream.App.Tests.UI.Hud.TargetDeckModels;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiTargetDeckTests
{
    private static (UiTargetDeck Deck, Recorder Actions) Make(TargetPlateModel? model, Func<double>? clock = null)
    {
        var actions = new Recorder();
        var deck = new UiTargetDeck(() => model, actions, icons: null, clock: clock ?? (() => 0.0));
        deck.Refresh();
        return (deck, actions);
    }

    private static bool Down(UiTargetDeck d, float x, float y) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseDown, Data1: (int)x, Data2: (int)y));

    private static bool Down(UiTargetDeck d, DeckRect r) => Down(d, r.X + r.W / 2, r.Y + r.H / 2);

    private static void Move(UiTargetDeck d, DeckRect r) =>
        d.OnEvent(new UiEvent(0, d, UiEventType.MouseMove, Data1: (int)(r.X + r.W / 2), Data2: (int)(r.Y + r.H / 2)));

    [Fact]
    public void ANullModelHitsNothing()
    {
        var (deck, _) = Make(null);
        Assert.False(deck.HitTestLocal(170, 50));
        Assert.False(deck.HitTestLocal(5, 5));
    }

    [Fact]
    public void AMonsterHitsItsFirstStud()
    {
        var (deck, _) = Make(Build(Monster()));
        DeckRect s = TargetDeckGeometry.Stud(0);
        Assert.True(deck.HitTestLocal(s.X + 2, s.Y + 2));
        Assert.True(deck.CapturesPointerDrag);
        Assert.True(deck.HitTestLocal(100, 30));
        Assert.False(deck.CapturesPointerDrag);   // the plate body drags the window
    }

    [Fact]
    public void PressingAssessRunsIt()
    {
        TargetPlateModel model = Build(Monster());
        var (deck, actions) = Make(model);
        Assert.True(Down(deck, TargetDeckGeometry.Stud(0)));
        Assert.Equal([$"run {TargetAction.Assess} {model.ObjectId}"], actions.Calls);
    }

    [Fact]
    public void PressingTheCloseCrossDeselects()
    {
        TargetPlateModel model = Build(Monster());
        var (deck, actions) = Make(model);
        DeckRect x = TargetDeckGeometry.Close;
        Assert.True(deck.HitTestLocal(x.X + x.W / 2, x.Y + x.H / 2));
        Assert.True(deck.CapturesPointerDrag);   // a press on the cross never drags the window
        Assert.True(Down(deck, x));
        Assert.Equal([$"run {TargetAction.Deselect} {model.ObjectId}"], actions.Calls);
        Assert.Equal("Deselect", UiTargetDeck.TooltipFor(TargetZone.Close, model)!.Title);
    }

    [Fact]
    public void TheCloseCrossSitsAtTheEndOfTheNameLineClearOfTheStuds()
    {
        DeckRect x = TargetDeckGeometry.Close, n = TargetDeckGeometry.Name;
        Assert.True(x.X + x.W <= n.X + n.W + 0.01f);
        Assert.True(x.Y >= n.Y && x.Y + x.H <= n.Y + n.H + 0.01f);
        for (int i = 0; i < TargetDeckGeometry.StudCount; i++)
        {
            DeckRect s = TargetDeckGeometry.Stud(i);
            Assert.True(x.X + x.W <= s.X || x.Y + x.H <= s.Y || s.Y + s.H <= x.Y);
        }
    }

    [Fact]
    public void ADisabledStudTakesThePressButRunsNothing()
    {
        TargetPlateModel model = Build(Player());
        Assert.Equal(TargetAction.Give, model.Studs[1].Action);
        Assert.False(model.Studs[1].Enabled);
        var (deck, actions) = Make(model);
        Assert.True(Down(deck, TargetDeckGeometry.Stud(1)));
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public void TheSizeIsFixedForEveryKind()
    {
        foreach (var o in EveryKind())
        {
            var (deck, _) = Make(Build(o));
            Assert.Equal(TargetDeckGeometry.Width, deck.Width);
            Assert.Equal(TargetDeckGeometry.Height, deck.Height);
            Assert.False(deck.Resizable);
        }
        var options = UiTargetDeck.MountOptions(0, 0);
        Assert.False(options.Resizable);
        Assert.Equal(WindowNames.TargetDeck, options.WindowName);
    }

    [Fact]
    public void EveryActionHasALabelAndAnIcon()
    {
        foreach (TargetAction a in Enum.GetValues<TargetAction>())
        {
            if (a == TargetAction.None) continue;
            Assert.False(string.IsNullOrEmpty(UiTargetDeck.LabelFor(a)));
            Assert.Contains(UiTargetDeck.IconFor(a), HudIcons.All);
        }
        Assert.Equal("Assess again", UiTargetDeck.LabelFor(TargetAction.Assess));
        Assert.Equal("Use selected key or lockpick", UiTargetDeck.LabelFor(TargetAction.UseKey));
    }

    private static readonly TargetPluginAction[] PluginActions =
    [
        new("lootbot", "Loot Bot", "mark", "Mark for loot", true),
        new("lootbot", "Loot Bot", "skip", "Skip", false),
    ];

    [Fact]
    public void MoreOpensAMenuOfTheMoreActionsThenThePluginActions()
    {
        TargetPlateModel model = Build(Monster(), pluginActions: PluginActions);
        var (deck, actions) = Make(model);
        Assert.True(Down(deck, TargetDeckGeometry.Stud(3)));
        Assert.True(deck.MenuOpen);
        Assert.Empty(actions.Calls);

        var entries = deck.MenuEntries;
        Assert.Equal(["Examine", "Copy name", "Loot Bot", "Mark for loot", "Skip"], entries.Select(e => e.Label));
        Assert.True(entries[2].Header);

        DeckRect copy = deck.MenuEntryRect(1);
        Assert.True(copy.Y > TargetDeckGeometry.Height);
        Assert.Same(deck, deck.HitTest(copy.X + 4, copy.Y + 4));   // the root's popup hit test, past the plate's bounds
        Assert.True(Down(deck, copy));
        Assert.Equal([$"run {TargetAction.CopyName} {model.ObjectId}"], actions.Calls);
        Assert.False(deck.MenuOpen);
    }

    [Fact]
    public void APluginEntryRunsThePluginAction_ADisabledOneDoesNothing()
    {
        TargetPlateModel model = Build(Monster(), pluginActions: PluginActions);
        var (deck, actions) = Make(model);
        Down(deck, TargetDeckGeometry.Stud(3));
        Assert.True(Down(deck, deck.MenuEntryRect(4)));   // Skip, disabled
        Assert.True(Down(deck, deck.MenuEntryRect(2)));   // the header
        Assert.Empty(actions.Calls);
        Assert.True(deck.MenuOpen);

        Down(deck, deck.MenuEntryRect(3));
        Assert.Equal([$"plugin lootbot {model.ObjectId} mark"], actions.Calls);
        Assert.False(deck.MenuOpen);
    }

    [Fact]
    public void EscapeOrAPressElsewhereClosesTheMenu()
    {
        var (deck, actions) = Make(Build(Monster()));
        Down(deck, TargetDeckGeometry.Stud(3));
        deck.OnEvent(new UiEvent(0, deck, UiEventType.KeyDown, Data0: (int)Key.Escape));
        Assert.False(deck.MenuOpen);

        Down(deck, TargetDeckGeometry.Stud(3));
        Assert.True(Down(deck, 100, 30));
        Assert.False(deck.MenuOpen);
        Assert.Empty(actions.Calls);

        Down(deck, TargetDeckGeometry.Stud(3));
        Down(deck, TargetDeckGeometry.Stud(3));   // More again toggles it shut
        Assert.False(deck.MenuOpen);

        Down(deck, TargetDeckGeometry.Stud(3));
        deck.OnHidden();
        Assert.False(deck.MenuOpen);
    }

    [Fact]
    public void InterleavedPluginActionsAreGroupedUnderOneHeaderEach()
    {
        TargetPlateModel model = Build(Monster(), pluginActions:
        [
            new("a", "Plugin A", "one", "A one", true),
            new("b", "Plugin B", "two", "B two", true),
            new("a", "Plugin A", "three", "A three", true),
        ]);
        var (deck, _) = Make(model);
        Assert.Equal(["Examine", "Copy name", "Plugin A", "A one", "A three", "Plugin B", "B two"],
            deck.MenuEntries.Select(e => e.Label));
        Assert.Equal(2, deck.MenuEntries.Count(e => e.Header));
    }

    [Fact]
    public void TheMenuEntriesAreBuiltOncePerModel()
    {
        TargetPlateModel model = Build(Monster());
        var (deck, _) = Make(model);
        Assert.Same(deck.MenuEntries, deck.MenuEntries);
    }

    [Fact]
    public void AnEqualRebuiltModelKeepsTheMenuEntries_AChangedOneRebuildsThem()
    {
        TargetPluginAction[] actions = [new("a", "Plugin A", "one", "A one", true)];
        TargetPlateModel model = Build(Monster(), pluginActions: actions);
        var deck = new UiTargetDeck(() => model, new Recorder(), icons: null, clock: () => 0.0);
        deck.Refresh();
        IReadOnlyList<TargetMenuEntry> first = deck.MenuEntries;

        // Built afresh each frame, as the runtime does: equal content keeps the cache.
        model = Build(Monster(), pluginActions: [new("a", "Plugin A", "one", "A one", true)]);
        deck.Refresh();
        Assert.Same(first, deck.MenuEntries);

        model = Build(Monster(), pluginActions: [new("a", "Plugin A", "one", "A one", false)]);
        deck.Refresh();
        Assert.NotSame(first, deck.MenuEntries);
        Assert.False(deck.MenuEntries[^1].Enabled);
    }

    [Theory]
    [InlineData(900f, false)]
    [InlineData(450f, true)]
    public void TheMenuOpensUpwardWhenThereIsNoRoomBelow(float rootHeight, bool upward)
    {
        TargetPlateModel model = Build(Monster(), pluginActions: PluginActions);
        var actions = new Recorder();
        var root = new UiRoot { Width = 800, Height = rootHeight };
        var deck = new UiTargetDeck(() => model, actions, icons: null, clock: () => 0.0) { Left = 200f, Top = 300f };
        root.AddChild(deck);
        deck.Refresh();

        Down(deck, TargetDeckGeometry.Stud(3));
        Assert.True(deck.MenuOpen);
        DeckRect first = deck.MenuEntryRect(0), last = deck.MenuEntryRect(deck.MenuEntries.Count - 1);
        if (upward) Assert.True(last.Y + last.H < 0f);
        else Assert.True(first.Y > TargetDeckGeometry.Height);
        Assert.InRange(deck.Top + first.Y, 0f, rootHeight);
        Assert.InRange(deck.Top + last.Y + last.H, 0f, rootHeight);

        Assert.Same(deck, deck.HitTest(first.X + 4, first.Y + 4));
        Down(deck, first);
        Assert.Equal([$"run {TargetAction.Examine} {model.ObjectId}"], actions.Calls);
        Assert.False(deck.MenuOpen);
    }

    [Fact]
    public void TheMenuClosesWhenTheTargetGoesAway()
    {
        TargetPlateModel? model = Build(Monster());
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 0.0);
        deck.Refresh();
        Down(deck, TargetDeckGeometry.Stud(3));
        model = null;
        deck.Refresh();
        Assert.False(deck.MenuOpen);
    }

    [Fact]
    public void AStudTooltipShowsAfterTheDwell_WithTheDisabledReason()
    {
        double now = 1.0;
        TargetPlateModel model = Build(Player());
        var (deck, _) = Make(model, () => now);
        Move(deck, TargetDeckGeometry.Stud(1));
        Assert.Null(deck.VisibleTooltip);
        now += UiCommandDeck.TooltipDwell + 0.01;
        HudTooltipContent tip = deck.VisibleTooltip!;
        Assert.Equal("Give selected item", tip.Title);
        Assert.Equal(model.Studs[1].DisabledReason, tip.Detail);

        Move(deck, TargetDeckGeometry.Stud(0));
        now += UiCommandDeck.TooltipDwell + 0.01;
        Assert.Equal("Trade", deck.VisibleTooltip!.Title);
        Assert.Null(deck.VisibleTooltip!.Detail);
    }

    [Fact]
    public void VitalsShowTotalsWhenTheyAreKnown()
    {
        var combat = new CombatState();
        combat.OnCreatureProfile(7, new CombatState.CreatureVitals(80, 100, 50, 120, 25, 90));
        var log = new TargetAppraisalLog();
        log.Record(7, true, 0.8f, now: 49.0);
        TargetPlateModel model = Build(Monster(), log: log, combat: combat);
        Assert.Equal("80 / 100", UiTargetDeck.HealthText(model.Vitals!.Value));
        Assert.Equal("80 / 100", UiTargetDeck.TooltipFor(TargetZone.Health, model)!.State);
        Assert.Equal("50 / 120", UiTargetDeck.TooltipFor(TargetZone.Stamina, model)!.State);
        Assert.Equal("25 / 90", UiTargetDeck.TooltipFor(TargetZone.Mana, model)!.State);

        // Health stays live between assesses: a later health update scales the known maximum.
        combat.OnUpdateHealth(7, 0.5f);
        model = Build(Monster(), log: log, combat: combat);
        Assert.Equal("50 / 100", UiTargetDeck.HealthText(model.Vitals!.Value));
    }

    [Fact]
    public void StaminaAndManaBarsCarryTheirTotals()
    {
        var combat = new CombatState();
        combat.OnCreatureProfile(7, new CombatState.CreatureVitals(580, 580, 710, 710, 440, 440));
        var log = new TargetAppraisalLog();
        log.Record(7, true, 1f, now: 49.0);
        TargetVitals v = Build(Monster(), log: log, combat: combat).Vitals!.Value;
        Assert.Equal("710 / 710", UiTargetDeck.StaminaText(v));
        Assert.Equal("440 / 440", UiTargetDeck.ManaText(v));

        // A failed assess keeps showing the last known totals (drawn dimmed).
        log.Record(7, false, 1f, now: 49.5);
        v = Build(Monster(), log: log, combat: combat).Vitals!.Value;
        Assert.Equal(TargetVitalsState.Failed, v.State);
        Assert.Equal("710 / 710", UiTargetDeck.StaminaText(v));

        // Never assessed: no text on the bars.
        v = Build(Monster()).Vitals!.Value;
        Assert.Null(UiTargetDeck.StaminaText(v));
        Assert.Null(UiTargetDeck.ManaText(v));
    }

    [Fact]
    public void TheVitalBarsShareHealthsHeightAndPluginLinesFitBelowThem()
    {
        Assert.Equal(TargetDeckGeometry.Health.H, TargetDeckGeometry.Stamina.H);
        Assert.Equal(TargetDeckGeometry.Health.H, TargetDeckGeometry.Mana.H);
        Assert.True(TargetDeckGeometry.Stamina.Y >= TargetDeckGeometry.Health.Y + TargetDeckGeometry.Health.H);
        Assert.True(TargetDeckGeometry.Mana.Y >= TargetDeckGeometry.Stamina.Y + TargetDeckGeometry.Stamina.H);
        Assert.True(TargetDeckGeometry.PluginLine(0).Y >= TargetDeckGeometry.Mana.Y + TargetDeckGeometry.Mana.H);
        DeckRect last = TargetDeckGeometry.PluginLine(1);
        Assert.True(last.Y + last.H <= TargetDeckGeometry.Body.Y + TargetDeckGeometry.Body.H - 2f);
    }

    [Fact]
    public void HealthFallsBackToAPercentWithoutTotals()
    {
        var combat = new CombatState();
        combat.OnUpdateHealth(7, 0.42f);
        TargetPlateModel model = Build(Monster(), combat: combat);
        Assert.Equal("42%", UiTargetDeck.HealthText(model.Vitals!.Value));
    }

    [Fact]
    public void FailedStaminaWithAnEarlierValueSaysItIsTheLastKnown()
    {
        var combat = new CombatState();
        combat.OnCreatureProfile(7, new CombatState.CreatureVitals(80, 100, 50, 100, 25, 100));
        var log = new TargetAppraisalLog();
        log.Record(7, false, 0.8f, now: 49.0);
        TargetPlateModel model = Build(Monster(), log: log, combat: combat);
        foreach (TargetZone zone in new[] { TargetZone.Stamina, TargetZone.Mana })
        {
            HudTooltipContent tip = UiTargetDeck.TooltipFor(zone, model)!;
            Assert.Equal("Assess failed", tip.Title);
            Assert.Equal("Your assess failed; showing the last known value. Health is still live.", tip.Detail);
        }
    }

    [Fact]
    public void FailedStaminaWithoutAnEarlierValueKeepsTheFailedText()
    {
        var log = new TargetAppraisalLog();
        log.Record(7, false, 1f, now: 49.0);
        HudTooltipContent tip = UiTargetDeck.TooltipFor(TargetZone.Mana, Build(Monster(), log: log))!;
        Assert.Equal("Your assess skill check failed; health is still live.", tip.Detail);
    }

    [Fact]
    public void FailedStaminaSaysAssessFailed()
    {
        var log = new TargetAppraisalLog();
        log.Record(7, false, 1f, now: 49.0);
        TargetPlateModel model = Build(Monster(), log: log);
        HudTooltipContent tip = UiTargetDeck.TooltipFor(TargetZone.Stamina, model)!;
        Assert.Equal("Assess failed", tip.Title);
        Assert.Equal("Assess failed", UiTargetDeck.TooltipFor(TargetZone.Mana, model)!.Title);
    }

    [Fact]
    public void ThePluginRowTooltipIsTheRowsOwnThenTheOverflow()
    {
        TargetPlateModel model = Build(Monster(), pluginRows: [PluginRow], overflow: "Loot Bot: 3 more rows");
        Assert.Equal(PluginRow.Tooltip + "\nLoot Bot: 3 more rows", UiTargetDeck.TooltipFor(TargetZone.PluginRow0, model)!.Detail);
        model = Build(Monster(), pluginRows: [PluginRow with { Tooltip = null }], overflow: "Loot Bot: 3 more rows");
        Assert.Equal("Loot Bot: 3 more rows", UiTargetDeck.TooltipFor(TargetZone.PluginRow0, model)!.Detail);
        model = Build(Monster(), pluginRows: [PluginRow]);
        Assert.Equal(PluginRow.Tooltip, UiTargetDeck.TooltipFor(TargetZone.PluginRow0, model)!.Detail);
    }

    [Fact]
    public void TickedFiresAfterEachRefreshTick()
    {
        TargetPlateModel? seen = null;
        TargetPlateModel model = Build(Monster());
        var deck = new UiTargetDeck(() => model, new NoActions(), icons: null, clock: () => 0.0);
        int ticks = 0;
        deck.Ticked += () => { ticks++; seen = deck.View; };
        deck.TickSelfAndChildren(0.016);
        Assert.Equal(1, ticks);
        Assert.Same(model, seen);
    }
}
