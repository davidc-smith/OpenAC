using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The Gear Deck owns its scale: a side-edge drag scales the whole deck in the
/// decks' steps, the bottom edge changes the height only, and the Command Deck's scale
/// never reaches it. Every drag goes through the real <see cref="UiRoot"/> pointer path.</summary>
public sealed class GearDeckScaleTests
{
    private const uint Player = 0x50000001, Bag = 0xB1;

    private sealed class NoActions : IGearItemActions
    {
        public bool Ready() => true;
        public void Report(string text) { }
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) => true;
        public bool Merge(uint from, uint to) => true;
        public bool WieldFromPaperdoll(uint item, EquipMask mask) => true;
        public bool WieldSecondary(uint item) => true;
        public bool Activate(uint item) => true;
        public bool OpenPack(uint pack) => true;
    }

    private sealed class NoDeckActions : ICommandDeckActions
    {
        public void ToggleCombat() { }
        public void PressAttack(AttackHeight height) { }
        public void ReleaseAttack() { }
        public void SetDesiredPower(float power) { }
        public void SetOption(CharacterOptionId option, bool value) { }
        public void Recall(RuntimePortalCommand command) { }
        public void CastOutlined() { }
    }

    internal static UiGearDeck NewDeck()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, ItemsCapacity = 102, ContainersCapacity = 7 });
        t.AddOrUpdate(new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24 });
        t.MoveItem(Bag, Player, 0);
        var session = new GearDeckSession(null, () => "Davian", () => true, () => 0.0);
        return new UiGearDeck(
            new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
                o => o.Name, session, new NoActions(), _ => false, () => { }),
            fonts: null, icons: null, clock: () => 0.0);
    }

    /// <summary>A deck mounted the way <c>RetailUiRuntime.MountGearDeck</c> mounts it, shown.</summary>
    internal static UiGearDeck MountDeck(UiRoot root, float left = 400f, float top = 50f)
    {
        UiGearDeck deck = NewDeck();
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0), UiGearDeck.MountOptions(left, top, deck.Scale) with { Visible = true });
        root.Tick(0.016, 0);
        return deck;
    }

    /// <summary>Presses at a point in the deck, moves by (<paramref name="dx"/>, <paramref name="dy"/>)
    /// and, when <paramref name="release"/>, lets go.</summary>
    internal static void Drag(UiRoot root, UiGearDeck deck, float localX, float localY, int dx, int dy, bool release = true)
    {
        int x = (int)(deck.Left + localX), y = (int)(deck.Top + localY);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + dx, y + dy);
        if (release) root.OnMouseUp(UiMouseButton.Left, x + dx, y + dy);
        root.Tick(0.016, 0);
    }

    [Fact]
    public void ANewDeck_IsAtScale1_AndResizesFromEveryEdge()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck = MountDeck(root);
        Assert.Equal(1f, deck.Scale);
        Assert.Equal(300f, deck.Width);
        Assert.True(deck.ResizeX);
        Assert.Equal(ResizeEdges.Left | ResizeEdges.Right | ResizeEdges.Top | ResizeEdges.Bottom, deck.ResizableEdges);
        Assert.Equal(GearDeckGeometry.WidthAt(DeckGeometry.MinScale), deck.MinWidth);
        Assert.Equal(GearDeckGeometry.WidthAt(DeckGeometry.MaxScale), deck.MaxWidth);
    }

    [Fact]
    public void DraggingTheRightEdge_ScalesTheDeck_AndSnapsTheWidthLiveToAStep()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck = MountDeck(root);
        float left = deck.Left, top = deck.Top;

        Drag(root, deck, deck.Width - 1f, 300f, 80, 0, release: false);   // 380 / 300 = 1.267: the nearest step is 1.25
        Assert.Equal(375f, deck.Width);
        Assert.Equal(1.25f, deck.Scale);
        Assert.Equal(1.25f, deck.Geometry.Scale);
        Assert.Equal(375f, deck.Geometry.Frame.W);

        root.OnMouseUp(UiMouseButton.Left, (int)(left + 380f), (int)(top + 300f));
        root.Tick(0.016, 0);
        Assert.Equal(1.25f, deck.Scale);
        Assert.Equal(375f, deck.Width);
        Assert.Equal(left, deck.Left);
        Assert.True(deck.Height >= GearDeckGeometry.MinHeightAt(1.25f));
    }

    [Fact]
    public void DraggingTheLeftEdge_ScalesTheDeck_AndKeepsTheRightEdge()
    {
        var root = new UiRoot { Width = 1600, Height = 1200 };
        UiGearDeck deck = MountDeck(root);
        float right = deck.Left + deck.Width;

        Drag(root, deck, 1f, 300f, -150, 0);
        Assert.Equal(DeckGeometry.MaxScale, deck.Scale);
        Assert.Equal(450f, deck.Width);
        Assert.Equal(right, deck.Left + deck.Width);

        Drag(root, deck, 1f, 300f, 300, 0);   // past the smallest size
        Assert.Equal(DeckGeometry.MinScale, deck.Scale);
        Assert.Equal(225f, deck.Width);
        Assert.Equal(right, deck.Left + deck.Width);
    }

    [Fact]
    public void DraggingTheBottomEdge_ChangesTheHeightOnly()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck = MountDeck(root);
        float h = deck.Height;

        Drag(root, deck, 150f, deck.Height - 1f, 0, 60);
        Assert.Equal(1f, deck.Scale);
        Assert.Equal(300f, deck.Width);
        Assert.Equal(h + 60f, deck.Height);
    }

    [Fact]
    public void ACornerDrag_TheSideSetsTheScale_TheBottomSetsTheHeight()
    {
        var root = new UiRoot { Width = 1600, Height = 1200 };
        UiGearDeck deck = MountDeck(root);
        float h = deck.Height;

        Drag(root, deck, deck.Width - 1f, deck.Height - 1f, 75, 200);
        Assert.Equal(1.25f, deck.Scale);
        Assert.Equal(375f, deck.Width);
        Assert.Equal(h + 200f, deck.Height);
        Assert.True(deck.Height > GearDeckGeometry.MinHeightFor(deck.Geometry.Input, 1.25f), "the height is the drag's, not the minimum");
    }

    [Fact]
    public void AScaleUpThatWouldLeaveTheScreen_StopsAtTheLargestStepThatFits()
    {
        var root = new UiRoot { Width = 1600, Height = 1200 };
        UiGearDeck deck = MountDeck(root, left: 1600f - 300f - 100f);   // 100 points of room to the right

        Drag(root, deck, deck.Width - 1f, 300f, 150, 0);
        Assert.Equal(1.3f, deck.Scale, 3);   // 400 / 300 = 1.333: 1.35 would cross the edge
        Assert.True(deck.Left + deck.Width <= root.Width + 0.01f, $"right {deck.Left + deck.Width}");
    }

    [Fact]
    public void AScaleWhoseMinimumHeightWouldLeaveTheScreen_StopsAtTheLargestStepThatFits()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck = MountDeck(root);   // top 50: 850 points of room below

        Drag(root, deck, deck.Width - 1f, 300f, 150, 0);
        float min = GearDeckGeometry.MinHeightFor(deck.Geometry.Input, deck.Scale);
        Assert.True(deck.Scale < 1.5f, "1.5's minimum height is taller than the room");
        Assert.True(min <= 850f, $"minimum {min} at {deck.Scale}");
        Assert.True(GearDeckGeometry.MinHeightFor(deck.Geometry.Input, deck.Scale + DeckGeometry.ScaleStep) > 850f,
            "the largest step that fits");
        Assert.True(deck.Top + deck.Height <= root.Height + 0.01f);
    }

    [Fact]
    public void ScalingUp_GrowsTheHeightToTheNewMinimum()
    {
        var root = new UiRoot { Width = 1600, Height = 1200 };
        UiGearDeck deck = MountDeck(root);
        Drag(root, deck, 150f, deck.Height - 1f, 0, -1000);   // as short as it goes at 1x
        float shortest = deck.Height;

        Drag(root, deck, deck.Width - 1f, 100f, 150, 0);
        Assert.Equal(1.5f, deck.Scale);
        Assert.True(deck.Height > shortest);
        Assert.True(deck.Height >= deck.MinHeight);
        Assert.Equal(deck.MinHeight, GearDeckGeometry.MinHeightFor(deck.Geometry.Input, 1.5f));
    }

    [Fact]
    public void TheCommandDecksScale_DoesNotReachTheGearDeck_NorTheOtherWay()
    {
        var root = new UiRoot { Width = 1600, Height = 1200 };
        var input = default(CommandDeckInput) with { Mode = CombatMode.Melee, SessionKey = 1 };
        var command = new UiCommandDeck(() => input, new CommandDeckModel(), new NoDeckActions(), icons: null, clock: () => 0.0);
        RetailWindowFrame.Mount(root, command, _ => (0u, 0, 0), UiCommandDeck.MountOptions(100f, 600f) with { Visible = true });
        command.Refresh();
        UiGearDeck gear = MountDeck(root, left: 900f);

        // The Command Deck to 1.25 by its bottom-right corner.
        int x = (int)(command.Left + 418f), y = (int)(command.Top + 148f);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseMove(x + 105, y + 38);
        root.OnMouseUp(UiMouseButton.Left, x + 105, y + 38);
        root.Tick(0.016, 0);
        command.Refresh();
        Assert.Equal(1.25f, command.Geometry.Scale, 3);
        Assert.Equal(1f, gear.Scale);
        Assert.Equal(300f, gear.Width);

        // The Gear Deck to 1.5 by its right edge; the Command Deck stays at 1.25.
        Drag(root, gear, gear.Width - 1f, 300f, 150, 0);
        command.Refresh();
        Assert.Equal(1.5f, gear.Scale);
        Assert.Equal(1.25f, command.Geometry.Scale, 3);
    }

    [Fact]
    public void TheSideGrip_DrawsWhileTheLeftEdgeIsDragged()
    {
        var root = new UiRoot { Width = 1600, Height = 900 };
        UiGearDeck deck = MountDeck(root);
        Drag(root, deck, 1f, 300f, -30, 0, release: false);
        Assert.Equal(ResizeEdges.Left, root.ResizeEdgesFor(deck));
        var renderer = MarkupDrawProbe.Draw(deck, root.Width, root.Height);   // drawn where it sits on screen
        bool grip = false;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u) continue;
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
                if (MathF.Abs(s.Verts[i + 4] - DeckChrome.GripInk.X) < 0.001f && MathF.Abs(s.Verts[i + 5] - DeckChrome.GripInk.Y) < 0.001f
                    && MathF.Abs(s.Verts[i + 6] - DeckChrome.GripInk.Z) < 0.001f && s.Verts[i] >= deck.Left && s.Verts[i] < deck.Left + 4f * deck.Scale)
                    grip = true;
        }
        Assert.True(grip, "a grip stroke on the left edge");
    }
}
