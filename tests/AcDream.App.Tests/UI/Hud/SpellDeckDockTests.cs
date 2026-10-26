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
        public bool CaptureOpen = true, Resizable = true;
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
        public void SetSpellDeckResizable(bool resizable) => Resizable = resizable;
    }

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
        Assert.Equal(100f, host.SpellDeckLeft);
        Assert.Equal(600f, host.SpellDeckTop + g.Height, 3);
        Assert.False(host.CaptureOpen);
        Assert.False(host.Resizable);

        c.AddRowBelow(c.Rows[0].Id);
        dock.Tick();
        Assert.Equal(4, host.SpellDeckGeometry.Rows);
        Assert.Equal(600f, host.SpellDeckTop + host.SpellDeckGeometry.Height, 3);   // grows upwards
        Assert.Equal(600f, host.DeckTop);
    }

    [Fact]
    public void SpellDeckDock_Attached_DeckMinStretchAndTopLimitFollowTheSpellDeck()
    {
        var (dock, host, _, tabs) = Make();
        tabs[0].AddRange(Enumerable.Range(1, 20).Select(i => (uint)i));

        dock.Tick();

        Assert.NotNull(host.MinStretch);
        Assert.NotNull(host.TopLimit);
        Assert.Equal(SpellDeckGeometry.StretchFor(20), host.MinStretch!(), 4);
        Assert.Equal(host.SpellDeckGeometry.Height, host.TopLimit!(), 4);
        Assert.Equal(SpellDeckGeometry.BaseHeight(3) * host.DeckGeometry.Scale, host.TopLimit!(), 4);
        Assert.NotNull(host.AboveHeightAtScale);
        Assert.Equal(SpellDeckGeometry.BaseHeight(3) * 1.3f, host.AboveHeightAtScale!(1.3f), 3);
    }

    [Fact]
    public void SpellDeckDock_CanAddRow_FalseWhenTheNextRowWouldPassTheTop()
    {
        var (dock, host, c, _) = Make();
        host.DeckTop = SpellDeckGeometry.BaseHeight(4) - 1f;   // 3 rows fit, 4 do not
        dock.Tick();

        Assert.False(dock.CanAddRow());
        c.AddRowBelow(c.Rows[0].Id);
        Assert.Equal(3, c.Rows.Count);

        host.DeckTop = SpellDeckGeometry.BaseHeight(4);
        Assert.True(dock.CanAddRow());
        c.AddRowBelow(c.Rows[0].Id);
        Assert.Equal(4, c.Rows.Count);
    }

    [Fact]
    public void SpellDeckDock_RisingMinSlots_WidensTheDeckOnTheNextTick_EvenHidden()
    {
        // The dock has no notion of the Spell Deck being shown: it runs every tick
        // (peace, melee, missile alike), so a hidden Spell Deck still widens the deck.
        var (dock, host, _, tabs) = Make();
        dock.Tick();
        Assert.Equal(1f, host.DeckGeometry.Stretch);

        tabs[0].AddRange(Enumerable.Range(1, 20).Select(i => (uint)i));
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
        Assert.Equal(SpellDeckGeometry.BaseHeight(3), host.DeckTop, 3);
        Assert.Equal(1f, host.DeckGeometry.Scale);
        Assert.Equal(0f, host.SpellDeckTop, 3);

        // 150 + 96 > 200: steps down to 0.8 (120 + 76.8), the block at the top.
        (dock, host, _, _) = Make();
        host.Screen = (0f, 0f, 1600f, 200f);
        host.DeckTop = 50f;
        dock.FitToScreen();
        Assert.Equal(0.8f, host.DeckGeometry.Scale, 3);
        Assert.Equal(76.8f, host.DeckTop, 3);
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
        var (dock, host, c, tabs) = Make();
        dock.Tick();
        host.Stored = (300f, 200f, 588f, SpellDeckGeometry.BaseHeight(3) * 1.2f);   // scale 1.2, stretch 588/504

        dock.Detach();

        Assert.False(c.Attached);
        Assert.True(host.CaptureOpen);
        Assert.True(host.Resizable);
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
        tabs[0].AddRange(Enumerable.Range(1, 20).Select(i => (uint)i));
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
            Assert.Equal(host.DeckLeft, host.SpellDeckLeft);
            Assert.Equal(host.DeckTop, host.SpellDeckTop + host.SpellDeckGeometry.Height, 3);

            host.DeckLeft += 50f;
            host.DeckTop -= 30f;
            dock.Tick();
            Assert.Equal(host.DeckLeft, host.SpellDeckLeft);

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
        c.AddRowBelow(c.Rows[0].Id);
        c.AddRowBelow(c.Rows[0].Id);
        dock.Tick();
        Assert.Equal(5, c.Rows.Count);
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
        c.AddRowBelow(c.Rows[0].Id);
        dock.Tick();
        Assert.Equal(4, c.DetachedRows);
        Assert.Equal(1.2f, host.SpellDeckGeometry.Scale, 3);
        Assert.Equal(SpellDeckGeometry.BaseHeight(4) * 1.2f, host.Stored.Height, 2);
    }
}
