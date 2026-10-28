using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal enum ChatDeckDrawerState { Pinned, Collapsed, Open }

/// <summary>What opened the fly-out: it decides whether a send or Esc closes it again.</summary>
internal enum ChatDeckOpenReason { None, Hover, Entry, Stud }

/// <summary>One frame's view of what keeps a fly-out open.</summary>
/// <param name="Busy">A deck menu is open, the transcript is being drag-selected, or a press inside the deck holds the pointer.</param>
public readonly record struct ChatDeckDrawerInput(
    bool PointerOverStrip,
    bool PointerOverDeck,
    bool InputFocused,
    bool InputEmpty,
    bool ScrolledUp,
    bool Busy);

/// <summary>
/// Whether the Chat Deck is pinned, collapsed to its edge strip, or flown out from it.
/// Pure: time comes in through <see cref="Tick"/>, the pointer and input through
/// <see cref="ChatDeckDrawerInput"/>; the window does the placing.
/// </summary>
internal sealed class ChatDeckDrawer
{
    public const double HoverDelay = 0.3;
    public const double CloseDelay = 0.6;
    public const double SlideTime = 0.12;

    private double _hover;
    private double _away;

    public ChatDeckDrawerState State { get; private set; } = ChatDeckDrawerState.Pinned;
    public ChatDeckEdge Edge { get; private set; } = ChatDeckEdge.Left;
    public ChatDeckOpenReason OpenedBy { get; private set; }
    /// <summary>0 when a fly-out starts, 1 once it is all the way out (and always 1 otherwise).</summary>
    public double Slide { get; private set; } = 1;

    public bool Pinned => State == ChatDeckDrawerState.Pinned;
    public bool DeckVisible => State != ChatDeckDrawerState.Collapsed;
    public bool StripVisible => State != ChatDeckDrawerState.Pinned;

    /// <summary>Pinned or the edge changed: the owner saves the layout.</summary>
    public event Action? LayoutChanged;

    /// <summary>The edge a window collapses to: whichever its centre is nearer (left on a tie).</summary>
    public static ChatDeckEdge NearestEdge(float left, float width, float screenWidth) =>
        left + width / 2f <= screenWidth / 2f ? ChatDeckEdge.Left : ChatDeckEdge.Right;

    /// <summary>Takes a saved layout (at login); raises nothing.</summary>
    public void Restore(bool pinned, ChatDeckEdge edge)
    {
        Edge = edge;
        State = pinned ? ChatDeckDrawerState.Pinned : ChatDeckDrawerState.Collapsed;
        Reset();
    }

    public void Collapse(ChatDeckEdge edge)
    {
        bool changed = State == ChatDeckDrawerState.Pinned || Edge != edge;
        Edge = edge;
        State = ChatDeckDrawerState.Collapsed;
        Reset();
        if (changed) LayoutChanged?.Invoke();
    }

    public void Pin()
    {
        if (State == ChatDeckDrawerState.Pinned) return;
        State = ChatDeckDrawerState.Pinned;
        Reset();
        LayoutChanged?.Invoke();
    }

    public void Open(ChatDeckOpenReason reason)
    {
        if (State != ChatDeckDrawerState.Collapsed) return;
        State = ChatDeckDrawerState.Open;
        OpenedBy = reason;
        Slide = 0;
        _hover = 0;
        _away = 0;
    }

    public void Close()
    {
        if (State != ChatDeckDrawerState.Open) return;
        State = ChatDeckDrawerState.Collapsed;
        Reset();
    }

    /// <summary>A line was sent, or Esc left the input: a fly-out a chat-entry action opened closes at once.</summary>
    public void EntryDone()
    {
        if (State == ChatDeckDrawerState.Open && OpenedBy == ChatDeckOpenReason.Entry) Close();
    }

    public void Tick(double deltaSeconds, in ChatDeckDrawerInput input)
    {
        switch (State)
        {
            case ChatDeckDrawerState.Collapsed:
                _hover = input.PointerOverStrip ? _hover + deltaSeconds : 0;
                if (_hover >= HoverDelay) Open(ChatDeckOpenReason.Hover);
                break;
            case ChatDeckDrawerState.Open:
                Slide = Math.Min(1, Slide + deltaSeconds / SlideTime);
                bool held = input.PointerOverStrip || input.PointerOverDeck || input.InputFocused
                    || !input.InputEmpty || input.ScrolledUp || input.Busy;
                _away = held ? 0 : _away + deltaSeconds;
                if (_away >= CloseDelay) Close();
                break;
        }
    }

    private void Reset()
    {
        OpenedBy = ChatDeckOpenReason.None;
        Slide = 1;
        _hover = 0;
        _away = 0;
    }
}
