using AcDream.App.Tests.UI.Hud;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    // The Gear Deck's scale is its saved width over 300, as the Command Deck's is its saved height over 150.

    [Fact]
    public void GearDeck_SavedWidthRestoresTheSameScale()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck = GearDeckScaleTests.MountDeck(root);
        using (new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900)))
            GearDeckScaleTests.Drag(root, deck, deck.Width - 1f, 300f, 75, 0);
        Assert.Equal(1.25f, deck.Scale);

        UiWindowLayout saved = Assert.IsType<UiWindowLayout>(
            store.LoadWindowLayout("Alice", "1600x900", WindowNames.GearDeck, default));
        Assert.Equal(375f, saved.Width, 1);

        var root2 = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck2 = GearDeckScaleTests.MountDeck(root2);
        using var persistence = new RetailWindowLayoutPersistence(
            root2.WindowManager, new SettingsStore(PathName), () => "Alice", () => (1600, 900));
        persistence.RestoreAll();
        deck2.Refresh();

        Assert.Equal(1.25f, deck2.Scale);
        Assert.Equal(375f, deck2.Width);
        Assert.Equal(1.25f, deck2.Geometry.Scale);
    }

    [Theory]
    [InlineData(300f, 1f)]       // saved before the Gear Deck followed any scale
    [InlineData(450f, 1.5f)]     // saved while it followed a Command Deck at 1.5
    [InlineData(460f, 1.5f)]     // hand-edited: never wider than the saved width
    [InlineData(100f, 0.75f)]    // under the range
    [InlineData(2000f, 1.5f)]    // over the range
    public void GearDeck_AnySavedWidthLoadsAtAScaleStep(float savedWidth, float scale)
    {
        var store = new SettingsStore(PathName);
        store.SaveWindowLayout("Alice", "1600x900", WindowNames.GearDeck,
            new UiWindowLayout(400f, 50f, savedWidth, 700f, true, false, false));
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck = GearDeckScaleTests.MountDeck(root);
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (1600, 900));

        persistence.RestoreAll();
        deck.Refresh();

        Assert.Equal(scale, deck.Scale, 3);
        Assert.Equal(GearDeckGeometry.WidthAt(deck.Scale), deck.Width);
        Assert.Equal(deck.Width, deck.Geometry.Frame.W);
        Assert.True(deck.Left + deck.Width <= 1600.01f);
    }

    [Fact]
    public void GearDeck_ResetLayoutGivesScale1()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 1600, Height = 1200 };
        UiGearDeck deck = GearDeckScaleTests.MountDeck(root);
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (1600, 1200));
        GearDeckScaleTests.Drag(root, deck, deck.Width - 1f, 300f, 150, 0);
        Assert.Equal(1.5f, deck.Scale);

        persistence.ResetToDefaults();
        deck.Refresh();

        Assert.Equal(1f, deck.Scale);
        Assert.Equal(300f, deck.Width);
    }
}
