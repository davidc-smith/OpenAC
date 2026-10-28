using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ModernHudSwitchTests
{
    private sealed class Harness
    {
        public readonly HashSet<string> Visible = [WindowNames.Vitals, WindowNames.Combat, WindowNames.Chat];
        public readonly List<string> Log = [];
        public readonly List<string> Calls = [];
        public readonly Dictionary<string, bool> Held = [];
        public bool Mounted = true;
        public bool ChatMounted = true;
        public bool GearMounted = true;
        public bool Gameplay = true;
        public int Resyncs;
        public int DeckHidden;
        public bool Magic;
        public bool SpellMounted = true;
        public int SpellDeckHidden;
        public ModernHudSwitch Switch = null!;

        public Harness(bool withChat = false, bool withGear = false)
        {
            var replacements = new List<ModernHudReplacement>
            {
                new("Command Deck", WindowNames.CommandDeck, ModernHudSwitch.CommandDeckRetailWindows,
                    () => Mounted, OnShown: () => { }, OnHidden: () => DeckHidden++, ResyncRetail: () => Resyncs++),
            };
            if (withChat)
                replacements.Add(new("Chat Deck", WindowNames.ChatDeck, ModernHudSwitch.ChatDeckRetailWindows,
                    () => ChatMounted, OnShown: () => Calls.Add("chat shown"), OnHidden: () => Calls.Add("chat hidden"),
                    HoldsRetailVisibility: true));
            if (withGear)
                replacements.Add(new("Gear Deck", WindowNames.GearDeck, [WindowNames.Inventory],
                    () => GearMounted, OnShown: () => Calls.Add("gear shown"), OnHidden: () => Calls.Add("gear hidden"),
                    HoldsRetailVisibility: true, OnDemand: true));
            Switch = new ModernHudSwitch(
                show: n => Visible.Add(n),
                hide: n => Visible.Remove(n),
                isVisible: Visible.Contains,
                gameplayActive: () => Gameplay,
                replacements,
                holdVisibility: n => Held.TryAdd(n, Visible.Contains(n)),
                releaseVisibility: n => Held.Remove(n, out bool v) && v,
                log: Log.Add,
                spellDeckMounted: () => SpellMounted,
                magicMode: () => Magic,
                onSpellDeckHidden: () => SpellDeckHidden++);
        }
    }

    [Fact]
    public void On_HidesRetailAndShowsDeck()
    {
        var h = new Harness();
        h.Switch.Apply(true);

        Assert.True(h.Switch.Active);
        Assert.DoesNotContain(WindowNames.Vitals, h.Visible);
        Assert.DoesNotContain(WindowNames.Combat, h.Visible);
        Assert.Contains(WindowNames.CommandDeck, h.Visible);
    }

    [Fact]
    public void WhileOn_GatedShowsAreIgnored_HidesStillWork()
    {
        var h = new Harness();
        h.Switch.Apply(true);

        h.Switch.GateVisibility(WindowNames.Combat)(true);
        Assert.False(h.Switch.GatedShow(WindowNames.SideVitals));
        Assert.DoesNotContain(WindowNames.Combat, h.Visible);
        Assert.DoesNotContain(WindowNames.SideVitals, h.Visible);

        h.Visible.Add(WindowNames.JumpPowerbar);
        h.Switch.GateVisibility(WindowNames.JumpPowerbar)(false);
        Assert.DoesNotContain(WindowNames.JumpPowerbar, h.Visible);
    }

    [Fact]
    public void WhileOff_GatesPassThrough()
    {
        var h = new Harness();
        h.Switch.GateVisibility(WindowNames.JumpPowerbar)(true);
        Assert.True(h.Switch.GatedShow(WindowNames.SideVitals));
        Assert.Contains(WindowNames.JumpPowerbar, h.Visible);
        Assert.Contains(WindowNames.SideVitals, h.Visible);
    }

    [Fact]
    public void On_WithoutDeck_LeavesRetailAndLogsOnce()
    {
        var h = new Harness { };
        h.Mounted = false;
        h.Switch.Apply(true);
        h.Switch.Apply(false);
        h.Switch.Apply(true);

        Assert.False(h.Switch.Active);
        Assert.Contains(WindowNames.Vitals, h.Visible);
        Assert.Single(h.Log);
        Assert.Equal(0, h.Resyncs);
    }

    [Fact]
    public void Off_RestoresRetailAndHidesDeck()
    {
        var h = new Harness();
        h.Switch.Apply(true);
        h.Switch.Apply(false);

        Assert.False(h.Switch.Active);
        Assert.DoesNotContain(WindowNames.CommandDeck, h.Visible);
        Assert.Equal(1, h.Resyncs);
        Assert.Equal(1, h.DeckHidden);
    }

    [Fact]
    public void Off_WhenNeverOn_DoesNotResync()
    {
        var h = new Harness();
        h.Switch.Apply(false);
        Assert.Equal(0, h.Resyncs);
    }

    [Fact]
    public void Tick_FollowsGameplay()
    {
        var h = new Harness();
        h.Switch.Apply(true);

        h.Gameplay = false;
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.CommandDeck, h.Visible);
        Assert.Equal(1, h.DeckHidden);

        h.Gameplay = true;
        h.Switch.Tick();
        Assert.Contains(WindowNames.CommandDeck, h.Visible);
    }

    [Fact]
    public void RepeatedApplyIsANoOp()
    {
        var h = new Harness();
        h.Switch.Apply(true);
        h.Visible.Add(WindowNames.Vitals);   // something else showed it
        h.Switch.Apply(true);
        Assert.Contains(WindowNames.Vitals, h.Visible);
    }

    [Fact]
    public void SpellDeck_ShownOnlyInMagic()
    {
        var h = new Harness();
        h.Switch.Apply(true);
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
        h.Magic = true; h.Switch.Tick();
        Assert.Contains(WindowNames.SpellDeck, h.Visible);
        Assert.True(h.Switch.SpellDeckActive);
        h.Magic = false; h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
        Assert.Equal(1, h.SpellDeckHidden);
        h.Magic = true; h.Switch.Tick(); h.Switch.Apply(false);
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
        Assert.Equal(2, h.SpellDeckHidden);
        Assert.False(h.Switch.SpellDeckActive);
    }

    [Fact]
    public void SpellDeck_HiddenWhenLeavingTheWorld()
    {
        var h = new Harness { Magic = true };
        h.Switch.Apply(true);
        Assert.Contains(WindowNames.SpellDeck, h.Visible);
        h.Gameplay = false; h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
        Assert.DoesNotContain(WindowNames.CommandDeck, h.Visible);
        Assert.True(h.Switch.SpellDeckActive);
    }

    [Fact]
    public void SpellDeckNotMounted_DeckStillOn_SpellDeckInactive()
    {
        var h = new Harness { SpellMounted = false, Magic = true };
        h.Switch.Apply(true);
        h.Switch.Tick();
        Assert.True(h.Switch.Active);
        Assert.False(h.Switch.SpellDeckActive);
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
    }

    [Fact]
    public void SpellDeck_NeverShownWhileOff()
    {
        var h = new Harness { Magic = true };
        h.Switch.Tick();
        Assert.False(h.Switch.SpellDeckActive);
        Assert.DoesNotContain(WindowNames.SpellDeck, h.Visible);
        Assert.Equal(0, h.SpellDeckHidden);
    }

    [Fact]
    public void ChatDeck_HidesAndRestoresTheRetailChat()
    {
        var h = new Harness(withChat: true);
        h.Switch.Apply(true);
        Assert.DoesNotContain(WindowNames.Chat, h.Visible);
        Assert.Contains(WindowNames.ChatDeck, h.Visible);
        Assert.True(h.Switch.IsShown(WindowNames.ChatDeck));
        Assert.Equal(["chat shown"], h.Calls);
        Assert.True(h.Held[WindowNames.Chat]);
        Assert.False(h.Held[WindowNames.ChatWindow1]);

        h.Switch.Apply(false);
        Assert.Contains(WindowNames.Chat, h.Visible);
        Assert.DoesNotContain(WindowNames.ChatWindow1, h.Visible);
        Assert.DoesNotContain(WindowNames.ChatDeck, h.Visible);
        Assert.Equal(["chat shown", "chat hidden"], h.Calls);
        Assert.Empty(h.Held);
    }

    [Fact]
    public void ChatDeck_RetailChatShownWhileActive_IsHiddenAgain()
    {
        var h = new Harness(withChat: true);
        h.Switch.Apply(true);
        h.Visible.Add(WindowNames.ChatWindow2);
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.ChatWindow2, h.Visible);
    }

    [Fact]
    public void ChatDeckNotMounted_KeepsRetailChat_CommandDeckStillApplies()
    {
        var h = new Harness(withChat: true) { ChatMounted = false };
        h.Switch.Apply(true);
        Assert.True(h.Switch.Active);
        Assert.Contains(WindowNames.Chat, h.Visible);
        Assert.DoesNotContain(WindowNames.Vitals, h.Visible);
        Assert.Contains(WindowNames.CommandDeck, h.Visible);
        Assert.Single(h.Log);
        h.Visible.Remove(WindowNames.Chat);   // so the harness's show reports a change
        Assert.True(h.Switch.GatedShow(WindowNames.Chat));
        Assert.Contains(WindowNames.Chat, h.Visible);
    }

    [Fact]
    public void CommandDeckNotMounted_ChatDeckStillApplies()
    {
        var h = new Harness(withChat: true) { Mounted = false };
        h.Switch.Apply(true);
        Assert.Contains(WindowNames.Vitals, h.Visible);
        Assert.DoesNotContain(WindowNames.Chat, h.Visible);
        Assert.True(h.Switch.IsShown(WindowNames.ChatDeck));
        Assert.True(h.Switch.GatedShow(WindowNames.SideVitals));
    }

    [Fact]
    public void ChatDeck_FollowsGameplay()
    {
        var h = new Harness(withChat: true);
        h.Switch.Apply(true);
        h.Gameplay = false;
        h.Switch.Tick();
        Assert.False(h.Switch.IsShown(WindowNames.ChatDeck));
        Assert.DoesNotContain(WindowNames.Chat, h.Visible);
        h.Gameplay = true;
        h.Switch.Tick();
        Assert.Equal(["chat shown", "chat hidden", "chat shown"], h.Calls);
    }

    [Fact]
    public void OnDemand_StaysClosedUntilOpened()
    {
        var h = new Harness(withGear: true);
        h.Switch.Apply(true);
        Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);

        Assert.True(h.Switch.SetOpen(WindowNames.GearDeck, true));
        h.Switch.Tick();
        Assert.Contains(WindowNames.GearDeck, h.Visible);
        Assert.Contains("gear shown", h.Calls);

        h.Switch.ToggleOpen(WindowNames.GearDeck);
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
        Assert.False(h.Switch.IsOpen(WindowNames.GearDeck));
    }

    [Fact]
    public void OnDemand_HiddenOutsideGameplay_ReturnsAfter()
    {
        var h = new Harness(withGear: true);
        h.Switch.Apply(true);
        h.Switch.SetOpen(WindowNames.GearDeck, true);
        h.Gameplay = false;
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
        h.Gameplay = true;
        h.Switch.Tick();
        Assert.Contains(WindowNames.GearDeck, h.Visible);
    }

    [Fact]
    public void RedirectTarget_OnlyWhileInForce()
    {
        var h = new Harness(withGear: true);
        Assert.Null(h.Switch.RedirectTarget(WindowNames.Inventory));
        h.Switch.Apply(true);
        Assert.Equal(WindowNames.GearDeck, h.Switch.RedirectTarget(WindowNames.Inventory));
        Assert.Null(h.Switch.RedirectTarget(WindowNames.Chat));
        h.Switch.Apply(false);
        Assert.Null(h.Switch.RedirectTarget(WindowNames.Inventory));
    }

    [Fact]
    public void RedirectTarget_NullWhenDeckDidNotMount()
    {
        var h = new Harness(withGear: true);
        h.GearMounted = false;
        h.Switch.Apply(true);
        Assert.Null(h.Switch.RedirectTarget(WindowNames.Inventory));
    }

    [Fact]
    public void OnDemand_RetailShownAnyway_BecomesDeck()
    {
        var h = new Harness(withGear: true);
        h.Switch.Apply(true);
        h.Visible.Add(WindowNames.Inventory);      // e.g. the indicator bar opened it directly
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.Inventory, h.Visible);
        Assert.True(h.Switch.IsOpen(WindowNames.GearDeck));
        Assert.Contains(WindowNames.GearDeck, h.Visible);
    }

    [Fact]
    public void OnDemand_OffClosesDeck_ReleasesTheHold_WithoutReopeningRetail()
    {
        var h = new Harness(withGear: true);
        h.Visible.Add(WindowNames.Inventory);
        h.Switch.Apply(true);                      // holds "inventory was open"
        h.Switch.SetOpen(WindowNames.GearDeck, true);
        h.Switch.Tick();
        h.Switch.Apply(false);
        Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
        // The retail inventory would take the Options panel's shared slot, where the
        // player is turning Modern HUD off; it stays closed, and its hold is released.
        Assert.DoesNotContain(WindowNames.Inventory, h.Visible);
        Assert.Empty(h.Held);
        Assert.False(h.Switch.IsOpen(WindowNames.GearDeck));
    }

    [Fact]
    public void CloseAllOnDemand_ClosesGear()
    {
        var h = new Harness(withGear: true);
        h.Switch.Apply(true);
        h.Switch.SetOpen(WindowNames.GearDeck, true);
        h.Switch.CloseAllOnDemand();
        h.Switch.Tick();
        Assert.DoesNotContain(WindowNames.GearDeck, h.Visible);
    }

    [Fact]
    public void SetOpen_FalseForAlwaysOnReplacement()
    {
        var h = new Harness(withChat: true);
        h.Switch.Apply(true);
        Assert.False(h.Switch.SetOpen(WindowNames.ChatDeck, true));
    }
}
