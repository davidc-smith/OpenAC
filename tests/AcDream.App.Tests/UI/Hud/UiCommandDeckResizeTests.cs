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
        public void Recast() { }
    }

    private const float DeckLeft = 100f, DeckTop = 200f;

    /// <summary>A deck mounted the way <c>RetailUiRuntime.MountCommandDeck</c> mounts it.</summary>
    private static (UiRoot Root, UiCommandDeck Deck) Mount()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        var input = default(CommandDeckInput) with { Mode = CombatMode.Melee, SessionKey = 1 };
        var deck = new UiCommandDeck(() => input, new CommandDeckModel(), new NoActions(), icons: null, clock: () => 0.0);
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0), UiCommandDeck.MountOptions(DeckLeft, DeckTop) with { Visible = true });
        deck.Refresh();
        return (root, deck);
    }

    private static int X(float local) => (int)(DeckLeft + local);
    private static int Y(float local) => (int)(DeckTop + local);

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

    [Theory]
    [InlineData(1f, 75f)]     // left edge
    [InlineData(1f, 1f)]      // top-left corner
    [InlineData(1f, 149f)]    // bottom-left corner
    [InlineData(250f, 1f)]    // top edge
    [InlineData(250f, 149f)]  // bottom edge
    public void OtherEdgesDoNotResize(float localX, float localY)
    {
        var (root, deck) = Mount();
        root.OnMouseMove(X(localX), Y(localY));
        Assert.Equal(ResizeEdges.None, root.HoverResizeEdges);

        root.OnMouseDown(UiMouseButton.Left, X(localX), Y(localY));
        root.OnMouseMove(X(localX) - 40, Y(localY) - 40);
        root.OnMouseUp(UiMouseButton.Left, X(localX) - 40, Y(localY) - 40);
        Assert.Equal((420f, 150f), (deck.Width, deck.Height));
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
    [InlineData(5000f, 140f, 0.95f, 2f)]
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
