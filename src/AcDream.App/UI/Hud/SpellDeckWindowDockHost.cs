namespace AcDream.App.UI.Hud;

/// <summary>
/// The Spell Deck dock's view of the two real windows. Both are mounted with
/// imported chrome, so each element is its own outer frame: a size set on the
/// element is the window's size, and the frame follows the Spell Deck's height
/// as rows are added. Changes made here are reported to the window manager, so
/// the layout records them (for the Spell Deck, only while detached: its
/// capture gate is closed while attached).
/// </summary>
internal sealed class SpellDeckWindowDockHost(
    UiRoot root,
    Func<RetailWindowLayoutPersistence?> persistence,
    UiCommandDeck deck,
    RetailWindowHandle deckHandle,
    UiSpellDeck spell,
    RetailWindowHandle spellHandle) : ISpellDeckDockHost
{
    public (float Left, float Top, float Width, float Height) Screen => (0f, 0f, root.Width, root.Height);

    public DeckGeometry DeckGeometry => deck.Geometry;
    public float DeckLeft => deckHandle.Left;

    public float DeckTop
    {
        get => deckHandle.Top;
        set
        {
            UiElement frame = deckHandle.OuterFrame;
            var before = Bounds(frame);
            frame.Top = value;
            Notify(frame, before);
        }
    }

    public void SetDeckGeometry(DeckGeometry geometry)
    {
        UiElement frame = deckHandle.OuterFrame;
        var before = Bounds(frame);
        deck.ApplyGeometry(geometry);
        Notify(frame, before);
    }

    public void SetDeckLimits(Func<float>? minStretch, Func<float>? topLimit, Func<float, float>? aboveHeightAtScale)
    {
        deck.MinStretch = minStretch;
        deck.TopLimit = topLimit;
        deck.AboveHeightAtScale = aboveHeightAtScale;
    }

    public bool EnforceDeckMinStretch()
    {
        UiElement frame = deckHandle.OuterFrame;
        var before = Bounds(frame);
        if (!deck.EnforceMinStretch()) return false;
        // The deck has already taken its new size, so ResizeTo would see no
        // change; report it directly so the layout saves it.
        Notify(frame, before);
        return true;
    }

    // Square top corners only while the attached Spell Deck actually shows on the deck.
    public void SetDeckJoined(bool joined) => deck.JoinedAbove = joined ? () => spellHandle.IsVisible : null;

    public void MoveDeckDuringDrag(float left, float top)
    {
        UiElement frame = deckHandle.OuterFrame;
        frame.Left = left;
        frame.Top = top;
        frame.ResetAnchorCapture();
    }

    public void ReportDeckMoved() => root.NotifyWindowMoved(deckHandle.OuterFrame);

    public bool WindowDragActive => root.IsWindowMoveActive;

    public void SetSpellDeckDrag(WindowDragConstraint? drag) => spell.WindowDrag = drag;

    public void SetSpellDeckResize(SpellDeckResizeForward? resize) => spell.ResizeForward = resize;

    public DeckGeometry ResizeDeckDuringDrag(ResizeEdges edges, float width, float height) =>
        deck.ResizeForwarded(edges, width, height);

    public void ReportDeckResized()
    {
        deck.EndForwardedResize();
        root.NotifyWindowResized(deckHandle.OuterFrame);
    }

    public bool WindowResizeActive => root.ActiveResizeEdges != ResizeEdges.None;

    public void PlaceSpellDeck(float left, float top, SpellDeckGeometry geometry)
    {
        UiElement frame = spellHandle.OuterFrame;
        var before = Bounds(frame);
        spell.ApplyGeometry(geometry);
        frame.Left = left;
        frame.Top = top;
        // Reported even when nothing moved: the dock places only on a change or
        // to (re)set the detached placement, which the layout must then record
        // (a first detach lands exactly where the attached deck already was).
        Notify(frame, before, always: true);
    }

    public SpellDeckGeometry SpellDeckGeometry => spell.Geometry;
    public float SpellDeckLeft => spellHandle.Left;
    public float SpellDeckTop => spellHandle.Top;
    public float SpellDeckWidth => spellHandle.Width;
    public float SpellDeckHeight => spellHandle.Height;

    public void ApplyStoredSpellDeckPlacement() =>
        persistence()?.ApplyStoredPlacement(WindowNames.SpellDeck);

    // The capture gate reads the controller's attachment, which the dock has
    // already set by the time it calls this.
    public void SetSpellDeckCaptureOpen(bool open) { }

    private static (float Left, float Top, float Width, float Height) Bounds(UiElement frame) =>
        (frame.Left, frame.Top, frame.Width, frame.Height);

    /// <summary>Tells the window manager (and so the layout) that a window moved or resized in place.</summary>
    private void Notify(UiElement frame, (float Left, float Top, float Width, float Height) before, bool always = false)
    {
        bool resized = frame.Width != before.Width || frame.Height != before.Height;
        bool moved = frame.Left != before.Left || frame.Top != before.Top || (always && !resized);
        if (!moved && !resized) return;
        frame.ResetAnchorCapture();
        if (moved) root.NotifyWindowMoved(frame);
        if (resized) root.NotifyWindowResized(frame);
    }
}
