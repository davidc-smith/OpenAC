using DatReaderWriter.Types;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Runtime.Chat;
using Silk.NET.Input;
using Harness = AcDream.App.Tests.UI.Hud.UiChatDeckTests.Harness;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckInputTests
{
    private static void Submit(Harness h, string text)
    {
        h.Deck.Input.SetText(text);
        h.Deck.Input.Submit();
    }

    private static SendChatCmd LastSent(Harness h) => Assert.IsType<SendChatCmd>(h.Bus.Published[^1]);

    private static void Key(UiField field, Key key) =>
        field.OnEvent(new UiEvent(0, field, UiEventType.KeyDown, Data0: (int)key));

    [Fact]
    public void Send_UsesTheTabsChannel_AndTheSharedEntry()
    {
        var h = new Harness();
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("group")]);
        h.Deck.Refresh();
        Assert.Equal("Fellowship", h.Deck.ChipLabel);

        Submit(h, "on my way");
        SendChatCmd sent = LastSent(h);
        Assert.Equal(ChatChannelKind.Fellowship, sent.Channel);
        Assert.Equal("on my way", sent.Text);
        Assert.Equal(1, h.Entry.Count);
        Assert.Equal("", h.Entry.Draft);
    }

    [Fact]
    public void ChipOverride_LastsOneLine()
    {
        var h = new Harness();
        h.Deck.ChooseChannel(ChatDeckSendChannel.General);
        h.Deck.Refresh();
        Assert.Equal("General", h.Deck.ChipLabel);

        Submit(h, "wts sword");
        Assert.Equal(ChatChannelKind.General, LastSent(h).Channel);
        Submit(h, "hello");
        Assert.Equal(ChatChannelKind.Say, LastSent(h).Channel);

        h.Deck.ChooseChannel(ChatDeckSendChannel.Trade);
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("local")]);
        Assert.Null(h.Deck.ChannelOverride);
    }

    [Fact]
    public void ChipOverride_EndsWhenTheModelIsReplaced()
    {
        var h = new Harness();
        h.Deck.ChooseChannel(ChatDeckSendChannel.Trade);
        h.Model = new ChatDeckModel(h.Log, ChatDeckDefaults.Create(), "Davian");
        h.Deck.Refresh();
        Assert.Null(h.Deck.ChannelOverride);
    }

    [Fact]
    public void Commands_GoToTheRouterUnchanged()
    {
        var h = new Harness();
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("group")]);
        Submit(h, "@tell Bob, hi there");
        SendChatCmd sent = LastSent(h);
        Assert.Equal(ChatChannelKind.Tell, sent.Channel);
        Assert.Equal("Bob", sent.TargetName);
    }

    [Fact]
    public void Draft_IsShared_AndUpRecalls()
    {
        var h = new Harness();
        h.Entry.SetDraft("half typed");
        Assert.Equal("half typed", h.Deck.Input.Text);

        h.Deck.Input.SetText("typed here");
        Assert.Equal("typed here", h.Entry.Draft);

        h.Entry.Clear();
        h.Entry.Remember("an old line");
        Key(h.Deck.Input, Silk.NET.Input.Key.Up);
        Assert.Equal("an old line", h.Deck.Input.Text);
    }

    [Fact]
    public void CtrlTab_CyclesTabs()
    {
        var h = new Harness();
        h.Keyboard.Held.Add(Silk.NET.Input.Key.ControlLeft);
        Key(h.Deck.Input, Silk.NET.Input.Key.Tab);
        Assert.Equal("local", h.Model!.SelectedTabId);

        h.Keyboard.Held.Add(Silk.NET.Input.Key.ShiftLeft);
        Key(h.Deck.Input, Silk.NET.Input.Key.Tab);
        Key(h.Deck.Input, Silk.NET.Input.Key.Tab);
        Assert.Equal(h.Model.Settings.Tabs[^1].Id, h.Model.SelectedTabId);

        // The field's own Ctrl keys still work.
        h.Keyboard.Held.Remove(Silk.NET.Input.Key.ShiftLeft);
        h.Deck.Input.SetText("copy me");
        Key(h.Deck.Input, Silk.NET.Input.Key.A);
        Key(h.Deck.Input, Silk.NET.Input.Key.C);
        Assert.Equal("copy me", h.Keyboard.StoredText);
    }

    [Fact]
    public void CtrlArrows_CycleTabs()
    {
        // macOS keeps Tab for itself while Ctrl is held, so Ctrl+Tab never arrives there.
        var h = new Harness();
        string first = h.Model!.SelectedTabId;
        h.Keyboard.Held.Add(Silk.NET.Input.Key.ControlLeft);
        Key(h.Deck.Input, Silk.NET.Input.Key.Right);
        Assert.Equal("local", h.Model.SelectedTabId);

        Key(h.Deck.Input, Silk.NET.Input.Key.Left);
        Assert.Equal(first, h.Model.SelectedTabId);

        Key(h.Deck.Input, Silk.NET.Input.Key.Left);
        Assert.Equal(h.Model.Settings.Tabs[^1].Id, h.Model.SelectedTabId);
    }

    [Fact]
    public void EntryTarget_ActsOnTheInput()
    {
        var h = new Harness();
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(h.Deck);
        IChatEntryTarget target = h.Deck;

        target.StartTell("Bob");
        Assert.Equal("@tell Bob, ", h.Deck.Input.Text);
        Assert.Same(h.Deck.Input, root.KeyboardFocus);

        target.StartCommand();
        Assert.Equal("/", h.Deck.Input.Text);

        target.StartReply(null);
        Assert.Equal("/", h.Deck.Input.Text);

        root.SetKeyboardFocus(null);
        Assert.True(target.Focus());
        Assert.Same(h.Deck.Input, root.KeyboardFocus);

        target.ToggleChatEntry(null);
        Assert.Null(root.KeyboardFocus);
        target.ToggleChatEntry(null);
        Assert.Same(h.Deck.Input, root.KeyboardFocus);

        root.SetKeyboardFocus(null);
        target.EnterChatMode(null);
        Assert.Same(h.Deck.Input, root.KeyboardFocus);

        h.Deck.OnHidden();
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void TellLink_StartsATell()
    {
        var h = new Harness();
        h.Model!.ApplySettings(h.Model.Settings with
        {
            Appearance = h.Model.Settings.Appearance with { Timestamps = ChatDeckTimestamps.Off },
        });
        h.Log.OnTellReceived("Bob", "meet me", 0x5000000Au, (uint)RetailLogTextType.Tell);
        h.Model!.Tick();
        h.Deck.Refresh();
        Assert.True(h.Deck.Transcript.OnCharClick!(new UiText.Pos(0, 1)));
        Assert.Equal("@tell Bob, ", h.Deck.Input.Text);
    }

    [Fact]
    public void Detach_StopsFollowingTheDraft()
    {
        var h = new Harness();
        h.Deck.Detach();
        h.Entry.SetDraft("elsewhere");
        Assert.NotEqual("elsewhere", h.Deck.Input.Text);
    }

    [Fact]
    public void Detach_StopsListeningToTheModelsClear()
    {
        var h = new Harness();
        h.Deck.ChooseChannel(ChatDeckSendChannel.Trade);
        h.Log.Clear();
        h.Model!.Tick();
        Assert.Null(h.Deck.ChannelOverride);   // attached: a clear resets the view

        h.Deck.ChooseChannel(ChatDeckSendChannel.Trade);
        h.Deck.Detach();
        h.Log.Clear();
        h.Model.Tick();
        Assert.Equal(ChatDeckSendChannel.Trade, h.Deck.ChannelOverride);
    }

    private static UiDatFont WideFont()
    {
        var glyphs = new Dictionary<char, FontCharDesc>();
        for (char c = ' '; c <= '~'; c++)
            glyphs[c] = new FontCharDesc { Unicode = c, Width = 12, Height = 8 };
        return new UiDatFont(fgTex: 1u, fgW: 32, fgH: 32, bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f, glyphs);
    }

    [Fact]
    public void Menus_CarryTheDecksPalette()
    {
        var h = new Harness();
        Assert.Same(ChatDeckPalette.Menu, h.Deck.ChipMenu.ThemePalette);
        Assert.Same(ChatDeckPalette.Menu, h.Deck.TabMenu.ThemePalette);
    }

    [Fact]
    public void Menus_TakeTheTranscriptFont_OnlyWhenFontsExist()
    {
        Assert.Null(new Harness().Deck.ChipMenu.DatFont);
        UiDatFont font = WideFont();
        var h = new Harness(fonts: new DeckFonts((_, _) => font));
        Assert.Same(font, h.Deck.ChipMenu.DatFont);
        Assert.Same(font, h.Deck.TabMenu.DatFont);
    }

    [Fact]
    public void ChipMenu_IsWideEnoughForItsWidestItem()
    {
        UiDatFont font = WideFont();
        var h = new Harness(fonts: new DeckFonts((_, _) => font));
        var root = new UiRoot { Width = 1200, Height = 800 };
        root.AddChild(h.Deck);
        Assert.True(h.Deck.ChipMenu.TryOpen(20, 20));
        float widest = ChatDeckSend.All.Max(c => font.MeasureWidth(ChatDeckSend.DisplayName(c)));
        Assert.True(widest > 0f);
        Assert.True(h.Deck.ChipMenu.PopupScreenRect.W >= widest + 2f * UiMarkupContextMenu.TextIndent);
    }
}
