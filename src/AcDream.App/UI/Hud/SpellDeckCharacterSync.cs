using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Keeps the Spell Deck on the character in the world, in the order the
/// layout needs. A new character's rows and attachment load as soon as its
/// key appears (the layout's capture gate reads the attachment), but the dock
/// does not place the Spell Deck until a layout restore has run after that
/// load: placing it earlier would save the previous character's (or the
/// default) placement over the new character's. That holds for the same
/// character logging back in, too. A key with no saved layout of its own, or
/// no layout persistence at all, has nothing to wait for.
/// </summary>
internal sealed class SpellDeckCharacterSync
{
    private readonly SpellDeckController _controller;
    private readonly SpellDeckDock _dock;
    private readonly SettingsStore? _store;
    private readonly Func<string> _characterKey;
    /// <summary>The layout's restore count; null when there is no layout persistence.</summary>
    private readonly Func<int?> _restoreCount;
    private int? _loadedAtRestore;

    public SpellDeckCharacterSync(
        SpellDeckController controller,
        SpellDeckDock dock,
        SettingsStore? store,
        Func<string> characterKey,
        Func<int?> restoreCount)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _dock = dock ?? throw new ArgumentNullException(nameof(dock));
        _store = store;
        _characterKey = characterKey ?? throw new ArgumentNullException(nameof(characterKey));
        _restoreCount = restoreCount ?? throw new ArgumentNullException(nameof(restoreCount));
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
        _controller.Load(RetailWindowLayoutPersistence.CanPersist(key)
            ? SpellDeckSettings.Load(_store, key)
            : SpellDeckSettings.Default);
        _loadedAtRestore = _restoreCount();
        AwaitingRestore = RetailWindowLayoutPersistence.CanPersist(key) && _loadedAtRestore is not null;
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
        if (AwaitingRestore && _restoreCount() == _loadedAtRestore) return;
        AwaitingRestore = false;
        _dock.AfterRestore();
    }

    /// <summary>Saves under the loaded character, never one whose settings have not been loaded,
    /// and never under a key the layout does not persist either (blank or "default").</summary>
    public void Save(SpellDeckSettings settings)
    {
        if (LoadedCharacter is { } key && RetailWindowLayoutPersistence.CanPersist(key))
            settings.Save(_store, key);
    }
}
