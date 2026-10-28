using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private string _tabId = ChatDeckDefaults.AllTabId;
    private bool _confirmDelete;
    private bool _confirmReset;

    private ChatDeckTab? SelectedTab => S.Tabs.FirstOrDefault(t => t.Id == _tabId) ?? S.Tabs.FirstOrDefault();
    private bool AllSelected => SelectedTab?.Id == ChatDeckDefaults.AllTabId;
    private void EditTab(Func<ChatDeckTab, ChatDeckTab> change)
    {
        if (SelectedTab is { } tab) Edit(s => ChatDeckSettingsEdits.UpdateTab(s, tab.Id, change));
    }

    public IReadOnlyList<string> TabNames => S.Tabs.Select(t => t.Name).ToList();
    public int SelectedTabIndex => Math.Max(0, S.Tabs.ToList().FindIndex(t => t.Id == SelectedTab?.Id));
    public Action<int> SelectTabRow => i =>
    {
        if (i < 0 || i >= S.Tabs.Count) return;
        _tabId = S.Tabs[i].Id;
        _confirmDelete = false;
    };

    public string TabName => SelectedTab?.Name ?? "";
    public Action<string> SetTabName => v => { if (v.Trim().Length > 0) EditTab(t => t with { Name = v }); };

    public IReadOnlyList<string> IconNames { get; } = Enum.GetNames<ChatDeckIcon>();
    public string TabIcon => (SelectedTab?.Icon ?? ChatDeckIcon.All).ToString();
    public Action<string> SetTabIcon => v => { if (Enum.TryParse(v, out ChatDeckIcon icon)) EditTab(t => t with { Icon = icon }); };

    public IReadOnlyList<string> SendNames { get; } = ChatDeckSend.All.Select(ChatDeckSend.DisplayName).ToList();
    public string TabSend => ChatDeckSend.DisplayName(SelectedTab?.SendChannel ?? ChatDeckSendChannel.Say);
    public Action<string> SetTabSend => v =>
    {
        foreach (ChatDeckSendChannel c in ChatDeckSend.All)
            if (ChatDeckSend.DisplayName(c) == v) EditTab(t => t with { SendChannel = c });
    };

    public bool TabPeek => SelectedTab?.Peek ?? false;
    public Action<bool> SetTabPeek => v => EditTab(t => t with { Peek = v });

    public ChatDeckChannelPicker TabChannels { get; }
    public IReadOnlyList<ChatDeckChannelPicker.GroupRow> TabChannelGroups => TabChannels.Groups;
    public bool ChannelsEditable => !AllSelected;
    public bool ShowAllNote => AllSelected;

    public bool CanAddTab => S.Tabs.Count < ChatDeckLimits.MaxTabs;
    public Action AddTab => () =>
    {
        string? id = null;
        if (Edit(s => ChatDeckSettingsEdits.AddTab(s, "New tab", out id)) && id is not null) _tabId = id;
    };

    public bool CanMoveTabUp => SelectedTabIndex > 1;
    public bool CanMoveTabDown => !AllSelected && SelectedTabIndex < S.Tabs.Count - 1;
    public Action MoveTabUp => () => { if (SelectedTab is { } t) Edit(s => ChatDeckSettingsEdits.MoveTab(s, t.Id, -1)); };
    public Action MoveTabDown => () => { if (SelectedTab is { } t) Edit(s => ChatDeckSettingsEdits.MoveTab(s, t.Id, 1)); };

    public bool CanDeleteTab => !AllSelected;
    public bool ConfirmingDelete => _confirmDelete && !AllSelected;
    public string DeleteQuestion => $"Delete the tab \"{TabName}\"? Its lines stay in All.";
    public Action AskDeleteTab => () => { _confirmDelete = !AllSelected; _confirmReset = false; };
    public Action DeleteTabConfirmed => () =>
    {
        if (SelectedTab is { } t) Edit(s => ChatDeckSettingsEdits.DeleteTab(s, t.Id));
        _tabId = ChatDeckDefaults.AllTabId;
        _confirmDelete = false;
    };

    public bool ConfirmingReset => _confirmReset;
    public Action AskReset => () => { _confirmReset = true; _confirmDelete = false; };
    public Action ResetConfirmed => () =>
    {
        Edit(ChatDeckSettingsEdits.ResetTabs);
        _tabId = ChatDeckDefaults.AllTabId;
        _confirmReset = false;
    };
    public Action CancelConfirm => () => _confirmDelete = _confirmReset = false;

    private const string TabsPageMarkup = """
          <group layout="row" gap="10" grow="1" visible="{OnTabsPage}">
            <group layout="column" gap="6" w="170">
              <list items="{TabNames}" selected="{SelectedTabIndex}" onchange="{SelectTabRow}" rowheight="18" selectionband="true" grow="1" />
              <group layout="row" gap="4" h="22">
                <button text="+ New tab" onclick="{AddTab}" enabled="{CanAddTab}" grow="1" h="22" />
                <button text="Up" onclick="{MoveTabUp}" enabled="{CanMoveTabUp}" w="40" h="22" />
                <button text="Down" onclick="{MoveTabDown}" enabled="{CanMoveTabDown}" w="44" h="22" />
              </group>
            </group>
            <group layout="column" gap="6" grow="1">
              <group layout="row" gap="8" align="center" h="22">
                <label text="Name" w="80" />
                <field text="{TabName}" onsubmit="{SetTabName}" onblur="{SetTabName}" maxlength="24" grow="1" h="20" />
              </group>
              <group layout="row" gap="8" align="center" h="22">
                <label text="Icon" w="80" />
                <menu items="{IconNames}" selected="{TabIcon}" onchange="{SetTabIcon}" rows="12" grow="1" h="20" />
              </group>
              <group layout="row" gap="8" align="center" h="22">
                <label text="Send as" w="80" />
                <menu items="{SendNames}" selected="{TabSend}" onchange="{SetTabSend}" rows="13" grow="1" h="20" />
              </group>
              <checkbox text="Peek new lines while collapsed" checked="{TabPeek}" onchange="{SetTabPeek}" h="16" />
              <label text="Channels" h="16" />
              <label text="All shows every line that no rule hides." visible="{ShowAllNote}" h="16" />
              <group layout="column" gap="2" scroll="y" grow="1" visible="{ChannelsEditable}">
                <repeat items="{TabChannelGroups}" key="{Name}">
                  <checkbox text="{Name}" checked="{All}" onchange="{SetAll}" h="16" />
                  <repeat items="{Channels}" key="{Id}">
                    <group layout="row" h="16">
                      <group w="18" />
                      <checkbox text="{Name}" checked="{On}" onchange="{SetOn}" grow="1" h="16" />
                    </group>
                  </repeat>
                </repeat>
              </group>
              <group layout="row" gap="8" h="22">
                <button text="Delete tab" onclick="{AskDeleteTab}" enabled="{CanDeleteTab}" w="90" h="22" />
                <group grow="1" />
                <button text="Reset to defaults" onclick="{AskReset}" w="130" h="22" />
              </group>
              <group layout="row" gap="8" align="center" h="22" visible="{ConfirmingDelete}">
                <label text="{DeleteQuestion}" grow="1" />
                <button text="Delete" onclick="{DeleteTabConfirmed}" w="64" h="22" />
                <button text="Cancel" onclick="{CancelConfirm}" w="64" h="22" />
              </group>
              <group layout="row" gap="8" align="center" h="22" visible="{ConfirmingReset}">
                <label text="Put the tabs back to the defaults? Rules stay." grow="1" />
                <button text="Reset" onclick="{ResetConfirmed}" w="64" h="22" />
                <button text="Cancel" onclick="{CancelConfirm}" w="64" h="22" />
              </group>
            </group>
          </group>
        """;
}
