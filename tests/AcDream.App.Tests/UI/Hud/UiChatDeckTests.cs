using AcDream.App.Tests.Plugins;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Chat;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckTests
{
    internal sealed class RecordingBus : ICommandBus
    {
        public readonly List<object> Published = [];
        public void Publish<T>(T command) where T : notnull => Published.Add(command);
    }

    internal sealed class Feedback : IChatCommandFeedback
    {
        public readonly List<string> Lines = [];
        public string? LastIncomingTellSender { get; set; }
        public string? LastOutgoingTellTarget { get; set; }
        public void ShowInterfaceText(string text) => Lines.Add(text);
        public void ShowSystemMessage(string text) => Lines.Add(text);
    }

    internal sealed class Harness
    {
        public readonly ChatLog Log = new();
        public ChatDeckModel? Model;
        public readonly RuntimeChatEntryOwner Entry = new();
        public readonly RecordingBus Bus = new();
        public readonly Feedback Feedback = new();
        public readonly List<PluginChatLinkClicked> Links = [];
        public readonly FakeKeyboard Keyboard = new();
        public readonly UiChatDeck Deck;
        public Action? RestorePlacement;

        public Harness(bool withModel = true, DeckFonts? fonts = null)
        {
            if (withModel) Model = new ChatDeckModel(Log, ChatDeckDefaults.Create(), "Davian");
            Deck = new UiChatDeck(
                new ChatDeckBindings(
                    Model: () => Model,
                    Entry: Entry,
                    Feedback: Feedback,
                    Bus: () => Bus,
                    LastIncomingTellSender: () => Feedback.LastIncomingTellSender,
                    StayInChatMode: () => false,
                    LinkClicked: Links.Add,
                    Keyboard: Keyboard,
                    RestorePinnedPlacement: () => RestorePlacement?.Invoke()),
                fonts: fonts, icons: null)
            { Width = ChatDeckGeometry.DefaultWidth, Height = ChatDeckGeometry.DefaultHeight };
            Deck.Refresh();
        }

        public void Say(string text, string sender = "Bob")
        {
            Log.OnLocalSpeech(sender, text, 0x50000002u, isRanged: false, logTextType: 0x02u);
            Model!.Tick();
            Deck.Refresh();
        }

        public void Press(DeckRect r) =>
            Deck.OnEvent(new UiEvent(0, Deck, UiEventType.MouseDown, Data1: (int)(r.X + 2), Data2: (int)(r.Y + 2)));

        public int TabIndex(string id) => Model!.Settings.Tabs.ToList().FindIndex(t => t.Id == id);
    }

    [Fact]
    public void PressOnATab_SelectsIt()
    {
        var h = new Harness();
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("combat")]);
        Assert.Equal("combat", h.Model!.SelectedTabId);
    }

    [Fact]
    public void FrameBody_IsLeftToTheWindowDrag()
    {
        var h = new Harness();
        Assert.True(h.Deck.HitTestLocal(2, 2));
        Assert.False(h.Deck.HandlesClick);
        DeckRect tab = h.Deck.Geometry.Tabs[0];
        h.Deck.HitTestLocal(tab.X + 2, tab.Y + 2);
        Assert.True(h.Deck.HandlesClick);
    }

    [Fact]
    public void Badges_ShowUnread()
    {
        var h = new Harness();
        h.Say("hello");
        Assert.Equal("1", h.Deck.TabLabels[h.TabIndex("local")].Badge);
        Assert.Equal("", h.Deck.TabLabels[h.TabIndex("combat")].Badge);
    }

    [Fact]
    public void Transcript_ShowsTheSelectedTab()
    {
        var h = new Harness();
        h.Say("hello");
        Assert.Single(h.Deck.Transcript.LinesProvider());
        h.Press(h.Deck.Geometry.Tabs[h.TabIndex("combat")]);
        h.Deck.Refresh();
        Assert.Empty(h.Deck.Transcript.LinesProvider());
    }

    [Fact]
    public void Transcript_RebuildsOnlyWhenSomethingChanged()
    {
        var h = new Harness();
        h.Say("hello");
        int builds = h.Deck.TranscriptBuilds;
        h.Deck.Refresh();
        h.Deck.Refresh();
        Assert.Equal(builds, h.Deck.TranscriptBuilds);
        h.Say("again");
        Assert.Equal(builds + 1, h.Deck.TranscriptBuilds);
    }

    [Fact]
    public void ShownAndAtBottom_ReadsTheSelectedTab()
    {
        var h = new Harness();
        h.Deck.OnShown();
        Assert.True(h.Model!.ViewVisible);
        h.Say("seen");
        Assert.Equal(0, h.Model.FindTab("all")!.Unread);

        // A hidden window gets no ticks, so hiding must tell the model at once (no Refresh here).
        h.Deck.OnHidden();
        Assert.False(h.Model.ViewVisible);
        h.Log.OnLocalSpeech("Bob", "unseen", 0x50000002u, isRanged: false, logTextType: 0x02u);
        h.Model.Tick();
        Assert.Equal(1, h.Model.FindTab("all")!.Unread);
    }

    [Fact]
    public void TabMenu_MarksRead()
    {
        var h = new Harness();
        h.Say("one");
        DeckRect local = h.Deck.Geometry.Tabs[h.TabIndex("local")];
        Assert.Equal(["Mark as read", "Mark all read"], h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2));
        h.Deck.ChooseTabMenuItem(0);
        Assert.Equal(0, h.Model!.FindTab("local")!.Unread);
        Assert.Equal(1, h.Model.FindTab("all")!.Unread);

        Assert.Empty(h.Deck.TabMenuItemsAt(5, h.Deck.Geometry.Transcript.Y + 10));
        h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2);
        h.Deck.ChooseTabMenuItem(1);
        Assert.Equal(0, h.Model.FindTab("all")!.Unread);
    }

    [Fact]
    public void Search_NarrowsAndEscClears()
    {
        var h = new Harness();
        h.Say("a drudge appears");
        h.Say("a banderling appears");
        h.Press(h.Deck.Geometry.SearchStud);
        h.Deck.Refresh();
        Assert.True(h.Deck.SearchOpen);

        h.Deck.SearchField.SetText("drudge");
        h.Deck.Refresh();
        Assert.Single(h.Deck.Transcript.LinesProvider());

        h.Deck.SearchField.OnEvent(new UiEvent(0, h.Deck.SearchField, UiEventType.KeyDown, Data0: (int)Silk.NET.Input.Key.Escape));
        h.Deck.Refresh();
        Assert.False(h.Deck.SearchOpen);
        Assert.Equal(2, h.Deck.Transcript.LinesProvider().Count);
    }

    [Fact]
    public void ScrolledUp_CountsNewLines_ClickFollows()
    {
        var h = new Harness();
        h.Say("first");
        UiScrollable scroll = h.Deck.Transcript.Scroll;
        scroll.SetExtents(1000, 100);
        scroll.SetScrollY(0);
        h.Deck.Refresh();

        h.Say("second");
        h.Say("third");
        Assert.Equal(2, h.Deck.NewLineCount);
        Assert.True(h.Deck.NewLinesMarker.Visible);

        h.Deck.NewLinesMarker.OnEvent(new UiEvent(0, h.Deck.NewLinesMarker, UiEventType.MouseDown));
        h.Deck.Refresh();
        Assert.True(scroll.AtEnd);
        Assert.Equal(0, h.Deck.NewLineCount);
        Assert.False(h.Deck.NewLinesMarker.Visible);
    }

    [Fact]
    public void ClearAndNewModel_ResetTheView()
    {
        var h = new Harness();
        h.Say("before");
        h.Log.Clear();
        h.Model!.Tick();
        h.Deck.Refresh();
        Assert.Empty(h.Deck.Transcript.LinesProvider());

        h.Say("again");
        h.Model = new ChatDeckModel(h.Log, ChatDeckDefaults.Create(), "Davian");
        h.Deck.Refresh();
        Assert.Empty(h.Deck.Transcript.LinesProvider());
    }

    [Fact]
    public void NoModel_NoTabs()
    {
        var h = new Harness(withModel: false);
        Assert.Empty(h.Deck.Geometry.Tabs);
        Assert.Empty(h.Deck.Transcript.LinesProvider());
        MarkupDrawProbe.Draw(h.Deck, 460, 220);   // draws without throwing
    }

    [Fact]
    public void Draws_WithTabsBadgesAndSearchOpen()
    {
        var h = new Harness();
        h.Say("hello");
        h.Press(h.Deck.Geometry.SearchStud);
        h.Deck.Refresh();
        var renderer = MarkupDrawProbe.Draw(h.Deck, 460, 220);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, ChatDeckPalette.BadgeGold) > 0);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, ChatDeckPalette.TabSelected) > 0);

        // The selected tab's underline shares the badge gold, so compare with a deck with no badges.
        var quiet = new Harness();
        var plain = MarkupDrawProbe.Draw(quiet.Deck, 460, 220);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, ChatDeckPalette.BadgeGold)
            > MarkupDrawProbe.CountQuads(plain, ChatDeckPalette.BadgeGold));
    }

    [Fact]
    public void AlertTab_DrawsItsBadgeRed()
    {
        var h = new Harness();
        h.Say("hey Davian, over here");
        Assert.True(h.Model!.FindTab("all")!.Alert);
        var renderer = MarkupDrawProbe.Draw(h.Deck, 460, 220);
        var calm = MarkupDrawProbe.Draw(new Harness().Deck, 460, 220);
        Assert.True(MarkupDrawProbe.CountQuads(renderer, ChatDeckPalette.BadgeRed)
            > MarkupDrawProbe.CountQuads(calm, ChatDeckPalette.BadgeRed));
    }
}
