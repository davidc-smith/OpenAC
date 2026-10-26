using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Combat;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiCommandDeckResizeTests
{
    private sealed class NoActions : ICommandDeckActions
    {
        public void ToggleCombat() { }
        public void PressAttack(AttackHeight height) { }
        public void ReleaseAttack() { }
        public void SetDesiredPower(float power) { }
        public void SetOption(CharacterOptionId option, bool value) { }
        public void Recall(RuntimePortalCommand command) { }
        public void CastOutlined() { }
    }

    private const float DeckLeft = 100f, DeckTop = 200f;

    /// <summary>A deck mounted the way <c>RetailUiRuntime.MountCommandDeck</c> mounts it.</summary>
    private static (UiRoot Root, UiCommandDeck Deck) Mount(float rootWidth = 1600, float rootHeight = 900)
    {
        var root = new UiRoot { Width = rootWidth, Height = rootHeight };
        var input = default(CommandDeckInput) with { Mode = CombatMode.Melee, SessionKey = 1 };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 0.0);
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0), UiCommandDeck.MountOptions(DeckLeft, DeckTop) with { Visible = true });
        deck.Refresh();
        return (root, deck);
    }

    private static int X(float local) => (int)(DeckLeft + local);
    private static int Y(float local) => (int)(DeckTop + local);

    [Fact]
    public void MinStretch_StopsANarrowingDrag()
    {
        var (_, deck) = Mount();
        deck.ApplyGeometry(DeckGeometry.Create(1f, 1.8f));
        deck.MinStretch = () => 1.5f;
        float w = DeckGeometry.BaseWidth * 1.1f, h = deck.Height;
        deck.ConstrainResize(ResizeEdges.Right, ref w, ref h);
        Assert.Equal(1.5f, deck.Geometry.Stretch, 3);
    }

    [Fact]
    public void MinStretch_HoldsOnARestoredSize()
    {
        var (_, deck) = Mount();
        deck.MinStretch = () => 1.5f;
        deck.Width = 500f;   // a narrower restored size (stretch about 1.19)
        deck.Refresh();
        Assert.Equal(1.5f, deck.Geometry.Stretch, 3);
        Assert.Equal(deck.Geometry.Width, deck.Width);
    }

    [Fact]
    public void EnforceMinStretch_WidensAtOnce()
    {
        var (_, deck) = Mount();
        deck.MinStretch = () => 1.25f;
        Assert.True(deck.EnforceMinStretch());
        Assert.Equal(1.25f, deck.Geometry.Stretch, 3);
        Assert.Equal(deck.Geometry.Width, deck.Width);
        Assert.False(deck.EnforceMinStretch());
    }

    [Fact]
    public void EnforceMinStretch_PullsLeftWhenTheScreenForcesIt()
    {
        var (_, deck) = Mount(700, 400);
        deck.Left = 400f;
        deck.MinStretch = () => 1.5f;   // 630 wide
        Assert.True(deck.EnforceMinStretch());
        Assert.Equal(700f - deck.Width, deck.Left, 2);
    }

    [Fact]
    public void TopLimit_StopsAWindowDrag()
    {
        // Imported chrome: the deck is its own outer frame, so the root's drag
        // constrains the deck itself.
        var (root, deck) = Mount();
        deck.TopLimit = () => 120f;
        DeckRect bar = DeckGeometry.Default.HealthBar;   // a bar drags the window
        int px = X(bar.X + bar.W / 2), py = Y(bar.Y + bar.H / 2);
        root.OnMouseMove(px, py);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        root.OnMouseMove(px, py - 150);
        root.OnMouseUp(UiMouseButton.Left, px, py - 150);
        Assert.Equal(120f, deck.Top);
    }

    [Fact]
    public void MountedDeckIsResizableOverTheFullRange()
    {
        var (_, deck) = Mount();
        Assert.True(deck.Resizable && deck.ResizeX && deck.ResizeY);
        Assert.Equal(ResizeEdges.Right, deck.ResizableEdges);
        Assert.Equal((315f, 112.5f), (deck.MinWidth, deck.MinHeight));
        Assert.Equal((1260f, 225f), (deck.MaxWidth, deck.MaxHeight));
        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
    }

    [Fact]
    public void RightEdgeStretchesTheWidthOnly()
    {
        var (root, deck) = Mount();
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(75));
        root.OnMouseMove(X(418) + 200, Y(75) + 40);
        root.OnMouseUp(UiMouseButton.Left, X(418) + 200, Y(75) + 40);

        Assert.Equal(620f, deck.Width, 1);
        Assert.Equal(150f, deck.Height);
        Assert.Equal(1f, deck.Geometry.Scale);
        Assert.Equal(620f / 420f, deck.Geometry.Stretch, 3);
    }

    [Fact]
    public void RightEdgeStopsAtTwiceTheWidth()
    {
        var (root, deck) = Mount();
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(75));
        root.OnMouseMove(X(418) + 900, Y(75));
        root.OnMouseUp(UiMouseButton.Left, X(418) + 900, Y(75));
        Assert.Equal(840f, deck.Width, 1);
    }

    [Fact]
    public void BottomRightCornerScales_KeepsStretch_AndTopLeftStays()
    {
        var (root, deck) = Mount();
        deck.ApplyGeometry(DeckGeometry.Create(1f, 1.5f));   // 630 x 150
        root.OnMouseDown(UiMouseButton.Left, X(628), Y(148));
        root.OnMouseMove(X(628), Y(148) + 39);               // proposed height 189 -> scale 1.25 (1.26 snaps)
        root.OnMouseUp(UiMouseButton.Left, X(628), Y(148) + 39);

        Assert.Equal(1.25f, deck.Geometry.Scale, 3);
        Assert.Equal(1.5f, deck.Geometry.Stretch, 3);
        Assert.Equal(787.5f, deck.Width, 1);
        Assert.Equal(187.5f, deck.Height, 1);
        Assert.Equal((DeckLeft, DeckTop), (deck.Left, deck.Top));
    }

    [Fact]
    public void TopRightCornerScales_AndTheBottomStays()
    {
        var (root, deck) = Mount();
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(1));
        root.OnMouseMove(X(418), Y(1) - 60);                  // proposed height 210 -> scale 1.4
        root.OnMouseUp(UiMouseButton.Left, X(418), Y(1) - 60);

        Assert.Equal(1.4f, deck.Geometry.Scale, 3);
        Assert.Equal(DeckTop + 150f, deck.Top + deck.Height, 1);
        Assert.Equal(DeckLeft, deck.Left);
    }

    [Fact]
    public void CornerLocksOntoOneHundredPercent()
    {
        var (root, deck) = Mount();
        deck.ApplyGeometry(DeckGeometry.Create(1.25f, 1f));
        root.OnMouseDown(UiMouseButton.Left, X(deck.Width - 2), Y(deck.Height - 2));
        root.OnMouseMove(X(deck.Width - 2), Y(deck.Height - 2) - 34);   // 153.5 / 150 = 1.023
        root.OnMouseUp(UiMouseButton.Left, X(deck.Width - 2), Y(deck.Height - 2) - 34);
        Assert.Equal(1f, deck.Geometry.Scale);
        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
    }

    /// <summary>One step of the scale in points along an axis of <paramref name="deck"/>.</summary>
    private static float StepW(UiCommandDeck deck) => DeckGeometry.BaseWidth * deck.Geometry.Stretch * DeckGeometry.ScaleStep;
    private const float StepH = DeckGeometry.BaseHeight * DeckGeometry.ScaleStep;

    [Fact]
    public void BottomRightCorner_VerticalDragFollowsThePointerOnEveryMove()
    {
        var (root, deck) = Mount();
        int px = X(418), py = Y(148);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        (int Dy, float Scale)[] moves = [(15, 1.1f), (30, 1.2f), (45, 1.3f), (45, 1.3f), (60, 1.4f), (30, 1.2f)];
        foreach (var (dy, scale) in moves)
        {
            root.OnMouseMove(px, py + dy);
            Assert.Equal(scale, deck.Geometry.Scale, 3);
            Assert.Equal(1f, deck.Geometry.Stretch, 3);
            Assert.Equal(150f * scale, deck.Height, 1);
            Assert.Equal((DeckLeft, DeckTop), (deck.Left, deck.Top));
            Assert.True(MathF.Abs(deck.Top + deck.Height - (py + dy)) <= StepH, $"dy {dy}: bottom {deck.Top + deck.Height}");
        }
        root.OnMouseUp(UiMouseButton.Left, px, py + 30);
        Assert.Equal(1.2f, deck.Geometry.Scale, 3);
    }

    [Fact]
    public void TopRightCorner_VerticalDragFollowsThePointerOnEveryMove()
    {
        var (root, deck) = Mount();
        int px = X(418), py = Y(1);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        (int Dy, float Scale)[] moves = [(-15, 1.1f), (-30, 1.2f), (-45, 1.3f), (-45, 1.3f), (-60, 1.4f), (-30, 1.2f)];
        foreach (var (dy, scale) in moves)
        {
            root.OnMouseMove(px, py + dy);
            Assert.Equal(scale, deck.Geometry.Scale, 3);
            Assert.Equal(DeckTop + 150f, deck.Top + deck.Height, 1);
            Assert.True(MathF.Abs(deck.Top - (py + dy)) <= StepH, $"dy {dy}: top {deck.Top}");
        }
        root.OnMouseUp(UiMouseButton.Left, px, py - 30);
    }

    [Fact]
    public void BottomRightCorner_HorizontalDragFollowsThePointerOnEveryMove()
    {
        var (root, deck) = Mount();
        int px = X(418), py = Y(148);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        (int Dx, float Scale)[] moves = [(42, 1.1f), (84, 1.2f), (126, 1.3f), (126, 1.3f), (168, 1.4f), (84, 1.2f)];
        foreach (var (dx, scale) in moves)
        {
            root.OnMouseMove(px + dx, py);
            Assert.Equal(scale, deck.Geometry.Scale, 3);
            Assert.Equal(1f, deck.Geometry.Stretch, 3);
            Assert.Equal(420f * scale, deck.Width, 1);
            Assert.True(MathF.Abs(deck.Left + deck.Width - (px + dx)) <= StepW(deck), $"dx {dx}: right {deck.Left + deck.Width}");
        }
        root.OnMouseUp(UiMouseButton.Left, px + 84, py);
    }

    [Fact]
    public void BottomRightCorner_DiagonalDragFollowsTheLeadingAxisOnEveryMove()
    {
        var (root, deck) = Mount();
        deck.ApplyGeometry(DeckGeometry.Create(1f, 1.5f));   // 630 x 150
        int px = X(628), py = Y(148);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        // Height leads: dy / 150 grows faster than dx / 630.
        (int Dx, int Dy, float Scale)[] moves = [(20, 15, 1.1f), (40, 30, 1.2f), (60, 45, 1.3f), (60, 45, 1.3f), (80, 60, 1.4f), (40, 30, 1.2f)];
        foreach (var (dx, dy, scale) in moves)
        {
            root.OnMouseMove(px + dx, py + dy);
            Assert.Equal(scale, deck.Geometry.Scale, 3);
            Assert.Equal(1.5f, deck.Geometry.Stretch, 3);
            Assert.Equal(630f * scale, deck.Width, 1);
            Assert.True(MathF.Abs(deck.Top + deck.Height - (py + dy)) <= StepH, $"dy {dy}: bottom {deck.Top + deck.Height}");
        }
        root.OnMouseUp(UiMouseButton.Left, px + 40, py + 30);
    }

    [Fact]
    public void RightEdge_TracksThePointerOneToOneOnEveryMove()
    {
        var (root, deck) = Mount();
        deck.ApplyGeometry(DeckGeometry.Create(1.25f, 1f));   // 525 x 187.5
        int px = X(523), py = Y(90);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        foreach (int dx in new[] { 50, 100, 150, 150, 80, 10 })
        {
            root.OnMouseMove(px + dx, py + 7);
            Assert.Equal(525f + dx, deck.Width, 1);
            Assert.Equal(187.5f, deck.Height, 1);
            Assert.Equal(1.25f, deck.Geometry.Scale, 3);
            Assert.True(MathF.Abs(deck.Left + deck.Width - (px + dx)) <= 2f, $"dx {dx}: right {deck.Left + deck.Width}");
        }
        root.OnMouseUp(UiMouseButton.Left, px + 10, py);
    }

    [Fact]
    public void ANewDragStartsFromTheScaleTheLastOneEndedAt()
    {
        var (root, deck) = Mount();
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(148));
        root.OnMouseMove(X(418), Y(148) + 45);                 // 1.3
        root.OnMouseUp(UiMouseButton.Left, X(418), Y(148) + 45);
        deck.Refresh();
        Assert.Equal(1.3f, deck.Geometry.Scale, 3);

        // 546 x 195 now; a horizontal drag measures against 1.3, not 1.0.
        int px = X(544), py = Y(193);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        root.OnMouseMove(px + 42, py);                          // 588 / 420 = 1.4
        Assert.Equal(1.4f, deck.Geometry.Scale, 3);
        root.OnMouseMove(px + 42, py);
        Assert.Equal(1.4f, deck.Geometry.Scale, 3);
        root.OnMouseUp(UiMouseButton.Left, px + 42, py);
    }

    private static void AssertInside(UiRoot root, UiCommandDeck deck)
    {
        Assert.True(deck.Left >= 0 && deck.Top >= 0, $"at {deck.Left},{deck.Top}");
        Assert.True(deck.Left + deck.Width <= root.Width + 0.01f, $"right {deck.Left + deck.Width} > {root.Width}");
        Assert.True(deck.Top + deck.Height <= root.Height + 0.01f, $"bottom {deck.Top + deck.Height} > {root.Height}");
        Assert.Equal((deck.Geometry.Width, deck.Geometry.Height), (deck.Width, deck.Height));
    }

    [Fact]
    public void MountedDeckIsLimitedToTheScreenWhileResizing() =>
        Assert.True(UiCommandDeck.MountOptions(0, 0).ConstrainResizeToParent);

    [Theory]
    [InlineData(0, 100, 1.3f)]     // down: 200 of room is 1.33, the step under it is 1.3
    [InlineData(300, 0, 1.3f)]     // right: 600 of room is 1.43, but 1.4 is too tall
    [InlineData(300, 100, 1.3f)]
    public void BottomRightCornerStaysOnASmallScreen(int dx, int dy, float scale)
    {
        var (root, deck) = Mount(700, 400);   // 600 x 200 of room right of and under the deck
        int px = X(418), py = Y(148);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        root.OnMouseMove(px + dx, py + dy);
        AssertInside(root, deck);
        Assert.Equal(scale, deck.Geometry.Scale, 3);
        root.OnMouseMove(px + dx + 50, py + dy + 50);
        root.OnMouseUp(UiMouseButton.Left, px + dx + 50, py + dy + 50);
        AssertInside(root, deck);
        Assert.Equal(scale, deck.Geometry.Scale, 3);
    }

    [Fact]
    public void TopRightCornerStaysOnTheScreen()
    {
        var (root, deck) = Mount();
        deck.Top = 50f;                       // bottom at 200: 200 of room above it
        int px = X(418), py = (int)(50f + 1f);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        root.OnMouseMove(px, py - 200);
        root.OnMouseUp(UiMouseButton.Left, px, py - 200);
        AssertInside(root, deck);
        Assert.Equal(1.3f, deck.Geometry.Scale, 3);
        Assert.Equal(200f, deck.Top + deck.Height, 1);
    }

    [Fact]
    public void RightEdgeStaysOnASmallScreen()
    {
        var (root, deck) = Mount(700, 900);   // 600 of room right of the deck
        deck.ApplyGeometry(DeckGeometry.Create(1.5f, 1f));   // 630: already past the edge
        int px = X(628), py = Y(110);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        root.OnMouseMove(px + 100, py);
        root.OnMouseUp(UiMouseButton.Left, px + 100, py);
        AssertInside(root, deck);
        Assert.Equal(1.4f, deck.Geometry.Scale, 3);
        Assert.Equal(1f, deck.Geometry.Stretch, 3);
    }

    [Fact]
    public void RightEdgeStretchStopsAtTheScreenEdge()
    {
        var (root, deck) = Mount(700, 900);
        root.OnMouseDown(UiMouseButton.Left, X(418), Y(75));
        root.OnMouseMove(X(418) + 400, Y(75));
        root.OnMouseUp(UiMouseButton.Left, X(418) + 400, Y(75));
        AssertInside(root, deck);
        Assert.Equal(600f, deck.Width, 1);
        Assert.Equal(1f, deck.Geometry.Scale, 3);
    }

    [Theory]
    [InlineData(1f, 75f)]     // left edge
    [InlineData(1f, 1f)]      // top-left corner
    [InlineData(1f, 149f)]    // bottom-left corner
    [InlineData(250f, 1f)]    // top edge
    [InlineData(250f, 149f)]  // bottom edge
    [InlineData(250f, 4f)]    // top edge, over the frame
    [InlineData(3f, 46f)]     // left edge, over stud 0
    public void OtherEdgesDoNotResize(float localX, float localY)
    {
        var (root, deck) = Mount();
        root.OnMouseMove(X(localX), Y(localY));
        Assert.Equal(ResizeEdges.None, root.HoverResizeEdges);

        root.OnMouseDown(UiMouseButton.Left, X(localX), Y(localY));
        Assert.Equal(ResizeEdges.None, root.ActiveResizeEdges);
        root.OnMouseMove(X(localX) - 40, Y(localY) - 40);
        root.OnMouseUp(UiMouseButton.Left, X(localX) - 40, Y(localY) - 40);
        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
    }

    [Theory]
    [InlineData(250f, 4f)]
    [InlineData(3f, 46f)]
    public void OtherEdgePointsOverTheDeckHitIt(float localX, float localY)
    {
        var (_, deck) = Mount();
        Assert.True(deck.HitTestLocal(localX, localY));
    }

    [Theory]
    [InlineData(418f, 1f, ResizeEdges.Right | ResizeEdges.Top)]
    [InlineData(418f, 149f, ResizeEdges.Right | ResizeEdges.Bottom)]
    [InlineData(418f, 75f, ResizeEdges.Right)]
    public void RightGripsHitTestOutsideTheFrame(float localX, float localY, ResizeEdges expected)
    {
        var (root, _) = Mount();
        root.OnMouseMove(X(localX), Y(localY));
        Assert.Equal(expected, root.HoverResizeEdges);
    }

    [Theory]
    [InlineData(630f, 187.5f, 1.25f, 1.2f)]
    [InlineData(420f, 150f, 1f, 1f)]
    [InlineData(5000f, 140f, 0.9f, 2f)]    // 0.93 would round up past 140
    public void AnySizeIsCorrectedToItsGeometryOnRefresh(float width, float height, float scale, float stretch)
    {
        var (_, deck) = Mount();
        deck.Width = width;
        deck.Height = height;
        deck.Refresh();
        Assert.Equal(scale, deck.Geometry.Scale, 3);
        Assert.Equal(stretch, deck.Geometry.Stretch, 3);
        Assert.Equal((deck.Geometry.Width, deck.Geometry.Height), (deck.Width, deck.Height));
    }

    [Fact]
    public void GripHighlightDrawsOnlyWhileAGripIsInUse()
    {
        var (root, deck) = Mount();
        static int Count(UiCommandDeck d)
        {
            int n = 0;
            foreach (var s in MarkupDrawProbe.Draw(d, d.Width, d.Height).DebugSpriteSegmentVerts) n += s.Verts.Count;
            return n;
        }

        root.OnMouseMove(X(250), Y(75));
        int idle = Count(deck);
        root.OnMouseMove(X(418), Y(75));
        Assert.True(Count(deck) > idle);
    }
}
