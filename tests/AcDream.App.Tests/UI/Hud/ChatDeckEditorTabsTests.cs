using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Markup;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckEditorTabsTests
{
    internal static (ChatDeckModel Model, ChatDeckEditorBinding Binding) Make()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        return (model, new ChatDeckEditorBinding(() => model));
    }

    [Fact]
    public void Markup_Builds()
    {
        var (_, b) = Make();
        UiElement window = ChatDeckSettingsBinding.Prepare(MarkupDocument.Build(ChatDeckEditorBinding.Markup, b, _ => (0u, 0, 0)), () => { });
        Assert.NotNull(window);
    }

    [Fact]
    public void Open_SelectsPageTabAndConfirm()
    {
        var (_, b) = Make();
        b.Open(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Tabs, TabId = "group", ConfirmDeleteTab = true });
        Assert.True(b.OnTabsPage);
        Assert.Equal("Group", b.TabName);
        Assert.Equal(3, b.SelectedTabIndex);
        Assert.True(b.ConfirmingDelete);
    }

    [Fact]
    public void Fields_EditOnlyTheSelectedTab()
    {
        var (m, b) = Make();
        b.SelectTabRow(1);   // Local
        b.SetTabName("Near");
        b.SetTabIcon("Coin");
        b.SetTabSend("Trade");
        b.SetTabPeek(false);
        ChatDeckTab local = m.Settings.Tabs[1];
        Assert.Equal(("Near", ChatDeckIcon.Coin, ChatDeckSendChannel.Trade, false), (local.Name, local.Icon, local.SendChannel, local.Peek));
        Assert.Equal("Social", m.Settings.Tabs[2].Name);
        Assert.Equal(["All", "Near", "Social", "Group", "Combat", "System"], b.TabNames);
    }

    [Fact]
    public void ChannelPicker_GroupsAndSingles()
    {
        var (m, b) = Make();
        b.SelectTabRow(1);   // Local: Say, Emote, TellIn, TellOut
        ChatDeckChannelPicker.GroupRow localGroup = b.TabChannels.Groups.Single(g => g.Name == "Local");
        Assert.True(localGroup.All);
        localGroup.Channels.Single(c => c.Id == nameof(ChatDeckChannel.Emote)).SetOn(false);
        Assert.Null(localGroup.All);
        Assert.DoesNotContain(ChatDeckChannel.Emote, m.Settings.Tabs[1].Channels);
        b.TabChannels.Groups.Single(g => g.Name == "Combat").SetAll(true);
        Assert.True(ChatDeckChannels.Group("Combat").All(m.Settings.Tabs[1].Channels.Contains));

        b.SelectTabRow(0);
        Assert.False(b.ChannelsEditable);
    }

    [Fact]
    public void AddMoveDelete()
    {
        var (m, b) = Make();
        b.AddTab();
        Assert.Equal("tab-1", m.Settings.Tabs[^1].Id);
        Assert.Equal(m.Settings.Tabs.Count - 1, b.SelectedTabIndex);
        Assert.False(b.CanMoveTabDown);
        b.MoveTabUp();
        Assert.Equal("tab-1", m.Settings.Tabs[^2].Id);

        b.SelectTabRow(0);
        Assert.False(b.CanDeleteTab);
        Assert.False(b.CanMoveTabUp);
        Assert.False(b.CanMoveTabDown);

        b.SelectTabRow(2);   // Social
        b.AskDeleteTab();
        Assert.True(b.ConfirmingDelete);
        b.CancelConfirm();
        Assert.False(b.ConfirmingDelete);
        b.AskDeleteTab();
        b.DeleteTabConfirmed();
        Assert.DoesNotContain(m.Settings.Tabs, t => t.Id == "social");
        Assert.Equal(0, b.SelectedTabIndex);
    }

    [Fact]
    public void AddTab_DisabledAtLimit_ResetAsksFirst()
    {
        var (m, b) = Make();
        while (m.Settings.Tabs.Count < ChatDeckLimits.MaxTabs) b.AddTab();
        Assert.False(b.CanAddTab);
        b.AskReset();
        Assert.True(b.ConfirmingReset);
        b.ResetConfirmed();
        Assert.Equal(ChatDeckDefaults.Create().Tabs.Count, m.Settings.Tabs.Count);
        Assert.False(b.ConfirmingReset);
    }

    [Fact]
    public void NewModel_ResetsTheEditor()
    {
        var log = new ChatLog();
        ChatDeckModel model = new(log, ChatDeckDefaults.Create(), "Davian");
        var b = new ChatDeckEditorBinding(() => model);
        b.Open(new ChatDeckEditorRequest { Page = ChatDeckEditorPage.Rules, TabId = "group", ConfirmDeleteTab = true });
        model = new ChatDeckModel(log, ChatDeckDefaults.Create(), "Other");
        Assert.True(b.OnTabsPage);
        Assert.Equal(0, b.SelectedTabIndex);
        Assert.False(b.ConfirmingDelete);
    }
}
