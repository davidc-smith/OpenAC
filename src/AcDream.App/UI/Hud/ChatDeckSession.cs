using AcDream.App.Settings;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The Chat Deck's model for the character in the world. It starts on the first
/// tick a real character is on screen and ends when the session does (logout,
/// character switch, disconnect), so history and unread never outlive a login.
/// <see cref="Tick"/> runs every frame, shown or not: the model queues every line.
/// </summary>
internal sealed class ChatDeckSession(
    ChatLog log,
    ChatDeckStorage storage,
    Func<string> characterKey,
    Func<bool> gameplayActive,
    Action<string>? report = null,
    Func<double>? clock = null) : IDisposable
{
    /// <summary>A burst of changes (a colour drag, a held spinner) is saved once, this long after the last one.</summary>
    private const double SaveIdleSeconds = 0.5;

    private readonly Func<double> _clock = clock ?? (() => Environment.TickCount64 / 1000.0);
    private bool _dirty;
    private double _lastChange;
    private readonly Action<string> _report = report ?? Console.WriteLine;
    private string? _key;

    public ChatDeckModel? Model { get; private set; }

    /// <summary>A model started or ended; the window drops what it drew from the old one.</summary>
    public event Action? ModelChanged;

    public void Tick()
    {
        string key = characterKey();
        bool character = gameplayActive()
            && !string.IsNullOrWhiteSpace(key)
            && key != RuntimeSettingsController.DefaultToonKey;
        if (character && key != _key)
        {
            Stop();
            Start(key);
        }
        Model?.Tick();
        if (_dirty && _clock() - _lastChange >= SaveIdleSeconds) Flush();
    }

    /// <summary>The session ended; the next character (or the same one again) starts empty.</summary>
    public void EndSession()
    {
        if (Model is null)
        {
            _key = null;   // a start that failed is tried again at the next login
            return;
        }
        Stop();
        ModelChanged?.Invoke();
    }

    /// <summary>Saves the model's settings as they stand (after the caller applied new ones).</summary>
    public void Save()
    {
        if (Model is { } model && _key is { } key) storage.Save(key, model.Settings);
        _dirty = false;
    }

    private void Flush()
    {
        if (_dirty) Save();
    }

    public void Dispose() => Stop();

    private void Start(string key)
    {
        // Kept even if the model cannot start, so a failure is reported once and not retried every frame.
        _key = key;
        try
        {
            Model = new ChatDeckModel(log, storage.Load(key), key);
        }
        catch (Exception ex)
        {
            _report($"[UI] Chat Deck: could not start for {key}; the deck stays empty this session. ({ex.GetType().Name}: {ex.Message})");
            return;
        }
        Model.SettingsChanged += OnSettingsChanged;
        ModelChanged?.Invoke();
    }

    private void Stop()
    {
        Flush();
        if (Model is { } model)
        {
            model.SettingsChanged -= OnSettingsChanged;
            model.Dispose();
        }
        Model = null;
        _key = null;
    }

    private void OnSettingsChanged(ChatDeckSettings settings)
    {
        _dirty = true;
        _lastChange = _clock();
    }
}
