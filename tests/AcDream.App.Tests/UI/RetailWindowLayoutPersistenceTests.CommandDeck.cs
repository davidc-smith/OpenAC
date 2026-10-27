using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    private sealed class NoDeckActions : ICommandDeckActions
    {
        public void ToggleCombat() { }
        public void PressAttack(AttackHeight height) { }
        public void ReleaseAttack() { }
        public void SetDesiredPower(float power) { }
        public void SetOption(CharacterOptionId option, bool value) { }
        public void Recall(RuntimePortalCommand command) { }
        public void CastOutlined() { }
    }

    private const float DeckLeft = 100f, DeckTop = 200f;

    /// <summary>A deck mounted the way <c>RetailUiRuntime.MountCommandDeck</c> mounts it.</summary>
    private static UiCommandDeck MountDeck(UiRoot root)
    {
        var input = default(CommandDeckInput) with { Mode = CombatMode.Melee, SessionKey = 1 };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoDeckActions(), icons: null, clock: () => 0.0);
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0), UiCommandDeck.MountOptions(DeckLeft, DeckTop) with { Visible = true });
        deck.Refresh();
        return deck;
    }

    private static void ResizeDeck(UiRoot root, UiCommandDeck deck, float localX, float localY, int dx, int dy)
    {
        int x = (int)(deck.Left + localX), y = (int)(deck.Top + localY);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + dx, y + dy);
        root.OnMouseUp(UiMouseButton.Left, x + dx, y + dy);
        deck.Refresh();
    }

    /// <summary>Stretch 1.5 by the right edge, then scale 1.25 by the bottom-right corner.</summary>
    private static void ResizeDeckTo125By150(UiRoot root, UiCommandDeck deck)
    {
        ResizeDeck(root, deck, 418f, 75f, 210, 0);       // 630 x 150
        ResizeDeck(root, deck, 628f, 148f, 0, 39);       // 787.5 x 187.5
        Assert.Equal(1.25f, deck.Geometry.Scale, 3);
        Assert.Equal(1.5f, deck.Geometry.Stretch, 3);
    }

    [Fact]
    public void CommandDeck_SavedSizeRestoresTheSameGeometry()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiCommandDeck deck = MountDeck(root);
        using (new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900)))
            ResizeDeckTo125By150(root, deck);

        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            store.LoadWindowLayout("Alice", "1600x900", WindowNames.CommandDeck, default));
        Assert.Equal(787.5f, saved.Width, 1);
        Assert.Equal(187.5f, saved.Height, 1);

        var root2 = new UiRoot { Width = 1600, Height = 900 };
        UiCommandDeck deck2 = MountDeck(root2);
        using var persistence = new RetailWindowLayoutPersistence(
            root2.WindowManager, new SettingsStore(PathName), () => "Alice", () => (1600, 900));
        persistence.RestoreAll();
        deck2.Refresh();

        Assert.Equal(1.25f, deck2.Geometry.Scale, 3);
        Assert.Equal(1.5f, deck2.Geometry.Stretch, 3);
        Assert.Equal(787.5f, deck2.Width, 1);
        Assert.Equal(187.5f, deck2.Height, 1);
    }

    [Fact]
    public void CommandDeck_HandEditedSavedSizeIsCorrected()
    {
        var store = new SettingsStore(PathName);
        store.SaveWindowLayout("Alice", "1600x900", WindowNames.CommandDeck,
            new UiWindowLayout(DeckLeft, DeckTop, 700f, 163f, true, false, false));
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiCommandDeck deck = MountDeck(root);
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (1600, 900));

        persistence.RestoreAll();
        deck.Refresh();

        Assert.Equal(1.05f, deck.Geometry.Scale, 3);          // 163 / 150 = 1.087
        Assert.Equal(700f / 441f, deck.Geometry.Stretch, 3);
        Assert.Equal(deck.Geometry.Width, deck.Width);
        Assert.Equal(deck.Geometry.Height, deck.Height);
        Assert.Equal(157.5f, deck.Height, 1);
    }

    [Fact]
    public void CommandDeck_ResetLayoutGivesTheDefaultSize()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiCommandDeck deck = MountDeck(root);
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (1600, 900));
        ResizeDeckTo125By150(root, deck);

        persistence.ResetToDefaults();
        deck.Refresh();

        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
        Assert.Equal(DeckGeometry.Default, deck.Geometry);
    }

    [Fact]
    public void CommandDeck_RestoreOntoASmallScreenFitsOnScreen()
    {
        var store = new SettingsStore(PathName);
        store.SaveWindowLayout("Alice", "640x200", WindowNames.CommandDeck,
            new UiWindowLayout(300f, 100f, 945f, 225f, true, false, false));   // scale 1.5, stretch 1.5
        var root = new UiRoot { Width = 640, Height = 200 };
        UiCommandDeck deck = MountDeck(root);
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (640, 200));

        persistence.RestoreAll();
        deck.Refresh();

        Assert.True(deck.Left >= 0 && deck.Top >= 0, $"at {deck.Left},{deck.Top}");
        Assert.True(deck.Left + deck.Width <= 640.01f, $"right {deck.Left + deck.Width}");
        Assert.True(deck.Top + deck.Height <= 200.01f, $"bottom {deck.Top + deck.Height}");
        Assert.Equal(1.3f, deck.Geometry.Scale, 3);           // the largest step under 200 / 150
        Assert.Equal((deck.Geometry.Width, deck.Geometry.Height), (deck.Width, deck.Height));
    }
}
