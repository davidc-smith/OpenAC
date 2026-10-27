namespace AcDream.App.UI.Hud;

/// <summary>A window drag's proposed spot (already inside the screen); the receiver may change it.</summary>
public delegate void WindowDragConstraint(ref float left, ref float top);

/// <summary>What <see cref="SpellDeckDock"/> needs from the Command Deck, the Spell Deck window and the screen.</summary>
public interface ISpellDeckDockHost
{
    (float Left, float Top, float Width, float Height) Screen { get; }

    // Command Deck
    DeckGeometry DeckGeometry { get; }
    float DeckLeft { get; }
    float DeckTop { get; set; }
    /// <summary>Sizes the deck to <paramref name="geometry"/>, keeping its left edge (and saving it).</summary>
    void SetDeckGeometry(DeckGeometry geometry);
    /// <summary>The deck's limits while the Spell Deck is attached (all null when detached):
    /// its narrowest stretch, the highest its top may go, and the Spell Deck's height at a
    /// candidate scale (a corner scale stops where the block still fits).</summary>
    void SetDeckLimits(Func<float>? minStretch, Func<float>? topLimit, Func<float, float>? aboveHeightAtScale);
    /// <summary>Widens the deck to its minimum stretch when narrower (through the window, so it is saved). True when it widened.</summary>
    bool EnforceDeckMinStretch();
    /// <summary>Whether the attached Spell Deck sits on the deck (it then draws square top corners while the Spell Deck shows).</summary>
    void SetDeckJoined(bool joined);
    /// <summary>Moves the deck during a drag of the attached Spell Deck, without reporting
    /// it to the layout (<see cref="ReportDeckMoved"/> does that once, when the drag ends).</summary>
    void MoveDeckDuringDrag(float left, float top);
    /// <summary>Tells the layout the deck moved, so its placement is saved.</summary>
    void ReportDeckMoved();
    /// <summary>Whether a window drag is under way.</summary>
    bool WindowDragActive { get; }

    // Spell Deck window
    /// <summary>Places and sizes the window and reports it to the layout even when it did not
    /// change, so a detached placement is recorded (and saved).</summary>
    void PlaceSpellDeck(float left, float top, SpellDeckGeometry geometry);
    SpellDeckGeometry SpellDeckGeometry { get; }
    float SpellDeckLeft { get; }
    float SpellDeckTop { get; }
    /// <summary>The window's outer size (after <see cref="ApplyStoredSpellDeckPlacement"/>, the stored size).</summary>
    float SpellDeckWidth { get; }
    float SpellDeckHeight { get; }
    void ApplyStoredSpellDeckPlacement();
    void SetSpellDeckCaptureOpen(bool open);
    void SetSpellDeckResizable(bool resizable);
    /// <summary>Hands the Spell Deck window the constraint for its own window drag.</summary>
    void SetSpellDeckDrag(WindowDragConstraint? drag);
}

/// <summary>
/// Ties the Spell Deck to the Command Deck. Attached, the Spell Deck sits on
/// the deck's frame, from the orb's left edge to the deck's right edge, with the
/// deck's scale and stretch, and its rows grow upwards; dragging it moves the
/// deck (the two are one block). The deck takes the width at which the Spell Deck's rows hold its fullest tab as its minimum
/// width and keeps room above it for the Spell Deck. Detached, the Spell Deck
/// is its own window, back where it was last detached (its placement is not
/// recorded while attached); the first detach puts it just above the deck.
/// </summary>
public sealed class SpellDeckDock
{
    private readonly ISpellDeckDockHost _host;
    private readonly SpellDeckController _controller;
    private readonly Func<float> _minStretch;
    private readonly Func<float> _topLimit;
    private readonly Func<float, float> _aboveHeightAtScale;
    private bool? _mode;
    /// <summary>The deck moved during a block drag and the layout has not been told yet.</summary>
    private bool _deckDragUnreported;

    /// <summary>How far right of the deck's left edge the attached Spell Deck starts (the orb's left edge), in base points.</summary>
    internal const float AttachedInset = 32f;
    /// <summary>How far below the deck's top the attached Spell Deck's bottom sits (the frame's top), in base points.</summary>
    internal const float AttachedOverlap = 4f;

    public SpellDeckDock(ISpellDeckDockHost host, SpellDeckController controller)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _minStretch = MinStretch;
        _topLimit = () => AboveHeight(_host.DeckGeometry.Scale);
        _aboveHeightAtScale = AboveHeight;
        _host.SetSpellDeckDrag(DragAttached);
    }

    /// <summary>How far the attached Spell Deck reaches above the deck's top at a scale (its height less the overlap).</summary>
    private float AboveHeight(float scale) =>
        SpellDeckGeometry.Create(scale, 1f, RowCount).Height - AttachedOverlap * scale;

    private int RowCount => _controller.RowCount;

    /// <summary>The narrowest stretch the Spell Deck (and, attached, the deck) may have: its rows must hold the fullest tab.</summary>
    private float MinStretch() => SpellDeckGeometry.StretchFor(_controller.MinSlotsPerRow);

    private static SpellDeckGeometry AttachedGeometry(DeckGeometry deck, int rows) =>
        SpellDeckGeometry.Create(deck.Scale, deck.Stretch, rows);

    /// <summary>The attached Spell Deck's spot for a deck at (<paramref name="deckLeft"/>, <paramref name="deckTop"/>).</summary>
    private static (float Left, float Top) AttachedSpot(float deckLeft, float deckTop, DeckGeometry deck, SpellDeckGeometry g) =>
        (deckLeft + AttachedInset * deck.Scale, deckTop + AttachedOverlap * deck.Scale - g.Height);

    /// <summary>Called every frame, whether or not the Spell Deck is shown.</summary>
    public void Tick()
    {
        if (_deckDragUnreported && !_host.WindowDragActive)
        {
            _deckDragUnreported = false;
            _host.ReportDeckMoved();
        }
        SyncMode();
        if (_controller.Attached)
        {
            _host.SetDeckLimits(_minStretch, _topLimit, _aboveHeightAtScale);
            _host.EnforceDeckMinStretch();
            DeckGeometry deck = _host.DeckGeometry;
            SpellDeckGeometry g = AttachedGeometry(deck, RowCount);
            var (left, top) = AttachedSpot(_host.DeckLeft, _host.DeckTop, deck, g);
            Place(left, top, g);
        }
        else
        {
            _host.SetDeckLimits(null, null, null);
            SpellDeckGeometry current = _host.SpellDeckGeometry;
            SpellDeckGeometry g = current.WithRows(RowCount);
            g = g.WithStretch(Math.Max(g.Stretch, MinStretch()));
            if (g == current) return;
            Place(_host.SpellDeckLeft, _host.SpellDeckTop, g);
        }
    }

    /// <summary>Whether one more row fits on screen (the controller's <c>canAddRow</c>).</summary>
    public bool CanAddRow()
    {
        var screen = _host.Screen;
        if (_controller.Attached)
        {
            DeckGeometry deck = _host.DeckGeometry;
            float above = AttachedGeometry(deck, RowCount + 1).Height - AttachedOverlap * deck.Scale;
            return _host.DeckTop - above >= screen.Top - 1e-3f;
        }
        SpellDeckGeometry spell = _host.SpellDeckGeometry;
        float extra = SpellDeckGeometry.BaseHeight(RowCount + 1) * spell.Scale - spell.Height;
        return _host.SpellDeckTop - extra >= screen.Top - 1e-3f
            || _host.SpellDeckTop + spell.Height + extra <= screen.Top + screen.Height + 1e-3f;
    }

    /// <summary>After a restore or a resolution change: an attached block that does not
    /// fit moves the deck down, then lowers the deck's scale a step at a time.</summary>
    public void FitToScreen()
    {
        SyncMode();
        if (_controller.Attached)
        {
            var screen = _host.Screen;
            int rows = RowCount;
            DeckGeometry deck = _host.DeckGeometry;
            DeckGeometry fitted = deck;
            float Above(DeckGeometry d) => AttachedGeometry(d, rows).Height - AttachedOverlap * d.Scale;
            while (fitted.Height + Above(fitted) > screen.Height
                   && fitted.Scale > DeckGeometry.MinScale + 1e-4f)
            {
                fitted = DeckGeometry.Create(fitted.Scale - DeckGeometry.ScaleStep, fitted.Stretch, MinStretch());
            }
            if (fitted != deck) _host.SetDeckGeometry(fitted);

            float above = Above(fitted);
            if (_host.DeckTop - above < screen.Top)
            {
                float lowest = Math.Max(screen.Top, screen.Top + screen.Height - fitted.Height);
                _host.DeckTop = Math.Min(screen.Top + above, lowest);
            }
        }
        Tick();
    }

    /// <summary>After a layout restore (or a character load with nothing to restore):
    /// applies the attachment from scratch, as if it had just changed. A detached
    /// Spell Deck takes its restored placement, normalised to its rows, and saves
    /// it back; the restore may have resized the window without its geometry.</summary>
    public void AfterRestore()
    {
        _mode = null;
        FitToScreen();
    }

    /// <summary>The Spell Deck window's drag constraint. Attached, the drag moves the
    /// whole block: the deck goes by the same amount, kept on screen with the Spell
    /// Deck above it, and the Spell Deck stays in its spot on the deck. The deck's
    /// move is reported once the drag ends (on the next <see cref="Tick"/>); the
    /// Spell Deck's own placement is not recorded while attached. Detached, the drag
    /// is the window's own.</summary>
    public void DragAttached(ref float left, ref float top)
    {
        if (!_controller.Attached) return;
        var screen = _host.Screen;
        DeckGeometry deck = _host.DeckGeometry;
        SpellDeckGeometry g = AttachedGeometry(deck, RowCount);
        float deckLeft = left - AttachedInset * deck.Scale;
        float deckTop = top + g.Height - AttachedOverlap * deck.Scale;
        deckLeft = Math.Max(screen.Left, Math.Min(deckLeft, screen.Left + screen.Width - deck.Width));
        deckTop = Math.Min(deckTop, screen.Top + screen.Height - deck.Height);
        deckTop = Math.Max(deckTop, screen.Top + AboveHeight(deck.Scale));
        if (deckLeft != _host.DeckLeft || deckTop != _host.DeckTop)
        {
            _host.MoveDeckDuringDrag(deckLeft, deckTop);
            _deckDragUnreported = true;
        }
        (left, top) = AttachedSpot(deckLeft, deckTop, deck, g);
    }

    public void Detach()
    {
        _controller.SetAttached(false);
        SyncMode();
    }

    /// <summary>Ties the Spell Deck back to the deck; it snaps into place on the next <see cref="Tick"/>.</summary>
    public void Attach()
    {
        _controller.SetAttached(true);
        SyncMode();
    }

    /// <summary>Applies the controller's attachment to the host when it changed
    /// (a detach, an attach, or a character load).</summary>
    private void SyncMode()
    {
        bool attached = _controller.Attached;
        if (_mode == attached) return;
        _mode = attached;
        _host.SetSpellDeckResizable(!attached);
        _host.SetDeckJoined(attached);
        if (attached)
        {
            _host.SetSpellDeckCaptureOpen(false);
            return;
        }
        _host.SetDeckLimits(null, null, null);
        _host.SetSpellDeckCaptureOpen(true);
        if (_controller.DetachedRows is null)
        {
            // Never placed detached (nothing stored to go back to): where it sat on
            // the deck as it is now, at the size it had attached. This placement is saved.
            SpellDeckGeometry first = _host.SpellDeckGeometry.WithRows(RowCount);
            first = first.WithStretch(Math.Max(first.Stretch, MinStretch()));
            var (left, top) = AttachedSpot(_host.DeckLeft, _host.DeckTop, _host.DeckGeometry, first);
            Place(left, top, first, force: true);
            return;
        }
        _host.ApplyStoredSpellDeckPlacement();
        // The stored height is that of the rows it was saved with; rows added or
        // removed since (while attached) change the height, never the scale.
        int storedRows = _controller.DetachedRows.Value;
        SpellDeckGeometry g = SpellDeckGeometry.FromSize(_host.SpellDeckWidth, _host.SpellDeckHeight, storedRows)
            .WithRows(RowCount);
        g = g.WithStretch(Math.Max(g.Stretch, MinStretch()));
        Place(_host.SpellDeckLeft, _host.SpellDeckTop, g, force: true);
    }

    /// <summary>Places the Spell Deck on screen, only when something changed (a detached move is saved).</summary>
    private void Place(float left, float top, SpellDeckGeometry g, bool force = false)
    {
        bool detached = !_controller.Attached;
        if (detached)
        {
            var screen = _host.Screen;
            left = Math.Max(screen.Left, Math.Min(left, screen.Left + screen.Width - g.Width));
            top = Math.Max(screen.Top, Math.Min(top, screen.Top + screen.Height - g.Height));
        }
        if (force || left != _host.SpellDeckLeft || top != _host.SpellDeckTop || g != _host.SpellDeckGeometry)
            _host.PlaceSpellDeck(left, top, g);
        // A detached placement is saved with the window; its row count goes with it.
        if (detached) _controller.SetDetachedRows(g.Rows);
    }
}
