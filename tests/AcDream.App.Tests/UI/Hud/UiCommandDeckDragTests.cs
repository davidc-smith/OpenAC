using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The deck mounted as a window the way <c>MountCommandDeck</c> does,
/// driven through a real <see cref="UiRoot"/>.</summary>
public sealed class UiCommandDeckDragTests
{
    private sealed class Recorder : ICommandDeckActions
    {
        public readonly List<string> Calls = [];
        public void ToggleCombat() => Calls.Add("toggle");
        public void PressAttack(AttackHeight height) => Calls.Add("press " + height);
        public void ReleaseAttack() => Calls.Add("release");
        public void SetDesiredPower(float power) => Calls.Add("power");
        public void SetOption(CharacterOptionId option, bool value) => Calls.Add("option " + option);
        public void Recall(RuntimePortalCommand command) => Calls.Add("recall " + command);
        public void Recast() => Calls.Add("recast");
    }

    private const float Left = 100f;
    private const float Top = 300f;

    private static (UiRoot Root, UiCommandDeck Deck, Recorder Actions) Mount()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        var actions = new Recorder();
        CommandDeckInput input = default(CommandDeckInput) with
        {
            Mode = CombatMode.Melee, DesiredPower = 0.5f, SessionKey = 1,
        };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), actions, icons: null, clock: () => 0.0);
        deck.Refresh();
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0), new RetailWindowFrame.Options
        {
            WindowName = WindowNames.CommandDeck,
            Chrome = RetailWindowChrome.Imported,
            Left = Left,
            Top = Top,
            Resizable = false,
            ResizeX = false,
            ResizeY = false,
            ContentClickThrough = false,
        });
        return (root, deck, actions);
    }

    private static (int X, int Y) Centre(DeckRect r) =>
        ((int)(Left + r.X + r.W / 2), (int)(Top + r.Y + r.H / 2));

    [Fact]
    public void PressOnTheHealthBarDragsTheWindow()
    {
        var (root, deck, actions) = Mount();
        var (x, y) = Centre(CommandDeckLayout.HealthBar);

        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 10, y);
        root.OnMouseMove(x + 30, y);
        root.OnMouseUp(UiMouseButton.Left, x + 30, y);

        Assert.Equal(Left + 30f, deck.Left, 1f);
        Assert.Equal(Top, deck.Top, 1f);
        Assert.Empty(actions.Calls);
    }

    [Fact]
    public void PressOnMediumAttacksAndDoesNotMove()
    {
        var (root, deck, actions) = Mount();
        var (x, y) = Centre(CommandDeckLayout.HeightButton(1));

        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 30, y);
        root.OnMouseUp(UiMouseButton.Left, x + 30, y);

        Assert.Equal(Left, deck.Left, 1f);
        Assert.Equal(Top, deck.Top, 1f);
        Assert.Equal(["press Medium", "release"], actions.Calls);
    }
}
