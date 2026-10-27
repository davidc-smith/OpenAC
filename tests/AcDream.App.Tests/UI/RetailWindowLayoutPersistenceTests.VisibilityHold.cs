using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed partial class RetailWindowLayoutPersistenceTests
{
    private static (UiRoot Root, UiPanel Chat) MountChat(bool visible = true)
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        var chat = new UiPanel { Width = 300, Height = 200, Left = 10, Top = 600, Visible = visible };
        root.AddChild(chat);
        root.RegisterWindow(WindowNames.Chat, chat, chat, null);
        return (root, chat);
    }

    private static bool SavedVisible(SettingsStore store) =>
        store.LoadWindowLayout("Alice", "1600x900", WindowNames.Chat, default)!.Value.Visible;

    [Fact]
    public void Held_HideDoesNotSaveHidden()
    {
        var store = new SettingsStore(PathName);
        var (root, _) = MountChat();
        using var persistence = new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900));
        persistence.RestoreAll();

        persistence.HoldVisibility(WindowNames.Chat);
        root.HideWindow(WindowNames.Chat);
        Assert.True(root.WindowManager.TryGet(WindowNames.Chat, out RetailWindowHandle? handle));
        handle!.MoveTo(40, 500);   // a geometry save while held

        Assert.True(SavedVisible(store));
        Assert.True(persistence.ReleaseVisibility(WindowNames.Chat));
    }

    [Fact]
    public void Held_RestoreRecordsButDoesNotShow()
    {
        var store = new SettingsStore(PathName);
        var (root, _) = MountChat();
        using (var first = new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900)))
            first.RestoreAll();   // saves chat visible

        var (root2, chat2) = MountChat(visible: false);
        using var persistence = new RetailWindowLayoutPersistence(root2.WindowManager, store, () => "Alice", () => (1600, 900));
        persistence.HoldVisibility(WindowNames.Chat);   // recorded hidden
        persistence.RestoreAll();

        Assert.False(chat2.Visible);
        Assert.True(persistence.ReleaseVisibility(WindowNames.Chat));   // the restore recorded "visible"
    }

    [Fact]
    public void Held_NamedLayoutsUseTheRecordedVisibility()
    {
        var store = new SettingsStore(PathName);
        var (root, chat) = MountChat();
        using var persistence = new RetailWindowLayoutPersistence(root.WindowManager, store, () => "Alice", () => (1600, 900));
        persistence.RestoreAll();
        persistence.HoldVisibility(WindowNames.Chat);
        root.HideWindow(WindowNames.Chat);

        persistence.SaveNamed("raid");
        Assert.True(store.LoadNamedWindowLayout("raid", WindowNames.Chat, default)!.Value.Visible);

        persistence.RestoreNamed("raid");
        Assert.False(chat.Visible);
    }

    [Fact]
    public void Release_WhenNotHeld_IsTheCurrentVisibility()
    {
        var (root, _) = MountChat(visible: false);
        using var persistence = new RetailWindowLayoutPersistence(root.WindowManager, null, () => "Alice", () => (1600, 900));
        Assert.False(persistence.ReleaseVisibility(WindowNames.Chat));
    }
}
