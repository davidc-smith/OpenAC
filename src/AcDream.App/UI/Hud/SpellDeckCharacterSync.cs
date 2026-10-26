using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Keeps the Spell Deck on the character in the world, in the order the
/// layout needs. A new character's rows and attachment load as soon as its
/// key appears (the layout's capture gate reads the attachment), but the dock
/// does not place the Spell Deck until that character's layout has been
/// restored: placing it earlier would save the previous character's (or the
/// default) placement over the new character's. A key with no saved layout
/// of its own has nothing to wait for.
/// </summary>
internal sealed class SpellDeckCharacterSync
{
    private readonly SpellDeckController _controller;
    private readonly SpellDeckDock _dock;
    private readonly SettingsStore? _store;
    private readonly Func<string> _characterKey;
    private readonly Func<string?> _restoredCharacter;

    public SpellDeckCharacterSync(
        SpellDeckController controller,
        SpellDeckDock dock,
        SettingsStore? store,
        Func<string> characterKey,
        Func<string?> restoredCharacter)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dock = dock ?? throw new ArgumentNullException(nameof(dock));
        _store = store;
        _characterKey = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
        _restoredCharacter = restoredCharacter ?? throw new ArgumentNullException(nameof(restoredCharacter));
    }

    /// <summary>The character whose settings are loaded; null before the first load.</summary>
    public string? LoadedCharacter { get; private set; }

    /// <summary>The loaded character's layout has not been restored yet; the dock waits.</summary>
    public bool AwaitingRestore { get; private set; }

    /// <summary>Loads the settings of the character now in the world, when it changed. Places nothing
    /// until that character's layout is restored.</summary>
    public void LoadIfChanged()
    {
        string key = _characterKey();
        if (key == LoadedCharacter) return;
        LoadedCharacter = key;
        _controller.Load(string.IsNullOrWhiteSpace(key)
            ? SpellDeckSettings.Default
            : SpellDeckSettings.Load(_store, key));
        AwaitingRestore = RetailWindowLayoutPersistence.CanPersist(key) && _restoredCharacter() != key;
        if (!AwaitingRestore) _dock.AfterRestore();
    }

    /// <summary>Every frame.</summary>
    public void Tick()
    {
        LoadIfChanged();
        _controller.Tick();
        if (!AwaitingRestore) _dock.Tick();
    }

    /// <summary>After a resolution change (the layout reflowed to the new screen): fits
    /// the block on screen. Unlike <see cref="AfterRestore"/> it does not re-apply the
    /// detached placement, which would save the reflow's projection over it.</summary>
    public void AfterReflow()
    {
        if (!AwaitingRestore) _dock.FitToScreen();
    }

    /// <summary>After any layout restore or named-layout load.</summary>
    public void AfterRestore()
    {
        LoadIfChanged();
        if (AwaitingRestore && _restoredCharacter() != LoadedCharacter) return;
        AwaitingRestore = false;
        _dock.AfterRestore();
    }

    /// <summary>Saves under the loaded character, never one whose settings have not been loaded.</summary>
    public void Save(SpellDeckSettings settings)
    {
        if (LoadedCharacter is { } key && !string.IsNullOrWhiteSpace(key))
            settings.Save(_store, key);
    }
}
