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
        public bool Gameplay = true;
        public int Resyncs;
        public int DeckHidden;
        public ModernHudSwitch Switch = null!;

        public Harness(bool withChat = false)
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
            Switch = new ModernHudSwitch(
                show: n => Visible.Add(n),
                hide: n => Visible.Remove(n),
                isVisible: Visible.Contains,
                gameplayActive: () => Gameplay,
                replacements,
                holdVisibility: n => Held.TryAdd(n, Visible.Contains(n)),
                releaseVisibility: n => Held.Remove(n, out bool v) && v,
                log: Log.Add);
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
}
