namespace AcDream.App.UI;

/// <summary>The look a themed control takes from the pointer and its own enabled flag.</summary>
public enum UiControlState { Normal, Hovered, Pressed, Disabled }

/// <summary>
/// Whether the pointer is over a control and whether the left button went
/// down on it. It only watches events: the control's own handling decides
/// what they do, and what it returns. A press shows as pressed only while
/// the pointer is still over the control.
/// </summary>
internal struct UiPointerState
{
    public bool Hovered { get; private set; }

    public bool Pressed { get; private set; }

    public void Observe(in UiEvent e)
    {
        switch (e.Type)
        {
            case UiEventType.HoverEnter: Hovered = true; break;
            case UiEventType.HoverLeave: Hovered = false; break;
            case UiEventType.MouseDown: Pressed = true; Hovered = true; break;
            case UiEventType.MouseUp:
            case UiEventType.CaptureChanged: Pressed = false; break;
        }
    }

    public readonly UiControlState State(bool enabled) =>
        !enabled ? UiControlState.Disabled
        : Pressed && Hovered ? UiControlState.Pressed
        : Hovered ? UiControlState.Hovered
        : UiControlState.Normal;
}
