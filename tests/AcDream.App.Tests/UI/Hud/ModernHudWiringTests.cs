using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;

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
}
