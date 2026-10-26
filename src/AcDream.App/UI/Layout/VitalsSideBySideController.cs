using System;

namespace AcDream.App.UI.Layout;

public sealed class VitalsSideBySideController
{
    private readonly Func<string, bool> _show;
    private readonly Func<string, bool> _hide;
    private readonly Func<bool> _sideBySideVitals;
    private readonly string _stackedWindow;
    private readonly string _sideWindow;
    private bool? _applied;

    public VitalsSideBySideController(
        UiRoot root,
        Func<bool> sideBySideVitals,
        string stackedWindow,
        string sideWindow)
        : this(
            (root ?? throw new ArgumentNullException(nameof(root))).ShowWindow,
            root.HideWindow,
            sideBySideVitals,
            stackedWindow,
            sideWindow)
    {
    }

    /// <summary>Shows and hides through <paramref name="show"/> and
    /// <paramref name="hide"/>, so a caller can gate the windows (Modern HUD).</summary>
    public VitalsSideBySideController(
        Func<string, bool> show,
        Func<string, bool> hide,
        Func<bool> sideBySideVitals,
        string stackedWindow,
        string sideWindow)
    {
        _show = show ?? throw new ArgumentNullException(nameof(show));
        _hide = hide ?? throw new ArgumentNullException(nameof(hide));
        _sideBySideVitals = sideBySideVitals
            ?? throw new ArgumentNullException(nameof(sideBySideVitals));
        _stackedWindow = stackedWindow;
        _sideWindow = sideWindow;
    }

    /// <summary>The last applied bit (null before the first apply). Exposed for tests.</summary>
    public bool? Applied => _applied;

    public void Tick()
    {
        bool sideBySide = _sideBySideVitals();
        if (_applied == sideBySide) return;
        _applied = sideBySide;

        if (sideBySide)
        {
            _hide(_stackedWindow);
            _show(_sideWindow);
        }
        else
        {
            _show(_stackedWindow);
            _hide(_sideWindow);
        }
    }

    /// <summary>Forgets the applied bit and applies the current one again.</summary>
    public void SyncVisibility()
    {
        _applied = null;
        Tick();
    }
}
