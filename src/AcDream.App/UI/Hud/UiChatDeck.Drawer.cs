using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private bool? _frameDraggable;

    private void OnLinesDelivered(IReadOnlyList<ChatDeckDelivery> deliveries)
    {
        if (_drawer.State == ChatDeckDrawerState.Collapsed && _model is { } model)
            _peek.Add(deliveries, model.Settings.Tabs);
    }

    /// <summary>A chat-entry action on a collapsed deck: fly out first, so the input it focuses is on screen.</summary>
    private void RevealForEntry()
    {
        if (_shown) OpenFlyout(ChatDeckOpenReason.Entry);
    }

    /// <summary>A line was sent or Esc left the input: a fly-out a chat-entry action opened closes now.</summary>
    private void EndEntry()
    {
        ChatDeckDrawerState before = _drawer.State;
        _drawer.EntryDone();
        AfterDrawerMoved(before);
        ApplyDrawer();
    }

    private void CreateDrawer() =>
        _drawer.LayoutChanged += () => _model?.SetLayout(_drawer.Pinned, _drawer.Edge);

    /// <summary>The runtime calls this every frame: a hidden window gets no OnTick.</summary>
    internal void TickDrawer(double deltaSeconds)
    {
        if (!_shown) return;
        if (!Visible) Refresh();   // the strip's badges follow the model while the window is hidden
        ChatDeckDrawerState before = _drawer.State;
        _peek.Advance(deltaSeconds);
        _drawer.Tick(deltaSeconds, ReadDrawerInput());
        AfterDrawerMoved(before);
        ApplyDrawer();
    }

    private void PressPin()
    {
        if (_drawer.Pinned) CollapseToEdge();
        else PinWhereItFliesOut();
    }

    private void PressCollapse()
    {
        if (_drawer.Pinned) CollapseToEdge();
        else CloseFlyout();
    }

    private void PressStripTab(int index)
    {
        SelectTab(index);
        OpenFlyout(ChatDeckOpenReason.Stud);
    }

    private void PressStripPin() => PinWhereItFliesOut();

    internal void CollapseToEdge()
    {
        if (FindRoot() is not { } root) return;
        _drawer.Collapse(ChatDeckDrawer.NearestEdge(Left, Width, root.Width));
        LeaveDeck();
        ApplyDrawer();
    }

    /// <summary>Pins the window where the fly-out puts it, beside the edge, and has the layout record that place.</summary>
    private void PinWhereItFliesOut()
    {
        if (_drawer.Pinned || FindRoot() is not { } root) return;
        DeckRect strip = ChatDeckStripGeometry.Place(_drawer.Edge, Top, Height, _labels.Count, root.Width, root.Height);
        Left = ChatDeckStripGeometry.FlyoutLeft(_drawer.Edge, Width, root.Width, 1);
        Top = strip.Y;
        _drawer.Pin();
        ApplyDrawer();
        ResetAnchorCapture();
        root.NotifyWindowMoved(this);   // after Pin: the capture gate is open again
    }

    private void OpenFlyout(ChatDeckOpenReason reason)
    {
        if (_drawer.State != ChatDeckDrawerState.Collapsed) return;
        _drawer.Open(reason);
        _peek.Clear();
        ApplyDrawer();
    }

    private void CloseFlyout()
    {
        ChatDeckDrawerState before = _drawer.State;
        _drawer.Close();
        AfterDrawerMoved(before);
        ApplyDrawer();
    }

    private void AfterDrawerMoved(ChatDeckDrawerState before)
    {
        if (before == ChatDeckDrawerState.Open && _drawer.State == ChatDeckDrawerState.Collapsed) LeaveDeck();
        if (before == ChatDeckDrawerState.Collapsed && _drawer.State == ChatDeckDrawerState.Open) _peek.Clear();
    }

    /// <summary>The window is leaving the screen: its menus close and its fields give the keyboard back.</summary>
    private void LeaveDeck()
    {
        _tabMenu.Close(restoreFocus: false);
        OnInputHidden();   // UiChatDeck.Input.cs: chip menu, input and search focus
    }

    /// <summary>Puts the window, the strip and the peek where the drawer's state says.</summary>
    internal void ApplyDrawer()
    {
        PlaceDrawer();
        // Every pass reports the view, so read state follows collapse and fly-out at once.
        _model?.SetView(_shown && _drawer.DeckVisible, Transcript.Scroll.AtEnd);
    }

    private void PlaceDrawer()
    {
        UiRoot? root = FindRoot();
        if (!_shown || root is null)
        {
            Strip.Visible = false;
            Peek.Visible = false;
            return;
        }

        _frameDraggable ??= Draggable;
        Resizable = _drawer.Pinned;
        Draggable = _drawer.Pinned && _frameDraggable.Value;
        Visible = _drawer.DeckVisible;
        Opacity = _drawer.State == ChatDeckDrawerState.Open ? (float)_drawer.Slide : 1f;
        Strip.Visible = _drawer.StripVisible;
        if (!_drawer.StripVisible)
        {
            Peek.Visible = false;
            return;
        }

        DeckRect s = ChatDeckStripGeometry.Place(_drawer.Edge, Top, Height, _labels.Count, root.Width, root.Height);
        Strip.Left = s.X;
        Strip.Top = s.Y;
        Strip.Width = s.W;
        Strip.Height = s.H;
        LayOutStrip();

        if (_drawer.State == ChatDeckDrawerState.Open)
        {
            Left = ChatDeckStripGeometry.FlyoutLeft(_drawer.Edge, Width, root.Width, _drawer.Slide);
            Top = s.Y;
            ResetAnchorCapture();
        }

        PlacePeek(root, s);
        Peek.Visible = _drawer.State == ChatDeckDrawerState.Collapsed && _peek.Lines.Count > 0;
    }

    private ChatDeckDrawerInput ReadDrawerInput()
    {
        if (FindRoot() is not { } root) return default;
        float x = root.MouseX;
        float y = root.MouseY;
        bool overStrip = Strip.Visible && Inside(Strip.Left, Strip.Top, Strip.Width, Strip.Height, x, y);
        bool overDeck = Visible && Inside(Left, Top, Width, Height, x, y);
        UiElement? focus = root.KeyboardFocus;
        bool focused = ReferenceEquals(focus, Input) || ReferenceEquals(focus, SearchField);
        bool busy = Transcript.IsSelecting || _tabMenu.IsOpen || _chipMenu.IsOpen || IsInside(root.Captured);
        return new ChatDeckDrawerInput(overStrip, overDeck, focused, Input.Text.Length == 0, !Transcript.Scroll.AtEnd, busy);
    }

    private static bool Inside(float left, float top, float width, float height, float x, float y) =>
        x >= left && x < left + width && y >= top && y < top + height;

    private bool IsInside(UiElement? element)
    {
        for (UiElement? e = element; e is not null; e = e.Parent)
            if (ReferenceEquals(e, this)) return true;
        return false;
    }
}
