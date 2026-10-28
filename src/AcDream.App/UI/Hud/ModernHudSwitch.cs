namespace AcDream.App.UI.Hud;

/// <summary>A Modern HUD window and the retail windows it stands in for.</summary>
/// <param name="Name">For the log line when it did not mount ("Command Deck").</param>
/// <param name="ResyncRetail">After Modern HUD turns off: lets the retail controllers show what they own.</param>
/// <param name="HoldsRetailVisibility">The retail windows' visibility is the player's, kept for them while
/// replaced and put back after (the chat windows); otherwise their controllers decide it (the vitals).</param>
/// <param name="OnDemand">Shown only while the player has it open (the Gear Deck), not whenever
/// gameplay is on screen. Opening one of its retail windows opens it instead (<see cref="ModernHudSwitch.RedirectTarget"/>).</param>
public sealed record ModernHudReplacement(
    string Name,
    string Window,
    IReadOnlyList<string> RetailWindows,
    Func<bool> Mounted,
    Action OnShown,
    Action OnHidden,
    Action? ResyncRetail = null,
    bool HoldsRetailVisibility = false,
    bool OnDemand = false);

/// <summary>
/// Turns Modern HUD on and off. Each replacement that mounted takes over its
/// retail windows while Modern HUD is on: they cannot be shown (their
/// controllers' show requests go through <see cref="GateVisibility"/> /
/// <see cref="GatedShow"/>, and a chat window shown anyway is hidden again on
/// the next tick), and the replacement is shown while gameplay is on screen.
/// A replacement that did not mount leaves its retail windows alone; the
/// others still apply. The Spell Deck, when it mounted, is shown with the
/// Command Deck only in magic combat. An on-demand replacement (the Gear Deck)
/// is shown only while open, and opening one of its retail windows redirects to it.
/// </summary>
public sealed class ModernHudSwitch
{
    public static IReadOnlyList<string> CommandDeckRetailWindows { get; } =
    [
        WindowNames.Vitals,
        WindowNames.SideVitals,
        WindowNames.Combat,
        WindowNames.JumpPowerbar,
    ];

    public static IReadOnlyList<string> ChatDeckRetailWindows { get; } =
    [
        WindowNames.Chat,
        WindowNames.ChatWindow1,
        WindowNames.ChatWindow2,
        WindowNames.ChatWindow3,
        WindowNames.ChatWindow4,
    ];

    private readonly Func<string, bool> _show;
    private readonly Func<string, bool> _hide;
    private readonly Func<string, bool> _isVisible;
    private readonly Func<bool> _gameplayActive;
    private readonly IReadOnlyList<ModernHudReplacement> _replacements;
    private readonly Action<string> _holdVisibility;
    private readonly Func<string, bool>? _releaseVisibility;
    private readonly Action<string> _log;
    private readonly HashSet<string> _inForce = new(StringComparer.Ordinal);
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedMountFailure = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _wasVisible = new(StringComparer.Ordinal);
    private readonly Func<bool> _spellDeckMounted;
    private readonly Func<bool> _magicMode;
    private readonly Action _onSpellDeckHidden;
    private bool? _requested;
    private bool _spellDeckShown;

    public ModernHudSwitch(
        Func<string, bool> show,
        Func<string, bool> hide,
        Func<string, bool> isVisible,
        Func<bool> gameplayActive,
        IReadOnlyList<ModernHudReplacement> replacements,
        Action<string>? holdVisibility = null,
        Func<string, bool>? releaseVisibility = null,
        Action<string>? log = null,
        Func<bool>? spellDeckMounted = null,
        Func<bool>? magicMode = null,
        Action? onSpellDeckHidden = null)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        _gameplayActive = gameplayActive ?? throw new ArgumentNullException(nameof(gameplayActive));
        _replacements = replacements ?? throw new ArgumentNullException(nameof(replacements));
        _holdVisibility = holdVisibility ?? (_ => { });
        _releaseVisibility = releaseVisibility;
        _log = log ?? Console.WriteLine;
        _spellDeckMounted = spellDeckMounted ?? (() => false);
        _magicMode = magicMode ?? (() => false);
        _onSpellDeckHidden = onSpellDeckHidden ?? (() => { });
    }

    /// <summary>Modern HUD is on and at least one replacement took over.</summary>
    public bool Active => _inForce.Count > 0;

    /// <summary>Whether a replacement's window is on screen now.</summary>
    public bool IsShown(string window) => _shown.Contains(window);

    /// <summary>The Command Deck is in force and the Spell Deck mounted: the spell keys,
    /// CAST and the spellbook's add-to-favourites go to the Spell Deck.</summary>
    public bool SpellDeckActive => _inForce.Contains(WindowNames.CommandDeck) && _spellDeckMounted();

    /// <summary>The Modern HUD window that stands in for <paramref name="retailWindow"/> when it
    /// is opened, or null when the retail window should open as usual.</summary>
    public string? RedirectTarget(string retailWindow)
    {
        foreach (ModernHudReplacement r in _replacements)
            if (r.OnDemand && _inForce.Contains(r.Window) && r.RetailWindows.Contains(retailWindow))
                return r.Window;
        return null;
    }

    public bool IsOpen(string window) => _open.Contains(window);

    /// <summary>Opens or closes an on-demand replacement that is in force. False for any other window.</summary>
    public bool SetOpen(string window, bool open)
    {
        if (!IsOnDemandInForce(window)) return false;
        if (open) _open.Add(window); else _open.Remove(window);
        return true;
    }

    public bool ToggleOpen(string window) => SetOpen(window, !_open.Contains(window));

    /// <summary>Closes every on-demand replacement (logout, character switch).</summary>
    public void CloseAllOnDemand() => _open.Clear();

    private bool IsOnDemandInForce(string window)
    {
        foreach (ModernHudReplacement r in _replacements)
            if (r.OnDemand && r.Window == window) return _inForce.Contains(window);
        return false;
    }

    /// <summary>A retail controller's <c>setWindowVisible</c> delegate, gated.</summary>
    public Action<bool> GateVisibility(string window) => visible =>
    {
        if (!visible) _hide(window);
        else if (!IsReplaced(window)) _show(window);
    };

    /// <summary>A show call that does nothing while the window is replaced.</summary>
    public bool GatedShow(string window) => !IsReplaced(window) && _show(window);

    private bool IsReplaced(string retailWindow) =>
        _replacements.Any(r => _inForce.Contains(r.Window) && r.RetailWindows.Contains(retailWindow));

    public void Apply(bool enabled)
    {
        if (_requested == enabled) return;
        _requested = enabled;

        if (enabled)
        {
            foreach (ModernHudReplacement r in _replacements)
            {
                if (!r.Mounted())
                {
                    if (_reportedMountFailure.Add(r.Window))
                        _log($"[UI] modern HUD: {r.Name} unavailable; keeping the retail windows.");
                    continue;
                }
                _inForce.Add(r.Window);
                foreach (string window in r.RetailWindows)
                {
                    if (r.HoldsRetailVisibility)
                    {
                        _wasVisible.TryAdd(window, _isVisible(window));
                        _holdVisibility(window);
                    }
                    _hide(window);
                }
            }
            Tick();
            return;
        }

        SetSpellDeckShown(false);
        foreach (ModernHudReplacement r in _replacements)
        {
            if (!_inForce.Remove(r.Window)) continue;
            _open.Remove(r.Window);
            SetShown(r, false);
            if (r.HoldsRetailVisibility)
            {
                foreach (string window in r.RetailWindows)
                {
                    bool was = _wasVisible.Remove(window, out bool recorded) && recorded;
                    if (_releaseVisibility?.Invoke(window) ?? was) _show(window);
                }
            }
            r.ResyncRetail?.Invoke();
        }
    }

    /// <summary>Keeps each replacement shown only while it is in force and gameplay is on screen,
    /// and the Spell Deck only while the Command Deck is shown in magic combat.</summary>
    public void Tick()
    {
        bool gameplay = _gameplayActive();
        foreach (ModernHudReplacement r in _replacements)
        {
            bool inForce = _inForce.Contains(r.Window);
            if (inForce && r.HoldsRetailVisibility)
            {
                foreach (string window in r.RetailWindows)
                {
                    if (!_isVisible(window)) continue;
                    _hide(window);
                    if (r.OnDemand) _open.Add(r.Window);      // a retail open that bypassed the redirect
                }
            }
            bool wanted = inForce && gameplay && (!r.OnDemand || _open.Contains(r.Window));
            SetShown(r, wanted);
        }
        SetSpellDeckShown(_shown.Contains(WindowNames.CommandDeck) && _magicMode() && _spellDeckMounted());
    }

    private void SetSpellDeckShown(bool shown)
    {
        if (shown == _spellDeckShown) return;
        _spellDeckShown = shown;
        if (shown)
        {
            _show(WindowNames.SpellDeck);
        }
        else
        {
            _hide(WindowNames.SpellDeck);
            _onSpellDeckHidden();
        }
    }

    private void SetShown(ModernHudReplacement r, bool shown)
    {
        if (shown == _shown.Contains(r.Window)) return;
        if (shown)
        {
            _shown.Add(r.Window);
            _show(r.Window);
            r.OnShown();
        }
        else
        {
            _shown.Remove(r.Window);
            _hide(r.Window);
            r.OnHidden();
        }
    }
}
