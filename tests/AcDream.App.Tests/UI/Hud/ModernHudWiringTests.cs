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
            deckMounted: () => true,
            gameplayActive: () => true,
            resyncRetail: () => vitals!.SyncVisibility(),
            onDeckHidden: () => { });
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
