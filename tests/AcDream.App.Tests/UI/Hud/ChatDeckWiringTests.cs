using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>
/// What <see cref="RetailUiRuntime"/> builds for the Chat Deck, headless: plain
/// panels for the retail windows, the real deck, persistence and switch.
/// </summary>
public sealed class ChatDeckWiringTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"acdream-chatdeck-wiring-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private sealed class World
    {
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public readonly UiField RetailInput = new();
        public readonly UiPanel Chat = new() { Width = 300, Height = 200, Left = 10, Top = 690 };
        public readonly UiPanel[] Floaties = [new(), new(), new(), new()];
        public readonly UiPanel Vitals = new() { Width = 160, Height = 58 };
        public readonly UiPanel CommandDeck = new() { Width = 420, Height = 150, Visible = false };
        public readonly UiChatDeck Deck;
        public readonly RetailWindowLayoutPersistence Persistence;
        public readonly ModernHudSwitch Switch;
        public bool DeckMounted = true;

        public World(string path)
        {
            Chat.AddChild(RetailInput);
            Root.AddChild(Chat);
            Root.RegisterWindow(WindowNames.Chat, Chat, Chat, null);
            string[] floatyNames = [WindowNames.ChatWindow1, WindowNames.ChatWindow2, WindowNames.ChatWindow3, WindowNames.ChatWindow4];
            for (int i = 0; i < 4; i++)
            {
                Floaties[i].Visible = false;
                Root.AddChild(Floaties[i]);
                Root.RegisterWindow(floatyNames[i], Floaties[i], Floaties[i], null);
            }
            Root.AddChild(Vitals);
            Root.RegisterWindow(WindowNames.Vitals, Vitals, Vitals, null);
            Root.AddChild(CommandDeck);
            Root.RegisterWindow(WindowNames.CommandDeck, CommandDeck, CommandDeck, null);
            Root.DefaultTextInput = RetailInput;

            var log = new ChatLog();
            var model = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Alice");
            Deck = new UiChatDeck(new ChatDeckBindings(
                () => model, new RuntimeChatEntryOwner(), new UiChatDeckTests.Feedback(), () => new UiChatDeckTests.RecordingBus(),
                () => null, () => false), fonts: null, icons: null);
            RetailWindowFrame.Mount(Root, Deck, _ => (0u, 0, 0), UiChatDeck.MountOptions(10f, 670f));

            Persistence = new RetailWindowLayoutPersistence(
                Root.WindowManager, new SettingsStore(path), () => "Alice", () => (1600, 900),
                stateManagedVisibilityWindows: [WindowNames.CommandDeck, WindowNames.ChatDeck]);
            Persistence.RestoreAll();

            Switch = new ModernHudSwitch(
                Root.ShowWindow, Root.HideWindow, Root.IsWindowVisible, () => true,
                [
                    new ModernHudReplacement("Command Deck", WindowNames.CommandDeck, ModernHudSwitch.CommandDeckRetailWindows,
                        () => true, OnShown: () => { }, OnHidden: () => { }),
                    new ModernHudReplacement("Chat Deck", WindowNames.ChatDeck, ModernHudSwitch.ChatDeckRetailWindows,
                        () => DeckMounted,
                        OnShown: () => { Deck.OnShown(); Root.DefaultTextInput = Deck.Input; },
                        OnHidden: () =>
                        {
                            Deck.OnHidden();
                            if (ReferenceEquals(Root.DefaultTextInput, Deck.Input)) Root.DefaultTextInput = null;
                        },
                        HoldsRetailVisibility: true),
                ],
                holdVisibility: Persistence.HoldVisibility,
                releaseVisibility: Persistence.ReleaseVisibility,
                log: _ => { });
        }
    }

    private bool SavedChatVisible() =>
        new SettingsStore(_path).LoadWindowLayout("Alice", "1600x900", WindowNames.Chat, default)!.Value.Visible;

    [Fact]
    public void On_ReplacesTheRetailChat_Off_BringsItBack()
    {
        var w = new World(_path);
        w.Switch.Apply(true);
        Assert.False(w.Chat.Visible);
        Assert.All(w.Floaties, f => Assert.False(f.Visible));
        Assert.True(w.Root.IsWindowVisible(WindowNames.ChatDeck));
        Assert.Same(w.Deck.Input, w.Root.DefaultTextInput);
        Assert.True(SavedChatVisible());

        w.Switch.Apply(false);
        Assert.True(w.Chat.Visible);
        Assert.All(w.Floaties, f => Assert.False(f.Visible));
        Assert.False(w.Root.IsWindowVisible(WindowNames.ChatDeck));
        Assert.Same(w.RetailInput, w.Root.DefaultTextInput);
        Assert.True(SavedChatVisible());
    }

    [Fact]
    public void RetailChatShownWhileUp_IsHiddenAgain()
    {
        var w = new World(_path);
        w.Switch.Apply(true);
        w.Root.ShowWindow(WindowNames.Chat);
        w.Switch.Tick();
        Assert.False(w.Chat.Visible);
        Assert.Same(w.Deck.Input, w.Root.DefaultTextInput);
        Assert.True(SavedChatVisible());
    }

    [Fact]
    public void NotMounted_LeavesTheRetailChat_AndTheCommandDeckStillReplaces()
    {
        var w = new World(_path) { DeckMounted = false };
        w.Switch.Apply(true);
        Assert.True(w.Chat.Visible);
        Assert.Same(w.RetailInput, w.Root.DefaultTextInput);
        Assert.False(w.Root.IsWindowVisible(WindowNames.ChatDeck));
        Assert.False(w.Vitals.Visible);
        Assert.True(w.CommandDeck.Visible);

        w.Switch.Apply(false);
        Assert.True(w.Chat.Visible);
        Assert.False(w.CommandDeck.Visible);
    }

    [Fact]
    public void MountOptions_ResizeEverySide_WithTheMinimum()
    {
        RetailWindowFrame.Options o = UiChatDeck.MountOptions(10f, 20f);
        Assert.Equal(WindowNames.ChatDeck, o.WindowName);
        Assert.True(o.Resizable && o.ResizeX && o.ResizeY && o.ConstrainResizeToParent);
        Assert.Equal(ChatDeckGeometry.MinWidth, o.MinWidth);
        Assert.Equal(ChatDeckGeometry.MinHeight, o.MinHeight);
        Assert.False(o.Visible);
    }
}
