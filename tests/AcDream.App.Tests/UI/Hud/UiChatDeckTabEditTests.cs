using AcDream.App.UI.Hud;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiChatDeckTabEditTests
{
    [Fact]
    public void FiltersStud_TogglesTheEditor_AndLights()
    {
        var h = new UiChatDeckTests.Harness();
        h.Press(h.Deck.Geometry.FiltersStud);
        Assert.Equal(1, h.EditorToggles);
    }

    [Fact]
    public void Plus_AddsSelectsAndOpensTheEditorOnIt()
    {
        var h = new UiChatDeckTests.Harness();
        h.Press(h.Deck.Geometry.AddTab);
        Assert.Equal("tab-1", h.Model!.SelectedTabId);
        ChatDeckEditorRequest r = Assert.Single(h.EditorRequests);
        Assert.Equal((ChatDeckEditorPage.Tabs, "tab-1"), (r.Page, r.TabId));
    }

    [Fact]
    public void TabMenu_ItemsAndDeleteOnAll()
    {
        var h = new UiChatDeckTests.Harness();
        DeckRect all = h.Deck.Geometry.Tabs[0];
        Assert.Equal(["Rename", "Edit tab…", "-", "Mark as read", "Mark all read", "-", "Delete"],
            h.Deck.TabMenuItemsAt(all.X + 2, all.Y + 2));
        Assert.False(h.Deck.TabMenu.ItemsEnabledSource!()[6]);

        DeckRect group = h.Deck.Geometry.Tabs[3];
        h.Deck.TabMenuItemsAt(group.X + 2, group.Y + 2);
        Assert.True(h.Deck.TabMenu.ItemsEnabledSource!()[6]);
        h.Deck.ChooseTabMenuItem(6);
        ChatDeckEditorRequest del = Assert.Single(h.EditorRequests);
        Assert.Equal(("group", true), (del.TabId, del.ConfirmDeleteTab));
        h.Deck.ChooseTabMenuItem(1);
        Assert.Equal(("group", false), (h.EditorRequests[1].TabId, h.EditorRequests[1].ConfirmDeleteTab));
    }

    [Fact]
    public void Rename_EnterApplies_EscCancels()
    {
        var h = new UiChatDeckTests.Harness();
        DeckRect local = h.Deck.Geometry.Tabs[1];
        h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2);
        h.Deck.ChooseTabMenuItem(0);
        Assert.True(h.Deck.RenameField.Visible);
        Assert.Equal("Local", h.Deck.RenameField.Text);
        h.Deck.RenameField.OnSubmit!("Near");
        Assert.Equal("Near", h.Model!.Settings.Tabs[1].Name);
        Assert.False(h.Deck.RenameField.Visible);

        h.Deck.TabMenuItemsAt(local.X + 2, local.Y + 2);
        h.Deck.ChooseTabMenuItem(0);
        h.Deck.RenameField.OnEscape!();
        h.Deck.RenameField.OnFocusLost?.Invoke("Ignored");
        Assert.Equal("Near", h.Model.Settings.Tabs[1].Name);
        Assert.False(h.Deck.RenameField.Visible);
    }
}
