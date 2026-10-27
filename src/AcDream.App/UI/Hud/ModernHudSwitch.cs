namespace AcDream.App.UI.Hud;

/// <summary>
/// Turns Modern HUD on and off. While active, the retail vitals, side
/// vitals, combat and jump windows cannot be shown (their controllers' show
/// requests go through <see cref="GateVisibility"/> / <see cref="GatedShow"/>)
/// and the Command Deck is shown while gameplay is on screen. Turning on is
/// refused, leaving retail untouched, when the deck did not mount. The Spell
/// Deck, when it mounted, is shown with the deck only in magic combat.
/// </summary>
public sealed class ModernHudSwitch
{
    public static IReadOnlyList<string> RetailWindows { get; } =
    [
        WindowNames.Vitals,
        WindowNames.SideVitals,
        WindowNames.Combat,
        WindowNames.JumpPowerbar,
    ];

    private readonly Func<string, bool> _show;
    private readonly Func<string, bool> _hide;
    private readonly Func<bool> _deckMounted;
    private readonly Func<bool> _gameplayActive;
    private readonly Action _resyncRetail;
    private readonly Action _onDeckHidden;
    private readonly Action<string> _log;
    private readonly Func<bool> _spellDeckMounted;
    private readonly Func<bool> _magicMode;
    private readonly Action _onSpellDeckHidden;
    private bool? _requested;
    private bool _deckShown;
    private bool _spellDeckShown;
    private bool _reportedMountFailure;

    public ModernHudSwitch(
        Func<string, bool> show,
        Func<string, bool> hide,
        Func<bool> deckMounted,
        Func<bool> gameplayActive,
        Action resyncRetail,
        Action onDeckHidden,
        Action<string>? log = null,
        Func<bool>? spellDeckMounted = null,
        Func<bool>? magicMode = null,
        Action? onSpellDeckHidden = null)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _deckMounted = deckMounted ?? throw new ArgumentNullException(nameof(deckMounted));
        _gameplayActive = gameplayActive ?? throw new ArgumentNullException(nameof(gameplayActive));
        _resyncRetail = resyncRetail ?? throw new ArgumentNullException(nameof(resyncRetail));
        _onDeckHidden = onDeckHidden ?? throw new ArgumentNullException(nameof(onDeckHidden));
        _log = log ?? Console.WriteLine;
        _spellDeckMounted = spellDeckMounted ?? (() => false);
        _magicMode = magicMode ?? (() => false);
        _onSpellDeckHidden = onSpellDeckHidden ?? (() => { });
    }

    public bool Active { get; private set; }

    /// <summary>Modern HUD is on and the Spell Deck mounted: the spell keys,
    /// CAST and the spellbook's add-to-favourites go to the Spell Deck.</summary>
    public bool SpellDeckActive => Active && _spellDeckMounted();

    /// <summary>A retail controller's <c>setWindowVisible</c> delegate, gated.</summary>
    public Action<bool> GateVisibility(string window) => visible =>
    {
        if (!visible) _hide(window);
        else if (!Active) _show(window);
    };

    /// <summary>A show call that does nothing while active.</summary>
    public bool GatedShow(string window) => !Active && _show(window);

    public void Apply(bool enabled)
    {
        if (_requested == enabled) return;
        _requested = enabled;

        if (enabled)
        {
            if (!_deckMounted())
            {
                if (!_reportedMountFailure)
                {
                    _reportedMountFailure = true;
                    _log("[UI] modern HUD: Command Deck unavailable; keeping the retail windows.");
                }
                return;
            }
            Active = true;
            foreach (string window in RetailWindows) _hide(window);
            Tick();
            return;
        }

        if (!Active) return;
        Active = false;
        SetSpellDeckShown(false);
        SetDeckShown(false);
        _resyncRetail();
    }

    /// <summary>Keeps the deck shown only while active and gameplay is on screen,
    /// and the Spell Deck only while the deck is shown in magic combat.</summary>
    public void Tick()
    {
        bool gameplay = Active && _gameplayActive();
        SetDeckShown(gameplay);
        SetSpellDeckShown(gameplay && _magicMode() && _spellDeckMounted());
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

    private void SetDeckShown(bool shown)
    {
        if (shown == _deckShown) return;
        _deckShown = shown;
        if (shown)
        {
            _show(WindowNames.CommandDeck);
        }
        else
        {
            _hide(WindowNames.CommandDeck);
            _onDeckHidden();
        }
    }
}
