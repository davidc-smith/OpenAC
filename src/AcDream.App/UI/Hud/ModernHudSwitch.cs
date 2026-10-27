namespace AcDream.App.UI.Hud;

/// <summary>A Modern HUD window and the retail windows it stands in for.</summary>
/// <param name="Name">For the log line when it did not mount ("Command Deck").</param>
/// <param name="ResyncRetail">After Modern HUD turns off: lets the retail controllers show what they own.</param>
/// <param name="HoldsRetailVisibility">The retail windows' visibility is the player's, kept for them while
/// replaced and put back after (the chat windows); otherwise their controllers decide it (the vitals).</param>
public sealed record ModernHudReplacement(
    string Name,
    string Window,
    IReadOnlyList<string> RetailWindows,
    Func<bool> Mounted,
    Action OnShown,
    Action OnHidden,
    Action? ResyncRetail = null,
    bool HoldsRetailVisibility = false);

/// <summary>
/// Turns Modern HUD on and off. Each replacement that mounted takes over its
/// retail windows while Modern HUD is on: they cannot be shown (their
/// controllers' show requests go through <see cref="GateVisibility"/> /
/// <see cref="GatedShow"/>, and a chat window shown anyway is hidden again on
/// the next tick), and the replacement is shown while gameplay is on screen.
/// A replacement that did not mount leaves its retail windows alone; the
/// others still apply.
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
    private readonly HashSet<string> _shown = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedMountFailure = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _wasVisible = new(StringComparer.Ordinal);
    private bool? _requested;

    public ModernHudSwitch(
        Func<string, bool> show,
        Func<string, bool> hide,
        Func<string, bool> isVisible,
        Func<bool> gameplayActive,
        IReadOnlyList<ModernHudReplacement> replacements,
        Action<string>? holdVisibility = null,
        Func<string, bool>? releaseVisibility = null,
        Action<string>? log = null)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _isVisible = isVisible ?? throw new ArgumentNullException(nameof(isVisible));
        _gameplayActive = gameplayActive ?? throw new ArgumentNullException(nameof(gameplayActive));
        _replacements = replacements ?? throw new ArgumentNullException(nameof(replacements));
        _holdVisibility = holdVisibility ?? (_ => { });
        _releaseVisibility = releaseVisibility;
        _log = log ?? Console.WriteLine;
    }

    /// <summary>Modern HUD is on and at least one replacement took over.</summary>
    public bool Active => _inForce.Count > 0;

    /// <summary>Whether a replacement's window is on screen now.</summary>
    public bool IsShown(string window) => _shown.Contains(window);

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

        foreach (ModernHudReplacement r in _replacements)
        {
            if (!_inForce.Remove(r.Window)) continue;
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

    /// <summary>Keeps each replacement shown only while it is in force and gameplay is on screen.</summary>
    public void Tick()
    {
        bool gameplay = _gameplayActive();
        foreach (ModernHudReplacement r in _replacements)
        {
            bool inForce = _inForce.Contains(r.Window);
            SetShown(r, inForce && gameplay);
            if (!inForce || !r.HoldsRetailVisibility) continue;
            foreach (string window in r.RetailWindows)
                if (_isVisible(window)) _hide(window);
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
