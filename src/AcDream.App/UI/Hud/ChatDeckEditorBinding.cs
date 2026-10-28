using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// What the Filters &amp; Tabs editor reads and writes. Built once and kept, like the window;
/// it follows whichever model the session holds, and starts over (Tabs page, All) on a new one.
/// Every change goes through <see cref="ChatDeckModel.Edit"/>, which saves it.
/// </summary>
public sealed partial class ChatDeckEditorBinding
{
    private readonly Func<ChatDeckModel?> _model;
    private ChatDeckModel? _seen;

    public ChatDeckEditorBinding(Func<ChatDeckModel?> model)
    {
        _model = model;
        TabChannels = new ChatDeckChannelPicker(
            () => SelectedTab?.Channels ?? ChatDeckRule.AnyChannel,
            set => EditTab(t => t with { Channels = set }));
        CreateRules();             // ChatDeckEditorBinding.Rules.cs
    }

    /// <summary>The model, after starting over if it is not the one this editor last saw.</summary>
    private ChatDeckModel? M
    {
        get
        {
            ChatDeckModel? m = _model();
            if (!ReferenceEquals(m, _seen))
            {
                _seen = m;
                Reset();
            }
            return m;
        }
    }

    private ChatDeckSettings S => M?.Settings ?? ChatDeckDefaults.Create();

    private bool Edit(Func<ChatDeckSettings, ChatDeckSettings> change) => M?.Edit(change) ?? false;

    private ChatDeckEditorPage _page;
    public bool OnTabsPage => Page == ChatDeckEditorPage.Tabs;
    public bool OnRulesPage => Page == ChatDeckEditorPage.Rules;
    private ChatDeckEditorPage Page { get { _ = M; return _page; } }
    public Action ShowTabsPage => () => _page = ChatDeckEditorPage.Tabs;
    public Action ShowRulesPage => () => _page = ChatDeckEditorPage.Rules;
    public Action? Close { get; set; }

    /// <summary>Points the editor at what the deck asked for; the runtime then shows the window.</summary>
    internal void Open(ChatDeckEditorRequest request)
    {
        _ = M;
        _page = request.Page;
        if (request.TabId is { } id && S.Tabs.Any(t => t.Id == id)) _tabId = id;
        _confirmDelete = request.ConfirmDeleteTab;
        _confirmReset = false;
        OpenRules(request);        // ChatDeckEditorBinding.Rules.cs
    }

    private void Reset()
    {
        _page = ChatDeckEditorPage.Tabs;
        _tabId = ChatDeckDefaults.AllTabId;
        _confirmDelete = _confirmReset = false;
        ResetRules();              // ChatDeckEditorBinding.Rules.cs
    }

    public const string Markup = Head + TabsPageMarkup + RulesPageMarkup + Foot;

    private const string Head = """
        <panel x="90" y="60" w="640" h="600" title="Filters &amp; Tabs" theme="plugin" layout="column" padding="10" gap="6">
          <group layout="row" gap="4" h="24">
            <tab text="Tabs" selected="{OnTabsPage}" onclick="{ShowTabsPage}" w="90" h="22" />
            <tab text="Rules" selected="{OnRulesPage}" onclick="{ShowRulesPage}" w="90" h="22" />
          </group>
        """;

    private const string Foot = """
          <group layout="row" gap="8" h="24">
            <group grow="1" />
            <button text="Close" onclick="{Close}" w="72" h="24" />
          </group>
        </panel>
        """;
}
