using System.Numerics;
using AcDream.App.UI.Markup;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.App.UI;

/// <summary>
/// A markup hotkey box: shows the bound key chord and, once clicked (or
/// Enter/Space while focused), records the next chord the player presses.
/// It reports the canonical string (<see cref="MarkupHotkeyText"/>); the
/// plugin stores it and the box shows whatever <see cref="ValueSource"/>
/// returns next frame. It holds keyboard focus while recording, so the game
/// never sees the keys.
/// </summary>
public sealed class UiMarkupHotkey : UiElement
{
    public const string PromptText = "Press a key...";
    public const string UnboundText = "None";
    public const float TextInset = 4f;

    public static readonly Vector4 ClassicBackground = new(0f, 0f, 0f, 0.9f);
    public static readonly Vector4 ClassicBorder = UiCheckLamp.UncheckedInner;
    public static readonly Vector4 ClassicText = new(0.91f, 0.87f, 0.76f, 1f);
    public static readonly Vector4 ClassicMuted = new(0.5f, 0.5f, 0.5f, 1f);
    public static readonly Vector4 FocusColor = new(1f, 0.82f, 0.25f, 1f);

    public PluginUiPalette? ThemePalette { get; set; }
    public UiDatFont? DatFont { get; set; }

    /// <summary>The bound hotkey string.</summary>
    public Func<string?>? ValueSource { get; set; }

    /// <summary>Called with the new canonical string ("" to unbind) when it differs from the bound one.</summary>
    public Action<string>? Changed { get; set; }

    /// <summary>How a chord is shown (the retail key names); the canonical string when null.</summary>
    public Func<KeyChord, string>? DescribeKey
    {
        get => _describeKey;
        set
        {
            _describeKey = value;
            _shownValid = false;
        }
    }

    /// <summary>The modifiers held now. Defaults to the root's keyboard.</summary>
    public Func<ModifierMask>? ModifiersSource { get; set; }

    public bool IsCapturing { get; private set; }
    public bool IsFocused { get; private set; }

    internal override bool CapturesAllKeys => IsCapturing;

    private bool _releaseFocus;   // a click began the capture: hand the keys back to the game after
    private Key? _startKey;       // the Enter/Space that began a keyboard capture, until it goes up
    private Key? _endKey;         // the key that ended a click capture; focus goes on its release
    private Func<KeyChord, string>? _describeKey;
    private bool _shownValid;
    private string? _shownValue;
    private string _shownText = UnboundText;
    private bool _shownBound;

    public UiMarkupHotkey()
    {
        AcceptsFocus = true;
        FocusOnMouseClick = true;
        TabStop = true;
    }

    public override bool HandlesClick => true;

    /// <summary>The prompt while capturing, else the bound chord's name or <see cref="UnboundText"/>.</summary>
    public string DisplayText
    {
        get
        {
            if (IsCapturing)
                return PromptText;
            RefreshShown();
            return _shownText;
        }
    }

    private void RefreshShown()
    {
        string? value = ValueSource?.Invoke();
        if (_shownValid && string.Equals(value, _shownValue, StringComparison.Ordinal))
            return;
        _shownValid = true;
        _shownValue = value;
        KeyChord? chord = MarkupHotkeyText.Parse(value);
        _shownBound = chord is not null;
        _shownText = chord is { } c ? DescribeKey?.Invoke(c) ?? MarkupHotkeyText.Format(c) : UnboundText;
    }

    private void Begin(bool releaseFocus, Key? startKey)
    {
        if (!Enabled || IsCapturing)
            return;
        IsCapturing = true;
        _releaseFocus = releaseFocus;
        _startKey = startKey;
        _endKey = null;
    }

    /// <summary>Stops recording; a click capture gives focus back when <paramref name="endKey"/> goes up (at once without one).</summary>
    private void End(Key? endKey)
    {
        IsCapturing = false;
        _startKey = null;
        if (!_releaseFocus)
            return;
        if (endKey is null)
            ReleaseFocus();
        else
            _endKey = endKey;
    }

    private void ReleaseFocus()
    {
        _releaseFocus = false;
        _endKey = null;
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, this))
            root.SetKeyboardFocus(null);
    }

    private void Report(string value)
    {
        if (!string.Equals(value, ValueSource?.Invoke() ?? string.Empty, StringComparison.Ordinal))
            Changed?.Invoke(value);
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.FocusGained:
                IsFocused = true;
                return true;
            case UiEventType.FocusLost:
                IsFocused = false;
                IsCapturing = false;
                _startKey = null;
                _endKey = null;
                _releaseFocus = false;
                return true;
            case UiEventType.Click:
                Begin(releaseFocus: true, startKey: null);
                return true;
            case UiEventType.KeyDown:
                return KeyDown((Key)e.Data0);
            case UiEventType.KeyUp:
                return KeyUp((Key)e.Data0);
        }
        return false;
    }

    private bool KeyDown(Key key)
    {
        if (!IsCapturing)
        {
            if (!Enabled || key is not (Key.Enter or Key.KeypadEnter or Key.Space))
                return false;
            Begin(releaseFocus: false, startKey: key);
            return true;
        }
        if (key == _startKey)
            return true;
        if (key == Key.Escape)
        {
            End(key);
            return true;
        }
        if (key is Key.Backspace or Key.Delete)
        {
            End(key);
            Report(string.Empty);
            return true;
        }
        ModifierMask mods = Modifiers();
        if (!MarkupHotkeyText.IsChordKey(key) || (mods & ModifierMask.Win) != 0)
            return true;   // a modifier alone (or Win held) is not a chord yet
        End(key);
        Report(MarkupHotkeyText.Format(new KeyChord(key, mods)));
        return true;
    }

    private bool KeyUp(Key key)
    {
        if (key == _startKey)
        {
            _startKey = null;
            return true;
        }
        if (key == _endKey)
        {
            ReleaseFocus();
            return true;
        }
        return IsCapturing;
    }

    private ModifierMask Modifiers()
    {
        if (ModifiersSource is { } source)
            return source();
        if (FindRoot()?.Keyboard is not { } keyboard)
            return ModifierMask.None;
        bool Held(Key left, Key right) => keyboard.IsKeyPressed(left) || keyboard.IsKeyPressed(right);
        ModifierMask mods = ModifierMask.None;
        if (Held(Key.ControlLeft, Key.ControlRight)) mods |= ModifierMask.Ctrl;
        if (Held(Key.AltLeft, Key.AltRight)) mods |= ModifierMask.Alt;
        if (Held(Key.ShiftLeft, Key.ShiftRight)) mods |= ModifierMask.Shift;
        if (Held(Key.SuperLeft, Key.SuperRight)) mods |= ModifierMask.Win;
        return mods;
    }

    protected override void OnTick(double deltaSeconds)
    {
        if (IsCapturing && !Enabled)
            End(endKey: null);
        // The end key's release can be lost (the window lost focus meanwhile): don't keep the keys.
        if (_endKey is { } key && FindRoot()?.Keyboard is { } keyboard && !keyboard.IsKeyPressed(key))
            ReleaseFocus();
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        PluginUiPalette? p = ThemePalette;
        Vector4 focus = p?.Accent ?? FocusColor;
        if (p is not null)
            PluginUiStyle.Field(ctx, p, Width, Height, Fade(p.Field), IsFocused || IsCapturing);
        else
        {
            ctx.DrawFill(0f, 0f, Width, Height, Fade(ClassicBackground));
            ctx.DrawRectOutline(0f, 0f, Width, Height, Fade(IsFocused || IsCapturing ? focus : ClassicBorder), 1f);
        }

        string text = DisplayText;
        Vector4 color = IsCapturing ? focus
            : _shownBound ? p?.Text ?? ClassicText
            : p?.Muted ?? ClassicMuted;
        ctx.PushClip(1f, 1f, Width - 2f, Height - 2f);   // a long name stops inside the border
        try
        {
            UiCaption.Draw(ctx, DatFont, text, TextInset, 0f, Height, Fade(color), outline: p is null);
        }
        finally
        {
            ctx.PopClip();
        }
    }

    private Vector4 Fade(Vector4 color) =>
        Enabled ? color : color with { W = color.W * UiMarkupToggle.DisabledAlpha };
}
