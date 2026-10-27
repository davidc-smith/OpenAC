using AcDream.Core.Chat.Deck;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>Where the Chat Deck's JSON is kept: the settings file, or a stand-in in tests.</summary>
internal interface IChatDeckStore
{
    string? Load(string toonKey);
    void Save(string toonKey, string json);
}

internal sealed class SettingsStoreChatDeckStore(SettingsStore store) : IChatDeckStore
{
    public string? Load(string toonKey) => store.LoadChatDeck(toonKey);
    public void Save(string toonKey, string json) => store.SaveChatDeck(toonKey, json);
}

/// <summary>
/// The Chat Deck's settings for one character at a time: read once at login and
/// written whenever they change. Keys this build does not know are carried from
/// the text last read or written. A save that would come out larger than the
/// loader accepts is refused, so a bad save never resets the character's tabs.
/// </summary>
internal sealed class ChatDeckStorage(IChatDeckStore? store, Action<string> log)
{
    private string? _previousJson;
    private bool _reportedOverSize;

    public ChatDeckSettings Load(string toonKey)
    {
        _reportedOverSize = false;
        _previousJson = null;
        if (store is null) return ChatDeckDefaults.Create();

        string? json = store.Load(toonKey);
        ChatDeckSettingsJson.ParseResult result = ChatDeckSettingsJson.Parse(json);
        if (result.Error is { } error)
        {
            // Left on disk as it is; the first save replaces it.
            log($"[UI] Chat Deck: settings for {toonKey} could not be read; using defaults. {error}");
            return result.Settings;
        }
        _previousJson = json;
        return result.Settings;
    }

    public bool Save(string toonKey, ChatDeckSettings settings)
    {
        if (store is null) return false;
        string json = ChatDeckSettingsJson.Serialize(settings, _previousJson);
        if (json.Length > ChatDeckLimits.MaxSettingsJsonLength)
        {
            if (!_reportedOverSize)
            {
                _reportedOverSize = true;
                log($"[UI] Chat Deck: settings for {toonKey} are larger than {ChatDeckLimits.MaxSettingsJsonLength} characters; not saved.");
            }
            return false;
        }
        try
        {
            store.Save(toonKey, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            log($"[UI] Chat Deck: saving settings for {toonKey} failed: {ex.Message}");
            return false;
        }
        _previousJson = json;
        return true;
    }
}
