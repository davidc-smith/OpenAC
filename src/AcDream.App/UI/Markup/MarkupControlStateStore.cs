using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Every markup window's remembered control values for the logged-in
/// character, kept in memory and written to the settings file by
/// <see cref="Save"/>. The character is read on every access, so a relog
/// writes the old character's changes and loads the new one's.
/// Use it on the UI thread only.
/// </summary>
public sealed class MarkupControlStateStore
{
    private readonly SettingsStore? _store;
    private readonly Func<string> _characterKey;
    private Dictionary<MarkupControlKey, string> _values = new();
    private string? _character;
    private int _generation;
    private bool _dirty;
    private bool _loadFailed;

    public MarkupControlStateStore(SettingsStore? store, Func<string> characterKey)
    {
        _store = store;
        _characterKey = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
    }

    /// <summary>The remembered values of one window's controls.</summary>
    public IMarkupControlState ForWindow(string windowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);
        return new WindowState(this, windowName);
    }

    /// <summary>Writes the current character's values if any changed since the last save.</summary>
    public void Save()
    {
        if (!_dirty)
            return;
        if (_store is null || !CanPersist(_character))
        {
            _dirty = false;
            return;
        }
        if (_loadFailed)
            return;
        try
        {
            _store.SaveMarkupControlState(_character!, _values);
            _dirty = false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"markup controls: failed to save {_character}: {ex.Message}");
        }
    }

    // Mirrors RetailWindowLayoutPersistence.CanPersist: the host's no-character
    // key is "default" and must never be written.
    private static bool CanPersist(string? key)
        => !string.IsNullOrWhiteSpace(key)
           && !string.Equals(key, "default", StringComparison.OrdinalIgnoreCase);

    private void FollowCharacter()
    {
        string character = _characterKey() ?? string.Empty;
        if (_generation > 0 && string.Equals(character, _character, StringComparison.Ordinal))
            return;
        Save();
        if (_dirty)
            Console.WriteLine($"markup controls: unsaved changes for {_character} were dropped");
        _character = character;
        _dirty = false;
        _loadFailed = false;
        _values = new Dictionary<MarkupControlKey, string>();
        if (_store is not null && CanPersist(character)
            && !_store.TryLoadMarkupControlState(character, out _values))
        {
            _loadFailed = true;
            Console.WriteLine($"markup controls: settings could not be read; {character} will not be saved this session");
        }
        _generation++;
    }

    private int CurrentGeneration
    {
        get
        {
            FollowCharacter();
            return _generation;
        }
    }

    private string? Get(string window, string control, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        FollowCharacter();
        return _values.TryGetValue(new MarkupControlKey(window, control, kind), out string? value) ? value : null;
    }

    private void Set(string window, string control, string kind, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(value);
        FollowCharacter();
        var key = new MarkupControlKey(window, control, kind);
        if (_values.TryGetValue(key, out string? current) && string.Equals(current, value, StringComparison.Ordinal))
            return;
        _values[key] = value;
        _dirty = true;
    }

    private sealed class WindowState(MarkupControlStateStore owner, string window) : IMarkupControlState
    {
        public int Generation => owner.CurrentGeneration;
        public string? Get(string control, string kind) => owner.Get(window, control, kind);
        public void Set(string control, string kind, string value) => owner.Set(window, control, kind, value);
    }
}
