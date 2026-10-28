using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

internal sealed partial class UiChatDeck
{
    private bool? _frameDraggable;
    private readonly ChatDeckIdleFade _fade = new();
    internal float BackgroundFade => _fade.Factor;
    private (float Left, float Top)? _pinnedPlace;   // where the window sat when it last collapsed

    private void OnLinesDelivered(IReadOnlyList<ChatDeckDelivery> deliveries)
    {
        if (_model is { } m && deliveries.Any(d => d.TabIds.Contains(m.SelectedTabId))) _fade.Wake();
        if (_shown && _drawer.State == ChatDeckDrawerState.Collapsed && _model is { } model)
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

    /// <summary>
    /// Follows the model the session holds: a new one brings its saved layout, none (the session
    /// ended) puts the drawer back to pinned. The runtime calls this the moment a session starts or
    /// ends, so the capture gate and the first shown frame already match the incoming character.
    /// </summary>
    internal void SyncDrawerFromModel()
    {
        ChatDeckModel? model = _b.Model();
        if (ReferenceEquals(model, _model)) return;
        if (_model is not null)
        {
            _model.Cleared -= ResetView;
            _model.LinesDelivered -= OnLinesDelivered;
        }
        _model = model;
        if (_model is not null)
        {
            _model.Cleared += ResetView;
            _model.LinesDelivered += OnLinesDelivered;
            _drawer.Restore(_model.Settings.Layout.Pinned, _model.Settings.Layout.Edge);
            if (!_drawer.Pinned)
            {
                // The saved window's place is where the strip sits and where a pin returns to.
                _b.RestorePinnedPlacement?.Invoke();
                _pinnedPlace = (Left, Top);
            }
        }
        else
        {
            _drawer.Restore(true, ChatDeckEdge.Left);   // raises nothing: no layout to save
        }
        _peek.Clear();
        ResetView();
        ApplyDrawer();
    }

    /// <summary>
    /// The runtime calls this after every layout restore and screen reflow. Those leave a window
    /// that is not pinned where it is (its capture gate is shut), so the deck puts it back at its
    /// saved place on the screen as it is now, and the strip and any fly-out follow.
    /// </summary>
    internal void FollowLayout()
    {
        if (_drawer.Pinned) return;
        _b.RestorePinnedPlacement?.Invoke();
        _pinnedPlace = (Left, Top);
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
        ChatDeckAppearance appearance = _model?.Settings.Appearance ?? new ChatDeckAppearance();
        _fade.Tick(deltaSeconds, DeckActive(), appearance.FadeWhenIdle && _drawer.Pinned, appearance.FadeAfterSeconds);
        AfterDrawerMoved(before);
        ApplyDrawer();
    }

    /// <summary>Anything that keeps a pinned deck from fading: the pointer over it, typing, a menu, a drag-select.</summary>
    private bool DeckActive()
    {
        if (FindRoot() is not { } root) return true;
        UiElement? focus = root.KeyboardFocus;
        return Inside(Left, Top, Width, Height, root.MouseX, root.MouseY)
            || ReferenceEquals(focus, Input) || ReferenceEquals(focus, SearchField)
            || _tabMenu.IsOpen || _chipMenu.IsOpen || Transcript.IsSelecting;
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

    private void PressStripPin() => PinBack();

    internal void CollapseToEdge()
    {
        if (FindRoot() is not { } root) return;
        if (_drawer.Pinned) _pinnedPlace = (Left, Top);
        _drawer.Collapse(ChatDeckDrawer.NearestEdge(Left, Width, root.Width));
        LeaveDeck();
        ApplyDrawer();
    }

    /// <summary>
    /// Pins the window back where it sat when it was last pinned: the saved layout's place when there is
    /// one, else the place remembered at collapse. The layout then records it.
    /// </summary>
    private void PinBack()
    {
        if (_drawer.Pinned || FindRoot() is not { } root) return;
        if (_pinnedPlace is { } place)
        {
            Left = place.Left;
            Top = place.Top;
        }
        _b.RestorePinnedPlacement?.Invoke();   // the stored layout wins when it has one
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
        FindRoot()?.BringToFront(this);   // over any window already at that edge
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
        if (before == ChatDeckDrawerState.Collapsed && _drawer.State == ChatDeckDrawerState.Open)
        {
            _peek.Clear();
            FindRoot()?.BringToFront(this);
        }
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
