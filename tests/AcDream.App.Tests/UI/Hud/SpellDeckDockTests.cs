using AcDream.App.UI;
using AcDream.App.UI.Hud;

namespace AcDream.App.Tests.UI.Hud;

public sealed class SpellDeckDockTests
{
    private sealed class Actions : ISpellDeckActions
    {
        public void CastSpell(uint spellId) { }
        public void CastItemSpell(uint itemId) { }
        public void SetFavorite(int tab, int position, uint spellId) { }
        public void RemoveFavorite(int tab, uint spellId) { }
        public void ExamineSpell(uint spellId) { }
        public void ShowMessage(string text) { }
    }

    /// <summary>The deck, the Spell Deck window and its stored (gated) placement as plain numbers.</summary>
    private sealed class Host : ISpellDeckDockHost
    {
        public (float Left, float Top, float Width, float Height) Screen { get; set; } = (0f, 0f, 1600f, 900f);

        public DeckGeometry DeckGeometry { get; set; } = DeckGeometry.Default;
        public float DeckLeft { get; set; } = 100f;
        public float DeckTop { get; set; } = 700f;
        public Func<float>? MinStretch, TopLimit;
        public Func<float, float>? AboveHeightAtScale;
        public int EnforceCalls;

        public void SetDeckGeometry(DeckGeometry geometry) => DeckGeometry = geometry;
        public void SetDeckLimits(Func<float>? minStretch, Func<float>? topLimit, Func<float, float>? aboveHeightAtScale) =>
            (MinStretch, TopLimit, AboveHeightAtScale) = (minStretch, topLimit, aboveHeightAtScale);
        public bool EnforceDeckMinStretch()
        {
            EnforceCalls++;
            float min = MinStretch?.Invoke() ?? DeckGeometry.MinStretch;
            if (DeckGeometry.Stretch >= min - 1e-4f) return false;
            DeckGeometry = DeckGeometry.Create(DeckGeometry.Scale, min, min);
            return true;
        }

        public SpellDeckGeometry SpellDeckGeometry { get; set; } = SpellDeckGeometry.Create(1f, 1f, 1);
        public float SpellDeckLeft { get; set; }
        public float SpellDeckTop { get; set; }
        public float SpellDeckWidth { get; set; }
        public float SpellDeckHeight { get; set; }
        public bool CaptureOpen = true;
        public int StoredApplied;
        // The persistence's recorded placement: only follows the window while capture is open.
        public (float Left, float Top, float Width, float Height) Stored = (100f, 600f, 420f, 96f);

        public void PlaceSpellDeck(float left, float top, SpellDeckGeometry geometry)
        {
            (SpellDeckLeft, SpellDeckTop, SpellDeckGeometry) = (left, top, geometry);
            (SpellDeckWidth, SpellDeckHeight) = (geometry.Width, geometry.Height);
            if (CaptureOpen) Stored = (left, top, geometry.Width, geometry.Height);
        }

        public void ApplyStoredSpellDeckPlacement()
        {
            StoredApplied++;
            (SpellDeckLeft, SpellDeckTop, SpellDeckWidth, SpellDeckHeight) = Stored;
        }

        public void SetSpellDeckCaptureOpen(bool open) => CaptureOpen = open;

        public bool? Joined;
        public void SetDeckJoined(bool joined) => Joined = joined;

        public WindowDragConstraint? SpellDrag;
        public void SetSpellDeckDrag(WindowDragConstraint? drag) => SpellDrag = drag;

        public bool WindowDragActive { get; set; }
        public int DeckMovesReported;
        public void MoveDeckDuringDrag(float left, float top) => (DeckLeft, DeckTop) = (left, top);
        public void ReportDeckMoved() => DeckMovesReported++;

        public SpellDeckResizeForward? SpellResize;
        public void SetSpellDeckResize(SpellDeckResizeForward? resize) => SpellResize = resize;

        // The deck's own grip rules, simplified: measured from the resize start.
        public bool WindowResizeActive { get; set; }
        public DeckGeometry? ResizeFrom;
        public readonly List<(ResizeEdges Edges, float Width, float Height)> DeckResizes = [];
        public int DeckResizesReported;
        public DeckGeometry ResizeDeckDuringDrag(ResizeEdges edges, float width, float height)
        {
            DeckResizes.Add((edges, width, height));
            DeckGeometry from = ResizeFrom ??= DeckGeometry;
            DeckGeometry = (edges & ResizeEdges.Top) != 0 ? from.WithCornerSize(width, height) : from.WithWidth(width);
            return DeckGeometry;
        }
        public void ReportDeckResized()
        {
            ResizeFrom = null;
            DeckResizesReported++;
        }
    }

    /// <summary>The attached Spell Deck's spot on the deck: from the orb's left edge, on the frame's top.</summary>
    private static (float Left, float Bottom) AttachedSpot(Host host) =>
        (host.DeckLeft + 70f * host.DeckGeometry.Scale, host.DeckTop + 4f * host.DeckGeometry.Scale);

    private static (SpellDeckDock Dock, Host Host, SpellDeckController C, List<uint>[] Tabs) Make(
        bool attached = true, int? detachedRows = null, List<SpellDeckSettings>? saved = null)
    {
        var tabs = Enumerable.Range(0, 8).Select(_ => new List<uint>()).ToArray();
        var host = new Host();
        SpellDeckDock? dock = null;
        var c = new SpellDeckController(new Actions(), tab => tabs[tab], () => null, () => 12,
            SpellDeckSettings.Default with { Attached = attached, DetachedRows = detachedRows },
            s => saved?.Add(s), () => false,
            () => dock!.CanAddRow());
        dock = new SpellDeckDock(host, c);
        return (dock, host, c, tabs);
    }

    [Fact]
    public void SpellDeckDock_Attached_SitsOnTheDeckWithItsScaleStretchAndRows()
    {
        var (dock, host, c, _) = Make();
        host.DeckGeometry = DeckGeometry.Create(1.25f, 1.5f);
        host.DeckTop = 600f;

        dock.Tick();

        SpellDeckGeometry g = host.SpellDeckGeometry;
        Assert.Equal((1.25f, 1.5f, 3), (g.Scale, g.Stretch, g.Rows));
        Assert.Equal(187.5f, host.SpellDeckLeft);            // 100 + 70 x 1.25
        Assert.Equal(605f, host.SpellDeckTop + g.Height, 3);   // 600 + 4 x 1.25
        Assert.False(host.CaptureOpen);

        c.AddRow();
        dock.Tick();
        Assert.Equal(4, host.SpellDeckGeometry.Rows);
        Assert.Equal(605f, host.SpellDeckTop + host.SpellDeckGeometry.Height, 3);   // grows upwards
        Assert.Equal(600f, host.DeckTop);
    }

    [Fact]
    public void SpellDeckDock_Attached_DeckMinStretchAndTopLimitFollowTheSpellDeck()
    {
        var (dock, host, c, _) = Make();
        c.Tabs.Apply([new(0, SpellDeckLayout.FromSlots([(0, 19, 1u)]))]);   // a spell in column 20

        dock.Tick();

        Assert.NotNull(host.MinStretch);
        Assert.NotNull(host.TopLimit);
        Assert.Equal(SpellDeckGeometry.StretchFor(20), host.MinStretch!(), 4);
        // The Spell Deck overlaps the deck by the 4 points above its frame.
        Assert.Equal(host.SpellDeckGeometry.Height - 4f, host.TopLimit!(), 4);
        Assert.Equal((SpellDeckGeometry.BaseHeight(3) - 4f) * host.DeckGeometry.Scale, host.TopLimit!(), 4);
        Assert.NotNull(host.AboveHeightAtScale);
        Assert.Equal((SpellDeckGeometry.BaseHeight(3) - 4f) * 1.3f, host.AboveHeightAtScale!(1.3f), 3);
    }

    [Fact]
    public void SpellDeckDock_CanAddRow_FalseWhenTheNextRowWouldPassTheTop()
    {
        var (dock, host, c, _) = Make();
        host.DeckTop = SpellDeckGeometry.BaseHeight(4) - 4f - 1f;   // 3 rows fit, 4 do not
        dock.Tick();

        Assert.False(dock.CanAddRow());
        c.AddRow();
        Assert.Equal(3, c.RowCount);

        host.DeckTop = SpellDeckGeometry.BaseHeight(4) - 4f;
        Assert.True(dock.CanAddRow());
        c.AddRow();
        Assert.Equal(4, c.RowCount);
    }

    [Fact]
    public void SpellDeckDock_RisingMinSlots_WidensTheDeckOnTheNextTick_EvenHidden()
    {
        // The dock has no notion of the Spell Deck being shown: it runs every tick
        // (peace, melee, missile alike), so a hidden Spell Deck still widens the deck.
        var (dock, host, c, _) = Make();
        dock.Tick();
        Assert.Equal(1f, host.DeckGeometry.Stretch);

        c.Tabs.Apply([new(0, SpellDeckLayout.FromSlots([(0, 19, 1u)]))]);   // a spell in column 20
        int before = host.EnforceCalls;
        dock.Tick();

        Assert.Equal(before + 1, host.EnforceCalls);
        Assert.Equal(SpellDeckGeometry.StretchFor(20), host.DeckGeometry.Stretch, 4);
        Assert.Equal(host.DeckGeometry.Stretch, host.SpellDeckGeometry.Stretch, 4);
    }

    [Fact]
    public void SpellDeckDock_RestoreFit_MovesTheDeckDownThenLowersTheScale()
    {
        // Room above: the deck moves down just enough.
        var (dock, host, _, _) = Make();
        host.Screen = (0f, 0f, 1600f, 900f);
        host.DeckTop = 50f;
        dock.FitToScreen();
        Assert.Equal(SpellDeckGeometry.BaseHeight(3) - 4f, host.DeckTop, 3);
        Assert.Equal(1f, host.DeckGeometry.Scale);
        Assert.Equal(0f, host.SpellDeckTop, 3);

        // 150 + 114 - 4 > 215: steps down to 0.8 (120 + 91.2 - 3.2), the block at the top.
        (dock, host, _, _) = Make();
        host.Screen = (0f, 0f, 1600f, 215f);
        host.DeckTop = 50f;
        dock.FitToScreen();
        Assert.Equal(0.8f, host.DeckGeometry.Scale, 3);
        Assert.Equal(88f, host.DeckTop, 3);
        Assert.Equal(0f, host.SpellDeckTop, 3);

        // Never fits: stops at 0.75 with the deck on the bottom of the screen.
        (dock, host, _, _) = Make();
        host.Screen = (0f, 0f, 1600f, 150f);
        host.DeckTop = 10f;
        dock.FitToScreen();
        Assert.Equal(DeckGeometry.MinScale, host.DeckGeometry.Scale, 3);
        Assert.Equal(150f - host.DeckGeometry.Height, host.DeckTop, 3);
    }

    [Fact]
    public void SpellDeckDock_RestoreFit_LeavesABlockThatFitsAlone()
    {
        var (dock, host, _, _) = Make();
        host.DeckGeometry = DeckGeometry.Create(1.25f, 1.5f);
        host.DeckTop = 600f;
        dock.FitToScreen();
        Assert.Equal((1.25f, 1.5f), (host.DeckGeometry.Scale, host.DeckGeometry.Stretch));
        Assert.Equal(600f, host.DeckTop);
    }

    [Fact]
    public void SpellDeckDock_DetachAndAttach_GateLimitsAndTheSameDetachedSpot()
    {
        var (dock, host, c, _) = Make(detachedRows: 3);
        dock.Tick();
        host.Stored = (300f, 200f, 588f, SpellDeckGeometry.BaseHeight(3) * 1.2f);   // scale 1.2, stretch 588/504

        dock.Detach();

        Assert.False(c.Attached);
        Assert.True(host.CaptureOpen);
        Assert.Equal(1, host.StoredApplied);
        Assert.Null(host.MinStretch);
        Assert.Null(host.TopLimit);
        Assert.Null(host.AboveHeightAtScale);
        Assert.Equal((300f, 200f), (host.SpellDeckLeft, host.SpellDeckTop));
        Assert.Equal(SpellDeckGeometry.FromSize(588f, SpellDeckGeometry.BaseHeight(3) * 1.2f, 3), host.SpellDeckGeometry);
        Assert.Equal(1.2f, host.SpellDeckGeometry.Scale, 3);
        dock.Tick();
        Assert.Null(host.MinStretch);
        Assert.Equal((300f, 200f), (host.SpellDeckLeft, host.SpellDeckTop));

        // Detached, the Spell Deck keeps its own size but follows the rows and the minimum width.
        c.Tabs.Apply([new(0, SpellDeckLayout.FromSlots([(0, 19, 1u)]))]);   // a spell in column 20
        dock.Tick();
        Assert.Equal(SpellDeckGeometry.StretchFor(20), host.SpellDeckGeometry.Stretch, 4);
        Assert.Equal(1.2f, host.SpellDeckGeometry.Scale, 3);
        Assert.Equal(1f, host.DeckGeometry.Stretch);   // the deck is no longer held
        var detachedSpot = host.Stored;

        for (int i = 0; i < 2; i++)
        {
            dock.Attach();
            Assert.True(c.Attached);
            Assert.False(host.CaptureOpen);
            dock.Tick();
            Assert.Equal(AttachedSpot(host).Left, host.SpellDeckLeft, 3);
            Assert.Equal(AttachedSpot(host).Bottom, host.SpellDeckTop + host.SpellDeckGeometry.Height, 3);

            host.DeckLeft += 50f;
            host.DeckTop -= 30f;
            dock.Tick();
            Assert.Equal(AttachedSpot(host).Left, host.SpellDeckLeft, 3);

            dock.Detach();
            Assert.Equal((detachedSpot.Left, detachedSpot.Top), (host.SpellDeckLeft, host.SpellDeckTop));
            Assert.Equal((detachedSpot.Left, detachedSpot.Top), (host.Stored.Left, host.Stored.Top));
            Assert.Equal(detachedSpot.Width, host.Stored.Width, 2);
            Assert.Equal(detachedSpot.Height, host.Stored.Height, 2);
        }
    }

    [Fact]
    public void SpellDeckDock_RowsAddedWhileAttached_DetachKeepsTheStoredScale()
    {
        // The stored detached placement was saved with 3 rows at scale 1.2.
        var saved = new List<SpellDeckSettings>();
        var (dock, host, c, _) = Make(detachedRows: 3, saved: saved);
        host.Stored = (300f, 200f, 588f, SpellDeckGeometry.BaseHeight(3) * 1.2f);
        dock.Tick();
        c.AddRow();
        c.AddRow();
        dock.Tick();
        Assert.Equal(5, c.RowCount);
        Assert.Equal(3, c.DetachedRows);   // attached moves are not the detached placement

        dock.Detach();

        Assert.Equal(1.2f, host.SpellDeckGeometry.Scale, 3);
        Assert.Equal(5, host.SpellDeckGeometry.Rows);
        Assert.Equal(SpellDeckGeometry.BaseHeight(5) * 1.2f, host.SpellDeckHeight, 2);
        Assert.Equal(SpellDeckGeometry.BaseHeight(5) * 1.2f, host.Stored.Height, 2);
        Assert.Equal(5, c.DetachedRows);   // the stored placement now goes with 5 rows
        Assert.Equal(5, saved[^1].DetachedRows);
    }

    [Fact]
    public void SpellDeckDock_Detached_RowChangesFollowIntoDetachedRows()
    {
        var (dock, host, c, _) = Make(attached: false, detachedRows: 3);
        host.Stored = (300f, 200f, 588f, SpellDeckGeometry.BaseHeight(3) * 1.2f);
        dock.AfterRestore();
        c.AddRow();
        dock.Tick();
        Assert.Equal(4, c.DetachedRows);
        Assert.Equal(1.2f, host.SpellDeckGeometry.Scale, 3);
        Assert.Equal(SpellDeckGeometry.BaseHeight(4) * 1.2f, host.Stored.Height, 2);
    }

    [Fact]
    public void SpellDeckDock_FirstDetach_GoesJustAboveTheDeckWhereItIsNow()
    {
        // Never detached: nothing stored to go back to.
        var (dock, host, c, _) = Make();
        host.DeckGeometry = DeckGeometry.Create(1.2f, 1.25f);
        host.DeckLeft = 250f;
        host.DeckTop = 500f;
        dock.Tick();
        SpellDeckGeometry attached = host.SpellDeckGeometry;

        dock.Detach();

        Assert.Equal(0, host.StoredApplied);
        Assert.Equal(attached, host.SpellDeckGeometry);
        // Where it sat attached: from the orb's left edge, on the frame's top (scale 1.2).
        (float Left, float Top) spot = (250f + 70f * 1.2f, 500f + 4f * 1.2f - attached.Height);
        Assert.Equal(spot, (host.SpellDeckLeft, host.SpellDeckTop));
        Assert.Equal(spot, (host.Stored.Left, host.Stored.Top));   // and saved
        Assert.Equal(3, c.DetachedRows);

        // From now on it goes back to that spot.
        dock.Attach();
        host.DeckLeft = 400f;
        dock.Tick();
        dock.Detach();
        Assert.Equal(1, host.StoredApplied);
        Assert.Equal(spot, (host.SpellDeckLeft, host.SpellDeckTop));
    }

    [Fact]
    public void SpellDeckDock_FirstDetach_StaysOnScreen()
    {
        var (dock, host, _, _) = Make();
        host.DeckTop = 40f;   // the block is off the top until the detach
        dock.Tick();
        dock.Detach();
        Assert.Equal(0f, host.SpellDeckTop);
    }

    public static TheoryData<float, float> Sizes()
    {
        var data = new TheoryData<float, float>();
        foreach (float scale in new[] { 0.75f, 1f, 1.5f })
            foreach (float stretch in new[] { 1f, 1.5f, 2f })
                data.Add(scale, stretch);
        return data;
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void SpellDeckDock_Attached_SitsOnTheFrameFromTheOrbToTheRightEdge(float scale, float stretch)
    {
        var (dock, host, _, _) = Make();
        host.DeckGeometry = DeckGeometry.Create(scale, stretch);
        host.DeckLeft = 200f;
        host.DeckTop = 600f;

        dock.Tick();

        SpellDeckGeometry g = host.SpellDeckGeometry;
        Assert.Equal(200f + 70f * scale, host.SpellDeckLeft, 3);                              // the frame's left, past the orb
        Assert.Equal(200f + host.DeckGeometry.Width, host.SpellDeckLeft + g.Width, 2);        // the deck's right edge
        Assert.Equal(host.DeckGeometry.Width - 70f * scale, g.Width, 2);
        Assert.Equal((350f + 420f * (stretch - 1f)) * scale, g.Width, 2);
        Assert.Equal(600f + host.DeckGeometry.Frame.Y, host.SpellDeckTop + g.Height, 3);      // the frame's top
        Assert.Equal(600f + 4f * scale, host.SpellDeckTop + g.Height, 3);
    }

    [Fact]
    public void SpellDeckDock_DeckIsJoinedOnlyWhileAttached()
    {
        var (dock, host, _, _) = Make();
        dock.Tick();
        Assert.True(host.Joined);

        dock.Detach();
        Assert.False(host.Joined);
        dock.Tick();
        Assert.False(host.Joined);

        dock.Attach();
        dock.Tick();
        Assert.True(host.Joined);
    }

    /// <summary>The root's window drag of the Spell Deck: it proposes a spot, the deck's constraint answers.</summary>
    private static (float Left, float Top) DragSpellTo(Host host, float left, float top)
    {
        Assert.NotNull(host.SpellDrag);
        host.SpellDrag!(ref left, ref top);
        return (left, top);
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void SpellDeckDock_DraggingTheAttachedSpellDeck_MovesTheDeckBySameAmount_SavedOnce(float scale)
    {
        var (dock, host, c, _) = Make(detachedRows: 3);
        host.DeckGeometry = DeckGeometry.Create(scale, 1f);
        host.DeckLeft = 300f;
        host.DeckTop = 600f;
        dock.Tick();
        var stored = host.Stored;
        float spellLeft = host.SpellDeckLeft, spellTop = host.SpellDeckTop;

        host.WindowDragActive = true;
        var placed = DragSpellTo(host, spellLeft + 40f, spellTop - 30f);
        dock.Tick();

        Assert.Equal((340f, 570f), (host.DeckLeft, host.DeckTop));
        Assert.Equal(spellLeft + 40f, placed.Left, 3);
        Assert.Equal(spellTop - 30f, placed.Top, 3);
        Assert.Equal((placed.Left, placed.Top), (host.SpellDeckLeft, host.SpellDeckTop));
        Assert.Equal(0, host.DeckMovesReported);   // not saved on every pointer move

        DragSpellTo(host, spellLeft - 10f, spellTop + 5f);
        dock.Tick();
        Assert.Equal((290f, 605f), (host.DeckLeft, host.DeckTop));
        Assert.Equal(0, host.DeckMovesReported);

        host.WindowDragActive = false;   // the drag ends
        dock.Tick();
        Assert.Equal(1, host.DeckMovesReported);
        dock.Tick();
        Assert.Equal(1, host.DeckMovesReported);

        // The Spell Deck's own (detached) placement is untouched.
        Assert.Equal(stored, host.Stored);
        Assert.Equal(3, c.DetachedRows);
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void SpellDeckDock_DraggingTheAttachedSpellDeck_KeepsTheBlockOnScreen(float scale)
    {
        var (dock, host, _, _) = Make();
        host.DeckGeometry = DeckGeometry.Create(scale, 1.5f);
        host.DeckTop = 600f;
        dock.Tick();
        var screen = host.Screen;
        float spellHeight = host.SpellDeckGeometry.Height;
        float deckWidth = host.DeckGeometry.Width, deckHeight = host.DeckGeometry.Height;
        host.WindowDragActive = true;

        // Up and left past the corner: the Spell Deck's top on the screen's top, the deck's left on its left.
        var placed = DragSpellTo(host, -500f, -500f);
        Assert.Equal(0f, host.DeckLeft, 3);
        Assert.Equal(spellHeight - 4f * scale, host.DeckTop, 3);
        Assert.Equal(70f * scale, placed.Left, 3);
        Assert.Equal(0f, placed.Top, 3);

        // Down and right past the corner: the deck's bottom and right edge on the screen's.
        placed = DragSpellTo(host, 5000f, 5000f);
        Assert.Equal(screen.Width - deckWidth, host.DeckLeft, 3);
        Assert.Equal(screen.Height - deckHeight, host.DeckTop, 3);
        Assert.Equal(host.DeckLeft + 70f * scale, placed.Left, 3);
        Assert.Equal(host.DeckTop + 4f * scale - spellHeight, placed.Top, 3);
    }

    [Fact]
    public void SpellDeckDock_DraggingTheDetachedSpellDeck_LeavesTheDeck()
    {
        var (dock, host, _, _) = Make(attached: false, detachedRows: 3);
        dock.AfterRestore();
        host.WindowDragActive = true;

        var placed = DragSpellTo(host, 123f, 45f);
        host.WindowDragActive = false;
        dock.Tick();

        Assert.Equal((123f, 45f), placed);
        Assert.Equal((100f, 700f), (host.DeckLeft, host.DeckTop));
        Assert.Equal(0, host.DeckMovesReported);
    }

    // ── Resizing the attached block ───────────────────────────────

    /// <summary>The root's resize of the Spell Deck: it proposes a size, the dock answers.</summary>
    private static SpellDeckGeometry? ResizeSpellTo(Host host, ResizeEdges edges, float width, float height)
    {
        Assert.NotNull(host.SpellResize);
        return host.SpellResize!(edges, width, height);
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void SpellDeckDock_TheAttachedRightEdge_StretchesTheDeck_TheSpellDeckFollows_SavedOnce(float scale)
    {
        var (dock, host, c, _) = Make(detachedRows: 3);
        host.DeckGeometry = DeckGeometry.Create(scale, 1f);
        host.DeckLeft = 300f;
        host.DeckTop = 600f;
        dock.Tick();
        var stored = host.Stored;
        SpellDeckGeometry before = host.SpellDeckGeometry;

        host.WindowResizeActive = true;
        // 84 points more at this scale: stretch 1.2 (504 / 420).
        SpellDeckGeometry? answer = ResizeSpellTo(host, ResizeEdges.Right, before.Width + 84f * scale, before.Height);
        Assert.Equal((ResizeEdges.Right, 504f * scale), (host.DeckResizes[^1].Edges, host.DeckResizes[^1].Width));
        Assert.Equal(1.2f, host.DeckGeometry.Stretch, 3);
        Assert.Equal(scale, host.DeckGeometry.Scale, 3);
        Assert.NotNull(answer);
        Assert.Equal(SpellDeckGeometry.Create(scale, 1.2f, 3), answer);
        dock.Tick();
        Assert.Equal(answer, host.SpellDeckGeometry);
        Assert.Equal(AttachedSpot(host).Left, host.SpellDeckLeft, 3);
        Assert.Equal(host.DeckLeft + host.DeckGeometry.Width, host.SpellDeckLeft + host.SpellDeckGeometry.Width, 2);

        // A second move is measured from the start too.
        ResizeSpellTo(host, ResizeEdges.Right, before.Width + 42f * scale, before.Height);
        Assert.Equal(1.1f, host.DeckGeometry.Stretch, 3);
        dock.Tick();
        Assert.Equal(0, host.DeckResizesReported);   // not saved on every pointer move

        host.WindowResizeActive = false;   // the resize ends
        dock.Tick();
        Assert.Equal(1, host.DeckResizesReported);
        dock.Tick();
        Assert.Equal(1, host.DeckResizesReported);
        Assert.Equal(stored, host.Stored);   // the Spell Deck's own placement is untouched
        Assert.Equal(3, c.DetachedRows);
    }

    [Theory]
    [InlineData(0.75f, 1f)]
    [InlineData(1f, 1.25f)]
    [InlineData(1.5f, 1.25f)]
    public void SpellDeckDock_TheAttachedTopRightCorner_ScalesTheDeck_TheSpellDeckFollows(float scale, float to)
    {
        var (dock, host, _, _) = Make();
        host.DeckGeometry = DeckGeometry.Create(scale, 1.5f);
        host.DeckLeft = 300f;
        host.DeckTop = 600f;
        dock.Tick();
        SpellDeckGeometry before = host.SpellDeckGeometry;

        host.WindowResizeActive = true;
        SpellDeckGeometry? answer = ResizeSpellTo(host, ResizeEdges.Right | ResizeEdges.Top,
            before.Width * to / scale, before.Height);

        Assert.Equal(ResizeEdges.Right | ResizeEdges.Top, host.DeckResizes[^1].Edges);
        Assert.Equal((to, 1.5f), (host.DeckGeometry.Scale, host.DeckGeometry.Stretch));
        Assert.Equal(SpellDeckGeometry.Create(to, 1.5f, 3), answer);
        dock.Tick();
        Assert.Equal(answer, host.SpellDeckGeometry);
        Assert.Equal(AttachedSpot(host).Left, host.SpellDeckLeft, 3);
        Assert.Equal(AttachedSpot(host).Bottom, host.SpellDeckTop + host.SpellDeckGeometry.Height, 3);

        // From the height alone, too.
        ResizeSpellTo(host, ResizeEdges.Right | ResizeEdges.Top, before.Width, before.Height * to / scale);
        Assert.Equal(to, host.DeckGeometry.Scale, 3);
    }

    [Fact]
    public void SpellDeckDock_TheDetachedSpellDeckResizesOnItsOwn()
    {
        var (dock, host, _, _) = Make(attached: false, detachedRows: 3);
        dock.AfterRestore();
        host.WindowResizeActive = true;

        Assert.Null(ResizeSpellTo(host, ResizeEdges.Right, 600f, 114f));
        Assert.Null(ResizeSpellTo(host, ResizeEdges.Right | ResizeEdges.Top, 600f, 200f));
        host.WindowResizeActive = false;
        dock.Tick();

        Assert.Empty(host.DeckResizes);
        Assert.Equal(DeckGeometry.Default, host.DeckGeometry);
        Assert.Equal(0, host.DeckResizesReported);
    }
}
