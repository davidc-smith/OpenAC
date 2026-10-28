using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckLineMenuTests
{
    private static UiChatDeckTests.Harness WithLines(params string[] lines)
    {
        var h = new UiChatDeckTests.Harness();
        foreach (string text in lines) h.Say(text);
        h.Deck.Refresh();
        h.Deck.Transcript.RefreshLines();
        return h;
    }

    [Fact]
    public void OneLine_ItemsAndCopy()
    {
        var h = WithLines("first", "second");
        Assert.Equal(["Copy line", "-", "Move these to…", "Also show in…", "Hide these", "Colour these…", "-", "Test this line"],
            h.Deck.LineMenuItemsAt(5f, 16f + 5f));
        h.Deck.ChooseLineMenuItem(0);
        Assert.Contains("second", h.Keyboard.StoredText);
        Assert.DoesNotContain("first", h.Keyboard.StoredText);
    }

    [Fact]
    public void HideThese_OpensTheFormWithARuleFromTheLine()
    {
        var h = WithLines("buy my stuff");
        h.Deck.LineMenuItemsAt(5f, 5f);
        h.Deck.ChooseLineMenuItem(4);   // Hide these
        ChatDeckEditorRequest r = Assert.Single(h.EditorRequests);
        Assert.Equal(ChatDeckEditorPage.Rules, r.Page);
        Assert.Equal("buy my stuff", r.NewRule!.Pattern);
        Assert.Equal(ChatDeckRuleAction.Hide, r.NewRule.Action);
        Assert.Equal([ChatDeckChannel.Say], r.NewRule.Channels);
        Assert.Equal("buy my stuff", r.FromLine!.Text);
    }

    [Fact]
    public void MoveThese_DefaultsTheTarget()
    {
        var h = WithLines("hello");
        h.Deck.LineMenuItemsAt(5f, 5f);
        h.Deck.ChooseLineMenuItem(2);
        Assert.Equal("local", h.EditorRequests[0].NewRule!.TargetTabId);
    }

    [Fact]
    public void TestThisLine_SendsItsFacts()
    {
        var h = WithLines("hello");
        h.Deck.LineMenuItemsAt(5f, 5f);
        h.Deck.ChooseLineMenuItem(7);
        Assert.Equal("hello", h.EditorRequests[0].TestLine!.Text);
    }

    [Fact]
    public void EmptySpace_NoMenu()
    {
        var h = new UiChatDeckTests.Harness();
        h.Deck.Transcript.RefreshLines();
        Assert.Empty(h.Deck.LineMenuItemsAt(5f, 5f));
    }

    [Fact]
    public void TwoSelectedLines_CopyLinesAndTestDisabled()
    {
        var h = WithLines("first", "second", "third");
        UiText t = h.Deck.Transcript;
        t.OnEvent(new UiEvent(0, t, UiEventType.MouseDown, 0, 0, 5));
        t.OnEvent(new UiEvent(0, t, UiEventType.MouseMove, 0, 0, 16 + 5));
        t.OnEvent(new UiEvent(0, t, UiEventType.MouseUp, 0, 0, 16 + 5));
        Assert.Equal("Copy lines", h.Deck.LineMenuItemsAt(5f, 5f)[0]);
        Assert.False(h.Deck.LineMenu.ItemsEnabledSource!()[7]);
        h.Deck.ChooseLineMenuItem(0);
        Assert.Contains("first", h.Keyboard.StoredText);
        Assert.Contains("second", h.Keyboard.StoredText);
        Assert.DoesNotContain("third", h.Keyboard.StoredText);
    }
}
