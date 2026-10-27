using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    [Fact]
    public void CaptureGate_Closed_MovesAreNotRecordedOrSaved_AndRestoresLeaveTheWindow()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 800, Height = 600 };
        RetailWindowHandle handle = Mount(root, "w");
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (800, 600));
        handle.MoveTo(44f, 55f);
        UiWindowLayout before = Assert.IsType<UiWindowLayout>(store.LoadWindowLayout("Alice", "800x600", "w", default));

        bool open = false;
        persistence.SetCaptureGate("w", () => open);
        handle.MoveTo(300f, 300f);
        handle.ResizeTo(250f, 150f);

        Assert.Equal(before, store.LoadWindowLayout("Alice", "800x600", "w", default));
        persistence.RestoreAll();
        persistence.ReflowToScreen();
        persistence.ClampAllToScreen();
        Assert.Equal((300f, 300f), (handle.Left, handle.Top));
        persistence.SaveAll();
        UiWindowLayout after = Assert.IsType<UiWindowLayout>(store.LoadWindowLayout("Alice", "800x600", "w", default));
        Assert.Equal((44f, 55f, 200f, 100f), (after.X, after.Y, after.Width, after.Height));

        open = true;
        persistence.ApplyStoredPlacement("w");
        Assert.Equal((44f, 55f), (handle.Left, handle.Top));
        Assert.Equal((200f, 100f), (handle.Width, handle.Height));

        handle.MoveTo(60f, 70f);
        UiWindowLayout moved = Assert.IsType<UiWindowLayout>(store.LoadWindowLayout("Alice", "800x600", "w", default));
        Assert.Equal((60f, 70f), (moved.X, moved.Y));
    }

    [Fact]
    public void CaptureGate_Closed_ResetToDefaultsDoesNotMoveTheWindow()
    {
        var root = new UiRoot { Width = 800, Height = 600 };
        RetailWindowHandle handle = Mount(root, "w");
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, new SettingsStore(PathName), () => "Alice", () => (800, 600));
        persistence.SetCaptureGate("w", () => false);
        handle.MoveTo(300f, 300f);

        persistence.ResetToDefaults();

        Assert.Equal((300f, 300f), (handle.Left, handle.Top));
    }

    [Fact]
    public void CaptureGate_Closed_RestoreRecordsTheSavedPlacementForLater()
    {
        var store = new SettingsStore(PathName);
        store.SaveWindowLayout("Alice", "800x600", "w", new UiWindowLayout(120f, 130f, 220f, 110f, true, false, false));
        var root = new UiRoot { Width = 800, Height = 600 };
        RetailWindowHandle handle = Mount(root, "w");
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (800, 600));
        bool open = false;
        persistence.SetCaptureGate("w", () => open);
        handle.MoveTo(300f, 300f);

        persistence.RestoreAll();
        Assert.Equal((300f, 300f), (handle.Left, handle.Top));

        open = true;
        persistence.ApplyStoredPlacement("w");
        Assert.Equal((120f, 130f, 220f, 110f), (handle.Left, handle.Top, handle.Width, handle.Height));
    }
}
