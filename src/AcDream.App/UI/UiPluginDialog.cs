using System.Numerics;

namespace AcDream.App.UI;

/// <summary>
/// The box a plugin's <c>Confirm</c> opens: a message and a row of buttons.
/// Enter picks the first button and Escape the last; every other key passes
/// on to the game. A press anywhere on it gives it the keyboard focus.
/// </summary>
internal abstract class UiPluginDialog : UiElement
{
    protected UiPluginDialog(int buttonCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(buttonCount, 1);
        ButtonCount = buttonCount;
        AcceptsFocus = true;
        ClickThrough = false;
    }

    public int ButtonCount { get; }

    /// <summary>The index Escape, the close button and every cancel report: the last button's.</summary>
    public int CancelIndex => ButtonCount - 1;

    /// <summary>Called with the index of the button picked; the host closes the dialog.</summary>
    public Action<int>? Chosen { get; set; }

    /// <summary>
    /// True once a button press (rather than a key or the host) answered the
    /// dialog. Pressing a button takes the keyboard focus off the dialog
    /// first, since buttons do not accept focus, so the host goes by this to
    /// tell that the player was on the dialog when it answered.
    /// </summary>
    public bool AnsweredByPress { get; private set; }

    protected void Choose(int index) => Chosen?.Invoke(index);

    /// <summary>Answers from a press on one of the dialog's buttons.</summary>
    protected void ChooseByPress(int index)
    {
        AnsweredByPress = true;
        Choose(index);
    }

    /// <summary>Focused, it leaves the keyboard to the game but for its answer keys.</summary>
    internal override bool HoldsKeyboard => false;

    /// <summary>Enter, keypad Enter and Escape answer it; every other key is the game's.</summary>
    internal override bool ClaimsKey(Silk.NET.Input.Key key) =>
        key is Silk.NET.Input.Key.Enter or Silk.NET.Input.Key.KeypadEnter or Silk.NET.Input.Key.Escape;

    // A held answer key must not repeat into the next queued dialog, nor
    // its char land in the field that gets the focus back.
    private void SwallowHeldKey(Silk.NET.Input.Key key) =>
        FindRoot()?.SuppressPhysicalKeyUntilRelease(key);

    /// <summary>Centres the dialog on the owner window, or on the screen without one, and keeps it on screen.</summary>
    public void Place(UiElement? owner, Vector2 screen)
    {
        Vector2 centre = screen * 0.5f;
        if (owner is not null)
            centre = owner.ScreenPosition + new Vector2(owner.Width, owner.Height) * 0.5f;
        Left = MathF.Round(Math.Clamp(centre.X - Width * 0.5f, 0f, MathF.Max(0f, screen.X - Width)));
        Top = MathF.Round(Math.Clamp(centre.Y - Height * 0.5f, 0f, MathF.Max(0f, screen.Y - Height)));
    }

    public override bool OnEvent(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.KeyDown:
                var key = (Silk.NET.Input.Key)e.Data0;
                switch (key)
                {
                    case Silk.NET.Input.Key.Enter:
                    case Silk.NET.Input.Key.KeypadEnter:
                        SwallowHeldKey(key);
                        Choose(0);
                        return true;
                    case Silk.NET.Input.Key.Escape:
                        SwallowHeldKey(key);
                        Choose(CancelIndex);
                        return true;
                    default:
                        return false;
                }
            case UiEventType.MouseDown:
            case UiEventType.RightDown:
                if (FindRoot() is { } root && !ReferenceEquals(root.KeyboardFocus, this))
                    root.SetKeyboardFocus(this);
                return true;
            case UiEventType.FocusGained:
            case UiEventType.FocusLost:
                return true;
            default:
                return base.OnEvent(in e);
        }
    }
}
