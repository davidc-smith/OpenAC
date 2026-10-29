using AcDream.App.Settings;

namespace AcDream.App.UI.Hud;

internal interface IGearDeckStore { string? Load(string toonKey); void Save(string toonKey, string json); }

internal sealed class SettingsStoreGearDeckStore(AcDream.UI.Abstractions.Panels.Settings.SettingsStore store) : IGearDeckStore
{
    public string? Load(string toonKey) => store.LoadGearDeck(toonKey);
    public void Save(string toonKey, string json) => store.SaveGearDeck(toonKey, json);
}

/// <summary>Loads and saves the Gear Deck's settings for the logged-in character, coalescing saves
/// over <see cref="SaveIdleSeconds"/> like ChatDeckSession.</summary>
internal sealed class GearDeckSession(IGearDeckStore? store, Func<string> characterKey, Func<bool> gameplayActive,
    Func<double>? clock = null, Action<string>? report = null)
{
    public const double SaveIdleSeconds = 0.5;
    private readonly Func<double> _clock = clock ?? (() => Environment.TickCount64 / 1000.0);
    private string? _key;
    private bool _dirty;
    private double _lastChange;

    public GearDeckSettings Settings { get; private set; } = GearDeckSettings.Default;
    public event Action? Changed;
    public event Action? Ended;

    public void Tick()
    {
        string key = characterKey();
        bool real = gameplayActive() && !string.IsNullOrWhiteSpace(key) && key != RuntimeSettingsController.DefaultToonKey;
        if (real && key != _key)
        {
            Flush();
            _key = key;
            Settings = GearDeckSettings.FromJson(SafeLoad(key));
            Changed?.Invoke();
        }
        TickSave();
    }

    /// <summary>
    /// Writes a pending change once it has been idle <see cref="SaveIdleSeconds"/>. The
    /// runtime calls it every frame, so a change made just before the deck closes is still
    /// saved; it reads no key and never loads (a change made before a character loaded has
    /// no key and is dropped by <see cref="Flush"/>, as before).
    /// </summary>
    public void TickSave()
    {
        if (_dirty && _clock() - _lastChange >= SaveIdleSeconds) Flush();
    }

    public void Update(Func<GearDeckSettings, GearDeckSettings> change)
    {
        GearDeckSettings next = change(Settings);
        if (next.Equals(Settings)) return;
        Settings = next;
        _dirty = true;
        _lastChange = _clock();
        Changed?.Invoke();
    }

    public void EndSession()
    {
        Flush();
        _key = null;
        Settings = GearDeckSettings.Default;
        Changed?.Invoke();
        Ended?.Invoke();
    }

    private void Flush()
    {
        if (!_dirty || _key is null || store is null) { _dirty = false; return; }
        try { store.Save(_key, Settings.ToJson()); }
        catch (Exception ex) { report?.Invoke($"[gear-deck] save failed: {ex.Message}"); }
        _dirty = false;
    }

    private string? SafeLoad(string key)
    {
        try { return store?.Load(key); }
        catch (Exception ex) { report?.Invoke($"[gear-deck] load failed: {ex.Message}"); return null; }
    }
}
