using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

// Restoring or saving every window touches the settings file a fixed number of
// times, however many windows there are: the file holds every section, so a
// read and a write per window froze the first world frame. A restore reads it
// once to load and once more to merge its save (another section may have been
// saved while the windows were placed); a save reads it once and writes once.
public sealed partial class RetailWindowLayoutPersistenceTests
{
    private static readonly string[] BatchWindows = ["chat", "radar", "combat", "vitals", "toolbar"];

    [Fact]
    public void RestoreAll_LoadsOnceAndSavesOnce()
    {
        var seed = new SettingsStore(PathName);
        for (int i = 0; i < BatchWindows.Length; i++)
            seed.SaveWindowPlacement("Alice", BatchWindows[i],
                new UiWindowPlacement(new UiWindowLayout(30f + i, 40f + i, 200f, 100f, true, false, false), 800, 600));

        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 800, Height = 600 };
        var handles = BatchWindows.Select(name => Mount(root, name)).ToArray();
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (800, 600));

        persistence.RestoreAll();

        Assert.Equal((2, 1), (store.FileReads, store.FileWrites));
        for (int i = 0; i < handles.Length; i++)
            Assert.Equal((30f + i, 40f + i), (handles[i].Left, handles[i].Top));
        var reread = new SettingsStore(PathName);
        for (int i = 0; i < BatchWindows.Length; i++)
            Assert.Equal((30f + i, 40f + i),
                (reread.LoadWindowPlacement("Alice", "800x600", BatchWindows[i], default)!.Value.Layout.X,
                 reread.LoadWindowPlacement("Alice", "800x600", BatchWindows[i], default)!.Value.Layout.Y));
    }

    [Fact]
    public void RestoreAfterDisplayChange_ReadsOnceAndWritesNothing()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 800, Height = 600 };
        foreach (string name in BatchWindows) Mount(root, name);
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (800, 600));
        persistence.SaveAll();
        int reads = store.FileReads, writes = store.FileWrites;

        persistence.RestoreAfterDisplayChange();

        Assert.Equal((reads + 1, writes), (store.FileReads, store.FileWrites));
    }

    [Fact]
    public void SaveAll_ReadsTheSettingsFileOnceAndWritesItOnce()
    {
        var store = new SettingsStore(PathName);
        var root = new UiRoot { Width = 800, Height = 600 };
        var handles = BatchWindows.Select(name => Mount(root, name)).ToArray();
        using var persistence = new RetailWindowLayoutPersistence(
            root.WindowManager, store, () => "Alice", () => (800, 600));
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PathName, "{}");

        persistence.SaveAll();

        Assert.Equal((1, 1), (store.FileReads, store.FileWrites));
        var reread = new SettingsStore(PathName);
        foreach (var handle in handles)
            Assert.Equal((handle.Left, handle.Top),
                (reread.LoadWindowPlacement("Alice", "800x600", handle.Name, default)!.Value.Layout.X,
                 reread.LoadWindowPlacement("Alice", "800x600", handle.Name, default)!.Value.Layout.Y));
    }
}
