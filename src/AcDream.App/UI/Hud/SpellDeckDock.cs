namespace AcDream.App.UI.Hud;

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
}

/// <summary>
/// Ties the Spell Deck to the Command Deck. Attached, the Spell Deck sits on
/// the deck's top with the deck's left edge, scale and stretch, and its rows
/// grow upwards; the deck takes the width at which the Spell Deck's rows hold its fullest tab as its minimum
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

    public SpellDeckDock(ISpellDeckDockHost host, SpellDeckController controller)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _minStretch = MinStretch;
        _topLimit = SpellHeight;
        _aboveHeightAtScale = scale => SpellDeckGeometry.Create(scale, 1f, RowCount).Height;
    }

    private int RowCount => _controller.RowCount;

    /// <summary>The narrowest stretch the Spell Deck (and, attached, the deck) may have: its rows must hold the fullest tab.</summary>
    private float MinStretch() => SpellDeckGeometry.StretchFor(_controller.MinSlotsPerRow);

    /// <summary>The attached Spell Deck's height at the deck's size.</summary>
    private float SpellHeight() => AttachedGeometry(_host.DeckGeometry, RowCount).Height;

    private static SpellDeckGeometry AttachedGeometry(DeckGeometry deck, int rows) =>
        SpellDeckGeometry.Create(deck.Scale, deck.Stretch, rows);

    /// <summary>Called every frame, whether or not the Spell Deck is shown.</summary>
    public void Tick()
    {
        SyncMode();
        if (_controller.Attached)
        {
            _host.SetDeckLimits(_minStretch, _topLimit, _aboveHeightAtScale);
            _host.EnforceDeckMinStretch();
            SpellDeckGeometry g = AttachedGeometry(_host.DeckGeometry, RowCount);
            Place(_host.DeckLeft, _host.DeckTop - g.Height, g);
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
            return _host.DeckTop - AttachedGeometry(deck, RowCount + 1).Height >= screen.Top - 1e-3f;
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
            while (fitted.Height + AttachedGeometry(fitted, rows).Height > screen.Height
                   && fitted.Scale > DeckGeometry.MinScale + 1e-4f)
            {
                fitted = DeckGeometry.Create(fitted.Scale - DeckGeometry.ScaleStep, fitted.Stretch, MinStretch());
            }
            if (fitted != deck) _host.SetDeckGeometry(fitted);

            float spellHeight = AttachedGeometry(fitted, rows).Height;
            if (_host.DeckTop - spellHeight < screen.Top)
            {
                float lowest = Math.Max(screen.Top, screen.Top + screen.Height - fitted.Height);
                _host.DeckTop = Math.Min(screen.Top + spellHeight, lowest);
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
        if (attached)
        {
            _host.SetSpellDeckCaptureOpen(false);
            return;
        }
        _host.SetDeckLimits(null, null, null);
        _host.SetSpellDeckCaptureOpen(true);
        if (_controller.DetachedRows is null)
        {
            // Never placed detached (nothing stored to go back to): just above the
            // deck as it is now, at the size it had attached. This placement is saved.
            SpellDeckGeometry first = _host.SpellDeckGeometry.WithRows(RowCount);
            first = first.WithStretch(Math.Max(first.Stretch, MinStretch()));
            Place(_host.DeckLeft, _host.DeckTop - first.Height, first, force: true);
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
