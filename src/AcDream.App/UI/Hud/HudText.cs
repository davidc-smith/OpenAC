using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI.Hud;

/// <summary>
/// The live Modern HUD text size: the settings each deck reads its text factor from,
/// saved on every change, and <see cref="Changed"/> for the decks that cache text.
/// </summary>
internal sealed class HudText
{
    private readonly Action<HudTextSettings>? _save;

    public HudText(HudTextSettings? settings = null, Action<HudTextSettings>? save = null)
    {
        Settings = settings ?? HudTextSettings.Default;
        _save = save;
    }

    public HudTextSettings Settings { get; private set; }

    /// <summary>Raised after a real change, once it is saved.</summary>
    public event Action? Changed;

    public float FactorFor(HudDeck deck) => Settings.FactorFor(deck);

    public void Set(HudTextSettings next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (next.Equals(Settings)) return;
        Settings = next;
        _save?.Invoke(next);
        Changed?.Invoke();
    }

    public void SetGlobal(int percent) => Set(Settings.WithGlobal(percent));

    /// <summary>Sets a deck's own size; null is "Same as HUD".</summary>
    public void SetOverride(HudDeck deck, int? percent) => Set(Settings.WithOverride(deck, percent));

    /// <summary>The size the client saved last (defaults without a store); each change is saved back to it.</summary>
    public static HudText Load(SettingsStore? store, Action<string>? report = null)
    {
        if (store is null) return new HudText();
        HudTextSettings settings = HudTextSettings.FromJson(store.LoadHudText());
        return new HudText(settings, s =>
        {
            try { store.SaveHudText(s.ToJson()); }
            catch (Exception ex) { report?.Invoke($"[UI] HUD text size not saved: {ex.Message}"); }
        });
    }
}
