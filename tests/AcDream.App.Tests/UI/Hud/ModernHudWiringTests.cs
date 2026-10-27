using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ModernHudWiringTests
{
    [Fact]
    public void SideVitalsStayHiddenWhileActive_AndComeBackAfter()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var stacked = new UiPanel { Width = 160, Height = 58 };
        var side = new UiPanel { Width = 460, Height = 26, Visible = false };
        var deck = new UiPanel { Width = 420, Height = 150, Visible = false };
        root.AddChild(stacked); root.AddChild(side); root.AddChild(deck);
        root.RegisterWindow(WindowNames.Vitals, stacked, stacked, null);
        root.RegisterWindow(WindowNames.SideVitals, side, side, null);
        root.RegisterWindow(WindowNames.CommandDeck, deck, deck, null);

        bool sideBySide = false;
        VitalsSideBySideController? vitals = null;
        var hud = new ModernHudSwitch(
            root.ShowWindow, root.HideWindow,
            isVisible: root.IsWindowVisible,
            gameplayActive: () => true,
            [
                new ModernHudReplacement(
                    "Command Deck", WindowNames.CommandDeck, ModernHudSwitch.CommandDeckRetailWindows,
                    Mounted: () => true,
                    OnShown: () => { },
                    OnHidden: () => { },
                    ResyncRetail: () => vitals!.SyncVisibility()),
            ]);
        vitals = new VitalsSideBySideController(
            hud.GatedShow, root.HideWindow, () => sideBySide,
            WindowNames.Vitals, WindowNames.SideVitals);
        vitals.Tick();

        hud.Apply(true);
        for (int i = 0; i < 4; i++)
        {
            sideBySide = !sideBySide;
            vitals.Tick();
            Assert.False(stacked.Visible);
            Assert.False(side.Visible);
        }
        Assert.True(deck.Visible);

        sideBySide = true;
        hud.Apply(false);
        Assert.False(deck.Visible);
        Assert.False(stacked.Visible);
        Assert.True(side.Visible);
    }

    private sealed class Routes
    {
        public readonly List<InputAction> Deck = [];
        public readonly List<InputAction> Retail = [];
        public readonly List<InputAction> Redispatched = [];
        public InputAction? Game;

        public bool Route(InputAction action, bool spellDeckActive) =>
            SpellActionRouter.Route(
                action,
                spellDeckActive,
                a => { Deck.Add(a); return true; },
                a => { Retail.Add(a); return true; },
                () => Game,
                Redispatched.Add);
    }

    [Fact]
    public void SpellDeckActive_SpellKeysReachTheSpellDeckNotTheRetailBar()
    {
        var r = new Routes();
        Assert.True(r.Route(InputAction.UseSpellSlot_1, spellDeckActive: true));
        Assert.True(r.Route(InputAction.UseSpellRow3Slot1, spellDeckActive: true));
        Assert.True(r.Route(InputAction.CastItemSpell, spellDeckActive: true));
        Assert.Equal([InputAction.UseSpellSlot_1, InputAction.UseSpellRow3Slot1, InputAction.CastItemSpell], r.Deck);
        Assert.Empty(r.Retail);
        Assert.Empty(r.Redispatched);
    }

    [Fact]
    public void SpellDeckInactive_RetailActionsKeepTheRetailBar()
    {
        var r = new Routes();
        Assert.True(r.Route(InputAction.UseSpellSlot_1, spellDeckActive: false));
        Assert.True(r.Route(InputAction.CombatNextSpell, spellDeckActive: false));
        Assert.Equal([InputAction.UseSpellSlot_1, InputAction.CombatNextSpell], r.Retail);
        Assert.Empty(r.Deck);
    }

    [Fact]
    public void SpellDeckInactive_ClientOnlyAction_RedispatchesTheGameKey()
    {
        var r = new Routes { Game = InputAction.UseQuickSlot_1 };
        Assert.True(r.Route(InputAction.UseSpellRow3Slot1, spellDeckActive: false));
        Assert.True(r.Route(InputAction.CastItemSpell, spellDeckActive: false));
        Assert.Equal([InputAction.UseQuickSlot_1, InputAction.UseQuickSlot_1], r.Redispatched);
        Assert.Empty(r.Retail);
        Assert.Empty(r.Deck);
    }

    [Fact]
    public void SpellDeckInactive_ClientOnlyActionWithNoGameKey_DoesNothing()
    {
        var r = new Routes();
        Assert.True(r.Route(InputAction.UseSpellRow2Slot4, spellDeckActive: false));
        r.Game = InputAction.UseSpellRow2Slot1;   // never loops back to a client-only action
        Assert.True(r.Route(InputAction.UseSpellRow2Slot4, spellDeckActive: false));
        Assert.Empty(r.Redispatched);
        Assert.Empty(r.Retail);
    }

    [Theory]
    [InlineData(InputAction.CombatPrevSpellTab)]
    [InlineData(InputAction.CombatNextSpellTab)]
    [InlineData(InputAction.CombatFirstSpellTab)]
    [InlineData(InputAction.CombatLastSpellTab)]
    public void TabKeys_ReachTheSpellDeckOnlyWhileActive(InputAction action)
    {
        var r = new Routes { Game = InputAction.UseQuickSlot_1 };
        Assert.True(r.Route(action, spellDeckActive: true));
        Assert.True(r.Route(action, spellDeckActive: false));
        Assert.Equal([action], r.Deck);
        Assert.Equal([action], r.Retail);
        Assert.Empty(r.Redispatched);   // a retail action is never re-sent as a Game key
    }

    private sealed class NoServer : ISpellDeckActions
    {
        public readonly List<string> Calls = [];
        public void CastSpell(uint spellId) => Calls.Add($"cast {spellId}");
        public void CastItemSpell(uint itemId) => Calls.Add($"item {itemId}");
        public void SetFavorite(int tab, int position, uint spellId) => Calls.Add($"set {tab} {spellId}");
        public void RemoveFavorite(int tab, uint spellId) => Calls.Add($"remove {tab} {spellId}");
        public void ExamineSpell(uint spellId) { }
        public void ShowMessage(string text) { }
    }

    /// <summary>The runtime's routing over a real controller with one local tab (tab 9) holding 77.</summary>
    private static (SpellDeckController Deck, NoServer Server, List<InputAction> Retail, Func<InputAction, bool, bool> Route)
        LocalTabDeck()
    {
        var server = new NoServer();
        var deck = new SpellDeckController(
            server, _ => [], () => null, () => 12,
            SpellDeckSettings.Default with { Tab = 8, LocalTabs = [[77u]] },
            _ => { }, () => false, () => true);
        deck.Tick();
        var retail = new List<InputAction>();
        return (deck, server, retail, (action, active) => SpellActionRouter.Route(
            action, active, a => deck.Handle(a), a => { retail.Add(a); return true; }, () => null, _ => { }));
    }

    [Fact]
    public void ActiveSpellDeck_TabKeysWrapOverTheLocalTabs_AndSlotKeysCastFromThem()
    {
        var (deck, server, retail, route) = LocalTabDeck();

        Assert.True(route(InputAction.UseSpellSlot_1, true));
        Assert.Equal(["cast 77"], server.Calls);
        Assert.True(route(InputAction.CombatNextSpellTab, true));    // PgUp from the last tab wraps to 1
        Assert.Equal(0, deck.Tab);
        Assert.True(route(InputAction.CombatPrevSpellTab, true));    // Insert wraps back to the local tab
        Assert.Equal(8, deck.Tab);
        Assert.True(route(InputAction.CombatFirstSpellTab, true));
        Assert.Equal(0, deck.Tab);
        Assert.True(route(InputAction.CombatLastSpellTab, true));
        Assert.Equal(8, deck.Tab);
        Assert.Empty(retail);
    }

    [Fact]
    public void InactiveSpellDeck_TabAndSlotKeysLeaveItAlone()
    {
        var (deck, server, retail, route) = LocalTabDeck();

        Assert.True(route(InputAction.CombatNextSpellTab, false));
        Assert.True(route(InputAction.UseSpellSlot_1, false));

        Assert.Equal(8, deck.Tab);
        Assert.Empty(server.Calls);
        Assert.Equal([InputAction.CombatNextSpellTab, InputAction.UseSpellSlot_1], retail);
    }

    [Fact]
    public void SpellbookAdd_OnALocalTab_SavesLocally_AndNeverReachesTheServer()
    {
        var server = new NoServer();
        var saved = new List<SpellDeckSettings>();
        var deck = new SpellDeckController(
            server, _ => [], () => null, () => 12,
            SpellDeckSettings.Default with { Tab = 8, LocalTabs = [[77u]] },
            saved.Add, () => false, () => true);

        deck.AddFavoriteFromSpellbook(78u);

        Assert.Empty(server.Calls);
        Assert.Equal([77u, 78u], Assert.Single(saved).LocalTabs[0]);
    }

    [Fact]
    public void ClientOnly_IsRowsTwoToTenAndTheItemSpell()
    {
        Assert.False(SpellActionRouter.IsClientOnly(InputAction.UseSpellSlot_12));
        Assert.False(SpellActionRouter.IsClientOnly(InputAction.UseQuickSlot_1));
        Assert.True(SpellActionRouter.IsClientOnly(SpellBarActions.ForSlot(1, 0)));
        Assert.True(SpellActionRouter.IsClientOnly(SpellBarActions.ForSlot(9, 11)));
        Assert.True(SpellActionRouter.IsClientOnly(InputAction.CastItemSpell));
    }

    private static readonly KeyChord Ctrl1 = new(Key.Number1, ModifierMask.Ctrl);

    private static KeyBindings Ctrl1Bindings()
    {
        var b = new KeyBindings();
        b.Add(new(Ctrl1, InputAction.UseSpellRow3Slot1, Scope: InputScope.MagicCombat));
        b.Add(new(Ctrl1, InputAction.UseQuickSlot_1));
        return b;
    }

    [Fact]
    public void GameActionOnSameKey_FindsTheGameActionOnTheFiringKey()
    {
        Assert.Equal(InputAction.UseQuickSlot_1,
            SpellActionRouter.GameActionOnSameKey(Ctrl1Bindings(), Ctrl1, InputAction.UseSpellRow3Slot1));
    }

    [Fact]
    public void GameActionOnSameKey_NullForAnotherKeyOrNoKey()
    {
        KeyBindings b = Ctrl1Bindings();
        b.Add(new(new KeyChord(Key.Number2, ModifierMask.Ctrl), InputAction.UseQuickSlot_2));
        // A stale key not bound to the action that fired.
        Assert.Null(SpellActionRouter.GameActionOnSameKey(
            b, new KeyChord(Key.Number2, ModifierMask.Ctrl), InputAction.UseSpellRow3Slot1));
        Assert.Null(SpellActionRouter.GameActionOnSameKey(b, null, InputAction.UseSpellRow3Slot1));
        // The firing key with no Game-scope binding.
        var only = new KeyBindings();
        only.Add(new(Ctrl1, InputAction.UseSpellRow3Slot1, Scope: InputScope.MagicCombat));
        Assert.Null(SpellActionRouter.GameActionOnSameKey(only, Ctrl1, InputAction.UseSpellRow3Slot1));
    }
}
