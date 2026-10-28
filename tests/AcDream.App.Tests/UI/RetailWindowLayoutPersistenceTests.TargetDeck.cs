using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.UI.Abstractions.Panels.Settings;
using static AcDream.App.Tests.UI.Hud.TargetDeckModels;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    /// <summary>A Target Deck mounted the way <c>RetailUiRuntime.MountTargetDeck</c> mounts it.</summary>
    private static (UiTargetDeck Deck, RetailWindowHandle Handle) MountTargetDeck(UiRoot root)
    {
        var deck = new UiTargetDeck(() => null, new NoActions(), icons: null, clock: () => 0.0);
        RetailWindowHandle handle = RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0),
            UiTargetDeck.MountOptions(RetailUiRuntime.TargetDeckDefaultLeft(root.Width), RetailUiRuntime.TargetDeckDefaultTop)
                with { Visible = true });
        return (deck, handle);
    }

    [Fact]
    public void TargetDeck_SavedPositionRoundTrips()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 1600, Height = 900 };
        var (_, handle) = MountTargetDeck(root);
        using (new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900),
            stateManagedVisibilityWindows: [WindowNames.TargetDeck]))
            handle.MoveTo(300f, 40f);

        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            store.LoadWindowLayout("Alice", "1600x900", WindowNames.TargetDeck, default));
        Assert.Equal((300f, 40f), (saved.X, saved.Y));
        Assert.Equal((TargetDeckGeometry.Width, TargetDeckGeometry.Height), (saved.Width, saved.Height));

        var root2 = new UiRoot { Width = 1600, Height = 900 };
        var (deck2, _) = MountTargetDeck(root2);
        using var persistence = new RetailWindowLayoutPersistence(
            root2.WindowManager, new SettingsStore(PathName), () => "Alice", () => (1600, 900),
            stateManagedVisibilityWindows: [WindowNames.TargetDeck]);
        persistence.RestoreAll();

        Assert.Equal((300f, 40f), (deck2.Left, deck2.Top));
        Assert.Equal((TargetDeckGeometry.Width, TargetDeckGeometry.Height), (deck2.Width, deck2.Height));
    }

    [Fact]
    public void TargetDeck_ResetLayoutGivesTheDefaultPlace()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 1600, Height = 900 };
        var (deck, handle) = MountTargetDeck(root);
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (1600, 900),
            stateManagedVisibilityWindows: [WindowNames.TargetDeck]);
        handle.MoveTo(10f, 500f);

        persistence.ResetToDefaults();

        Assert.Equal(((1600f - TargetDeckGeometry.Width) * 0.5f, 12f), (deck.Left, deck.Top));
    }
}
