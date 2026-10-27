using System.Collections.Concurrent;

namespace AcDream.Core.Chat.Deck;

/// <summary>A line delivered on one tick and the tabs it went to.</summary>
public sealed record ChatDeckDelivery(ChatDeckLine Line, IReadOnlyList<string> TabIds);

/// <summary>
/// The Chat Deck for one game session. Lines may arrive on any thread; they are queued
/// and applied by <see cref="Tick"/>, which, like every other member, runs on the game thread.
/// <see cref="Tick"/> must run every frame while the session lives, whether or not the window is
/// visible: the queue is unbounded and the model keeps collecting lines while hidden.
/// Event handlers run inside <see cref="Tick"/> (and <see cref="SelectTab"/>) and may call the model's members.
/// </summary>
public sealed class ChatDeckModel : IDisposable
{
    private readonly ChatLog _log;
    private readonly string _characterName;
    private readonly ConcurrentQueue<ChatEntry?> _queue = new();   // null marks a /clear
    private readonly List<ChatDeckTabState> _tabs = [];
    private ChatDeckRuleEngine _engine;
    private bool _disposed;

    public ChatDeckModel(ChatLog log, ChatDeckSettings settings, string characterName)
    {
        _log = log;
        _characterName = characterName;
        Settings = ChatDeckSettingsNormalizer.Normalize(settings);
        _engine = new ChatDeckRuleEngine(Settings.Rules, Settings.Tabs, _characterName);
        RebuildTabs();
        SelectedTabId = Settings.Layout.LastTabId;
        _log.EntryAppended += OnEntryAppended;
        _log.Cleared += OnCleared;
    }

    public string CharacterName => _characterName;
    public ChatDeckSettings Settings { get; private set; }
    /// <summary>Each rule as compiled, with its status (invalid pattern, faulted, ...), for the editor.</summary>
    public IReadOnlyList<ChatDeckCompiledRule> CompiledRules => _engine.Rules;
    public IReadOnlyList<ChatDeckTabState> Tabs => _tabs;
    public string SelectedTabId { get; private set; }
    public bool ViewVisible { get; private set; }
    public bool ViewAtBottom { get; private set; } = true;

    /// <summary>Lines applied by one <see cref="Tick"/>; not raised when there were none.</summary>
    public event Action<IReadOnlyList<ChatDeckDelivery>>? LinesDelivered;
    /// <summary>The chat log was cleared and every tab emptied.</summary>
    public event Action? Cleared;
    /// <summary>The model changed its own settings (a rule faulted, the selected tab changed); save them.</summary>
    public event Action<ChatDeckSettings>? SettingsChanged;

    public ChatDeckTabState? FindTab(string tabId) => _tabs.FirstOrDefault(t => t.TabId == tabId);

    public void Tick()
    {
        if (_disposed) return;
        List<ChatDeckDelivery>? delivered = null;
        bool settingsChanged = false;

        while (_queue.TryDequeue(out ChatEntry? item))
        {
            if (item is not { } entry)
            {
                if (delivered is not null)
                {
                    LinesDelivered?.Invoke(delivered);
                    delivered = null;
                }
                foreach (ChatDeckTabState tab in _tabs) tab.Clear();
                Cleared?.Invoke();
                continue;
            }

            ChatDeckLineFacts facts = ChatDeckLineFacts.From(entry, _characterName);
            ChatDeckVerdict verdict = _engine.Evaluate(facts);
            if (verdict.TimedOutRuleIds.Count > 0)
            {
                // Turn the rule off at once, so the next line in this tick does not wait on it too.
                var ids = verdict.TimedOutRuleIds.ToHashSet(StringComparer.Ordinal);
                Settings = Settings with { Rules = MarkFaulted(Settings.Rules, ids) };
                _engine = new ChatDeckRuleEngine(Settings.Rules, Settings.Tabs, _characterName);
                settingsChanged = true;
            }
            if (verdict.Hidden) continue;

            var line = new ChatDeckLine(facts, verdict);
            foreach (string tabId in verdict.Destinations)
                FindTab(tabId)?.Add(line, seen: IsSeen(tabId));
            (delivered ??= []).Add(new ChatDeckDelivery(line, verdict.Destinations));
        }

        if (settingsChanged) SettingsChanged?.Invoke(Settings);
        if (delivered is not null) LinesDelivered?.Invoke(delivered);
    }

    /// <summary>
    /// Replaces the settings and rebuilds rules and tabs. Raises no <see cref="SettingsChanged"/>:
    /// the caller supplied these settings and saves <see cref="Settings"/> itself.
    /// </summary>
    public void ApplySettings(ChatDeckSettings settings)
    {
        Settings = ChatDeckSettingsNormalizer.Normalize(settings);
        _engine = new ChatDeckRuleEngine(Settings.Rules, Settings.Tabs, _characterName);
        RebuildTabs();
        if (FindTab(SelectedTabId) is null) SelectedTabId = ChatDeckDefaults.AllTabId;
        if (Settings.Layout.LastTabId != SelectedTabId)
            Settings = Settings with { Layout = Settings.Layout with { LastTabId = SelectedTabId } };
    }

    public void SelectTab(string tabId)
    {
        if (FindTab(tabId) is not { } tab) return;
        SelectedTabId = tabId;
        tab.MarkRead();
        if (Settings.Layout.LastTabId == tabId) return;
        Settings = Settings with { Layout = Settings.Layout with { LastTabId = tabId } };
        SettingsChanged?.Invoke(Settings);
    }

    /// <summary>Whether the window shows the transcript (pinned or flown out) and whether it is scrolled to the newest line.</summary>
    public void SetView(bool visible, bool atBottom)
    {
        ViewVisible = visible;
        ViewAtBottom = atBottom;
        if (visible && atBottom) FindTab(SelectedTabId)?.MarkRead();
    }

    public void MarkAllRead()
    {
        foreach (ChatDeckTabState tab in _tabs) tab.MarkRead();
    }

    /// <summary>The verdict a line would get, for the editor's test box. Changes nothing.</summary>
    public ChatDeckVerdict Test(ChatDeckLineFacts facts) => _engine.Evaluate(facts);

    public ChatDeckSendChannel SendChannelFor(string tabId, ChatDeckSendChannel? overrideChannel) =>
        overrideChannel
        ?? Settings.Tabs.FirstOrDefault(t => t.Id == tabId)?.SendChannel
        ?? ChatDeckSendChannel.Say;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _log.EntryAppended -= OnEntryAppended;
        _log.Cleared -= OnCleared;
        _queue.Clear();
    }

    private void OnEntryAppended(ChatEntry entry)
    {
        if (!_disposed) _queue.Enqueue(entry);
    }

    private void OnCleared()
    {
        if (!_disposed) _queue.Enqueue(null);
    }

    private bool IsSeen(string tabId) => tabId == SelectedTabId && ViewVisible && ViewAtBottom;

    private void RebuildTabs()
    {
        var old = _tabs.ToDictionary(t => t.TabId, StringComparer.Ordinal);
        _tabs.Clear();
        foreach (ChatDeckTab tab in Settings.Tabs)
            _tabs.Add(old.TryGetValue(tab.Id, out var kept) ? kept : new ChatDeckTabState(tab.Id));
    }

    private static IReadOnlyList<ChatDeckRule> MarkFaulted(IReadOnlyList<ChatDeckRule> rules, HashSet<string> ids) =>
        rules.Select(r => ids.Contains(r.Id) ? r with { Faulted = true } : r).ToList();
}
