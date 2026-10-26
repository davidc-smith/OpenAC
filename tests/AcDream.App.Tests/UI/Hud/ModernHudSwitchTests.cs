using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ModernHudSwitchTests
{
    private sealed class Harness
    {
        public readonly HashSet<string> Visible = [WindowNames.Vitals, WindowNames.Combat];
        public readonly List<string> Log = [];
        public bool Mounted = true;
        public bool Gameplay = true;
        public int Resyncs;
        public int DeckHidden;
        public ModernHudSwitch Switch = null!;

        public Harness()
        {
            Switch = new ModernHudSwitch(
                show: n => Visible.Add(n),
                hide: n => Visible.Remove(n),
                deckMounted: () => Mounted,
                gameplayActive: () => Gameplay,
                resyncRetail: () => Resyncs++,
                onDeckHidden: () => DeckHidden++,
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
}
