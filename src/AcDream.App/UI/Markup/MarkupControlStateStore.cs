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
    // Keys removed while the load is failing: the retry merge must drop them from disk too.
    private readonly HashSet<MarkupControlKey> _removedWhileFailed = new();
    private const long RetryDelayMs = 5000;
    private readonly Func<long> _clock;
    private long _retryAfterMs;
    private bool _reportedFailure;

    public MarkupControlStateStore(SettingsStore? store, Func<string> characterKey, Func<long>? clockMs = null)
    {
        _clock = clockMs ?? (() => Environment.TickCount64);
        _store = store;
        _characterKey = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
    }

    /// <summary>The remembered values of one window's controls.</summary>
    public IMarkupControlState ForWindow(string windowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowName);
        return new WindowState(this, windowName);
    }

    /// <summary>The window key for values every markup window shares, such as recent colours.</summary>
    public const string SharedWindow = "*";

    /// <summary>The values every markup window shares for the character.</summary>
    public IMarkupControlState Shared => ForWindow(SharedWindow);

    /// <summary>Writes the current character's values if any changed since the last save.</summary>
    /// <param name="ignoreBackoff">
    /// Skip the retry delay after a failure; for the final save at shutdown.
    /// </param>
    public void Save(bool ignoreBackoff = false)
    {
        if (!_dirty)
            return;
        if (!ignoreBackoff && _clock() < _retryAfterMs)
            return;
        if (_store is null || !CanPersist(_character))
        {
            _dirty = false;
            return;
        }
        if (_loadFailed)
        {
            if (!_store.TryLoadMarkupControlState(_character!, out var onDisk))
            {
                BackOff(null);
                return;
            }
            // The load failed before anything was read, so _values holds only this
            // session's changes: lay them over what is on disk now.
            foreach (var key in _removedWhileFailed)
                onDisk.Remove(key);
            foreach (var (key, value) in _values)
                onDisk[key] = value;
            Console.WriteLine($"markup controls: settings for {_character} readable again; merged {_values.Count} changes");
            _values = onDisk;
            _loadFailed = false;
            _removedWhileFailed.Clear();
            // The read problem is over; a write failure from here on is news.
            _reportedFailure = false;
        }
        try
        {
            _store.SaveMarkupControlState(_character!, _values);
            _dirty = false;
            _reportedFailure = false;
            _retryAfterMs = 0;
        }
        catch (Exception ex)
        {
            BackOff($"markup controls: failed to save {_character}: {ex.Message}");
        }
    }

    // Save runs every frame: after a failure wait before trying again, and
    // say so once rather than once per attempt.
    private void BackOff(string? message)
    {
        _retryAfterMs = _clock() + RetryDelayMs;
        if (_reportedFailure)
            return;
        _reportedFailure = true;
        if (message is not null)
            Console.WriteLine(message);
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
        Save(ignoreBackoff: true);
        if (_dirty)
            Console.WriteLine($"markup controls: unsaved changes for {_character} were dropped");
        _character = character;
        _dirty = false;
        _loadFailed = false;
        _removedWhileFailed.Clear();
        _retryAfterMs = 0;
        _reportedFailure = false;
        _values = new Dictionary<MarkupControlKey, string>();
        if (_store is not null && CanPersist(character)
            && !_store.TryLoadMarkupControlState(character, out _values))
        {
            _loadFailed = true;
            _reportedFailure = true;
            Console.WriteLine($"markup controls: settings could not be read; {character} will not be saved until they can be");
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
        _removedWhileFailed.Remove(key);
        _dirty = true;
    }

    private void Remove(string window, string control, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        FollowCharacter();
        var key = new MarkupControlKey(window, control, kind);
        bool removed = _values.Remove(key);
        if (_loadFailed)
        {
            // The key may be on disk even though this session never saw it.
            _removedWhileFailed.Add(key);
            _dirty = true;
        }
        else if (removed)
        {
            _dirty = true;
        }
    }

    private sealed class WindowState(MarkupControlStateStore owner, string window) : IMarkupControlState
    {
        public int Generation => owner.CurrentGeneration;
        public string? Get(string control, string kind) => owner.Get(window, control, kind);
        public void Set(string control, string kind, string value) => owner.Set(window, control, kind, value);
        public void Remove(string control, string kind) => owner.Remove(window, control, kind);
    }
}
