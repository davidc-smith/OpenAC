using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckDrawTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Helm = 0xC1, Sword = 0xC2;

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

    private sealed class FakeBackend : IPluginFontBackend
    {
        private uint _next = 100;
        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName) => _next++;
        public bool ReleaseCoverage(uint texture) => true;
    }

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    private static void Wear(ClientObjectTable t, ClientObject o, EquipMask mask)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, Player, -1, mask));
    }

    /// <summary>A player with one bag, 30 loose items (some stacked) and two worn items.</summary>
    private static ClientObjectTable World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        for (int i = 0; i < 30; i++)
        {
            Place(t, new ClientObject
            {
                ObjectId = 0xA00u + (uint)i,
                Name = $"A rather long item name number {i}",
                StackSize = i % 3 == 0 ? 1 : i * 350,
            }, Player, i);
        }
        Wear(t, new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", ValidLocations = EquipMask.HeadWear }, EquipMask.HeadWear);
        Wear(t, new ClientObject { ObjectId = Sword, WielderId = Player, Name = "Sword", ValidLocations = EquipMask.MeleeWeapon }, EquipMask.MeleeWeapon);
        return t;
    }

    private static UiGearDeck Deck(ClientObjectTable table, uint player = Player, DeckFonts? fonts = null,
        EmbeddedSvgSpriteCache? icons = null, Func<GearDeckSettings, GearDeckSettings>? settings = null, uint icon = 0u,
        Func<uint>? playerGuid = null, Func<int?>? strength = null, Func<ItemType, uint, uint, uint, uint, uint>? resolveIcon = null,
        float? scale = null, Func<double>? clock = null, ItemLocks? locks = null)
    {
        var session = new GearDeckSession(null, () => "", () => false);
        if (settings is not null) session.Update(settings);
        var b = new GearDeckBindings(
            Objects: () => table,
            PlayerGuid: playerGuid ?? (() => player),
            Strength: strength ?? (() => 100),
            ResolveIcon: resolveIcon ?? ((_, _, _, _, _) => icon),
            ResolveDragIcon: (_, _, _, _, _) => 0u,
            DisplayName: o => o.Name,
            Session: session,
            Actions: new NoActions(),
            IsPending: id => id == 0xA01u,
            Close: () => { },
            Locks: locks);
        var deck = new UiGearDeck(b, fonts, icons, clock);
        if (scale is { } s) deck.SetScale(s);
        deck.Refresh();
        return deck;
    }

    private static TextRenderer DrawInside(UiGearDeck deck)
    {
        var renderer = MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);
        int vertices = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                vertices++;
                Assert.InRange(s.Verts[i], -0.01f, deck.Width + 0.01f);
                Assert.InRange(s.Verts[i + 1], -0.01f, deck.Height + 0.01f);
            }
        Assert.True(vertices > 0);
        return renderer;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AtScale1_5_TheDeckIsWiderAndDrawsInsideItsFrame(bool list)
    {
        UiGearDeck deck = Deck(World(), fonts: new DeckFonts((_, _) => null!), scale: 1.5f,
            settings: s => s with { View = list ? GearPackView.List : GearPackView.Grid });
        Assert.Equal(1.5f, deck.Scale);
        Assert.Equal(450f, deck.Width);
        Assert.True(deck.Height >= GearDeckGeometry.MinHeightAt(1.5f), "the height grew to the minimum at the scale");
        Assert.Equal(1.5f, deck.Geometry.Scale);
        deck.SetQuery(GearQuery.Empty.With(GearChip.NameContains("item")));
        deck.Refresh();
        TextRenderer r = DrawInside(deck);
        Assert.True(HasColour(r, list ? UiGearDeck.RowRule : UiGearDeck.CellFill));
    }

    [Fact]
    public void TheTooltip_DrawsInTheDecksTextFaceAtItsScale()
    {
        var baked = new List<float>();
        double now = 0;
        UiGearDeck deck = Deck(World(), fonts: new DeckFonts((px, _) => { baked.Add(px); return null!; }),
            scale: 1.5f, clock: () => now);
        DeckRect cell = deck.Geometry.GridCell(0, 0)!.Value;
        float x = cell.X + cell.W / 2f, y = cell.Y + cell.H / 2f;
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseMove, Data1: (int)x, Data2: (int)y));
        now += UiCommandDeck.TooltipDwell;
        Assert.NotNull(deck.VisibleTooltip);

        MarkupDrawProbe.DrawOverlays(deck, deck.Width, deck.Height);
        // The tooltip body is the text face (14 px at 1x); at 1.5x it is 21 px, and no 1x face is baked.
        Assert.Contains(DeckFonts.PixelHeight(DeckFonts.TextPixelHeight, 1.5f), baked);
        Assert.DoesNotContain(DeckFonts.TextPixelHeight, baked);
    }

    [Fact]
    public void AScaledDeck_MountsAtItsWidth_AsTheRuntimeMountsIt()
    {
        var root = new UiRoot { Width = 2560, Height = 1330 };
        UiGearDeck deck = Deck(World(), scale: 1.5f);
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0),
            UiGearDeck.MountOptions(RetailUiRuntime.GearDeckDefaultLeft(root.Width, deck.Width), RetailUiRuntime.GearDeckDefaultTop, deck.Scale));
        Assert.Equal(450f, deck.Width);
        Assert.Equal(1.5f, deck.Scale);
        Assert.Equal(GearDeckGeometry.WidthAt(DeckGeometry.MinScale), deck.MinWidth);
        Assert.Equal(GearDeckGeometry.WidthAt(DeckGeometry.MaxScale), deck.MaxWidth);
        Assert.True(deck.MinHeight >= GearDeckGeometry.MinHeightAt(1.5f), "the mount's minimum is at the scale too");
        Assert.Equal(root.Width - 450f - 10f, deck.Left);
    }

    [Fact]
    public void MountedAt1x_ThenScaledUp_KeepsItsRightEdge_SoTheEdgeMarginStays()
    {
        var root = new UiRoot { Width = 1600, Height = 1200 };
        UiGearDeck deck = Deck(World());
        RetailWindowFrame.Mount(root, deck, _ => (0u, 0, 0),
            UiGearDeck.MountOptions(RetailUiRuntime.GearDeckDefaultLeft(root.Width, deck.Width), RetailUiRuntime.GearDeckDefaultTop, deck.Scale));
        Assert.Equal(root.Width - 300f - 10f, deck.Left);

        deck.SetScale(1.5f);
        deck.Refresh();
        Assert.Equal(450f, deck.Width);
        Assert.Equal(root.Width - 450f - 10f, deck.Left);
    }

    [Fact]
    public void WithoutFonts_ChipPillsStillScale()
    {
        UiGearDeck one = Deck(World());
        UiGearDeck big = Deck(World(), scale: 1.5f);
        GearQuery q = GearQuery.Empty.With(GearChip.WornOnly);
        one.SetQuery(q);
        big.SetQuery(q);
        one.Refresh();
        big.Refresh();
        Assert.Equal(one.Geometry.Chips[0].W * 1.5f, big.Geometry.Chips[0].W, 0.5f);
    }

    [Fact]
    public void TheDeck_FollowsAChangingScale_SnappedAndClamped()
    {
        var root = new UiRoot { Width = 1600, Height = 1200 };
        UiGearDeck deck = Deck(World());
        root.AddChild(deck);
        deck.Left = 1300;
        deck.Refresh();
        Assert.Equal(300f, deck.Width);
        float minAtOne = deck.MinHeight;

        deck.SetScale(1.52f);   // snaps to 1.5
        deck.Refresh();
        Assert.Equal(1.5f, deck.Scale);
        Assert.Equal(450f, deck.Width);
        Assert.Equal(450f, deck.Geometry.Frame.W);
        Assert.Equal(1.5f, deck.Geometry.Scale);
        Assert.True(deck.MinHeight > minAtOne);
        Assert.True(deck.Height >= deck.MinHeight, "the height grows to the new minimum");
        Assert.True(deck.Left + deck.Width <= root.Width, "kept inside its parent");
        float w = deck.Width, h = deck.Height;
        deck.ConstrainResize(ResizeEdges.Bottom, ref w, ref h);
        Assert.Equal(450f, w);

        deck.SetScale(9f);   // past the decks' range
        deck.Refresh();
        Assert.Equal(DeckGeometry.MaxScale, deck.Scale);
        deck.SetScale(1f);
        deck.Refresh();
        Assert.Equal(300f, deck.Width);
        Assert.Equal(1f, deck.Geometry.Scale);
    }

    /// <summary>Tighter than a channel step (1/255): the deck's dark inks differ by one step.</summary>
    private const float Tol = 0.001f;

    private static bool HasColour(TextRenderer renderer, Vector4 c)
    {
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u) continue;
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
                if (MathF.Abs(s.Verts[i + 4] - c.X) < Tol && MathF.Abs(s.Verts[i + 5] - c.Y) < Tol
                    && MathF.Abs(s.Verts[i + 6] - c.Z) < Tol && MathF.Abs(s.Verts[i + 7] - c.W) < Tol)
                    return true;
        }
        return false;
    }

    [Fact]
    public void ExpandedGrid_DrawsCellsGaugeAndSelectedPackInsideTheFrame()
    {
        UiGearDeck deck = Deck(World());
        Assert.Equal(GearDeckGeometry.Width, deck.Width);
        Assert.Equal(GearDeckGeometry.DefaultHeight, deck.Height);
        Assert.Equal(30, deck.Items.Count);
        Assert.Equal(Player, deck.SelectedPackId);

        TextRenderer r = DrawInside(deck);
        Assert.True(HasColour(r, UiGearDeck.CellFill));
        Assert.True(HasColour(r, UiGearDeck.SlotFill));
        Assert.True(HasColour(r, DeckChrome.Track));
        Assert.True(HasColour(r, DeckChrome.Handle));
        Assert.True(HasColour(r, UiGearDeck.CellFill with { W = UiGearDeck.DimAlpha }), "the pending item is dimmed");
    }

    [Fact]
    public void FoldedList_DrawsWornRowAndListRowsInsideTheFrame()
    {
        UiGearDeck deck = Deck(World(), settings: s => s with { Folded = true, View = GearPackView.List });
        Assert.True(deck.Geometry.Input.Folded);
        Assert.True(deck.Geometry.Input.List);
        Assert.Equal(2, deck.Geometry.Input.WornCount);

        TextRenderer r = DrawInside(deck);
        Assert.True(HasColour(r, UiGearDeck.SlotFill));
        Assert.True(HasColour(r, UiGearDeck.RowRule));
        Assert.False(HasColour(r, UiGearDeck.CellFill));
    }

    [Fact]
    public void MinimumHeight_StaysInsideTheFrame()
    {
        UiGearDeck deck = Deck(World());
        deck.Height = GearDeckGeometry.MinHeight;
        deck.Refresh();
        float min = GearDeckGeometry.MinHeightFor(deck.Geometry.Input);
        Assert.Equal(min, deck.Height);   // below the minimum grows to it
        Assert.Equal(min, deck.Geometry.Frame.H);
        Assert.Equal(1 + 7, deck.Geometry.VisibleSidePacks);
        DrawInside(deck);
    }

    [Fact]
    public void ASelectedSidePackShowsItsContents()
    {
        var t = World();
        Place(t, new ClientObject { ObjectId = 0xD1, Name = "Gem" }, Bag, 0);
        UiGearDeck deck = Deck(t, settings: s => s with { SelectedPack = Bag });
        Assert.Equal(Bag, deck.SelectedPackId);
        Assert.Equal([0xD1u], deck.Items.Select(o => o.ObjectId).ToArray());
        Assert.Equal("Pack 2 · 1 / 24", deck.FooterText);
        DrawInside(deck);
    }

    [Fact]
    public void AMissingSelectedPackFallsBackToTheMainPack()
    {
        UiGearDeck deck = Deck(World(), settings: s => s with { SelectedPack = 0xEEEE });
        Assert.Equal(Player, deck.SelectedPackId);
        Assert.Equal("Main · 30 / 102", deck.FooterText);
    }

    [Fact]
    public void AnEmptyTable_DrawsOnlyTheFrameAndTitle()
    {
        UiGearDeck deck = Deck(new ClientObjectTable(), player: 0);
        TextRenderer r = DrawInside(deck);
        Assert.False(HasColour(r, UiGearDeck.CellFill));
        Assert.False(HasColour(r, UiGearDeck.SlotFill));
        Assert.False(HasColour(r, DeckChrome.Track));
        Assert.False(HasColour(r, DeckChrome.StudOff));
    }

    [Fact]
    public void WithFontsAndIcons_StaysInsideTheFrame()
    {
        var baked = new List<(float, BundledUiFontWeight)>();
        var fonts = new DeckFonts((px, w) => { baked.Add((px, w)); return null!; });
        using var icons = new EmbeddedSvgSpriteCache(new FakeBackend());
        DrawInside(Deck(World(), fonts: fonts, icons: icons, icon: 7u));
        DrawInside(Deck(World(), fonts: fonts, icons: icons, settings: s => s with { Folded = true, View = GearPackView.List }, icon: 7u));
        Assert.Contains((DeckFonts.SmallPixelHeight, BundledUiFontWeight.Regular), baked);
    }

    [Fact]
    public void LockedAetheriaSlotsAreDimmed()
    {
        var t = World();
        UiGearDeck deck = Deck(t);
        Assert.True(HasColour(DrawInside(deck), UiGearDeck.SlotFill with { W = UiGearDeck.DimAlpha }));

        t.Get(Player)!.Properties.Ints[AetheriaUnlocks.PropertyId] = (int)AetheriaUnlockState.All;
        deck = Deck(t);
        Assert.False(HasColour(DrawInside(deck), UiGearDeck.SlotFill with { W = UiGearDeck.DimAlpha }));
    }

    [Fact]
    public void TheModelIsRebuiltOnlyWhenTheTableChanges()
    {
        var t = World();
        UiGearDeck deck = Deck(t);
        var before = deck.Model;
        deck.Refresh();
        Assert.Same(before, deck.Model);

        Place(t, new ClientObject { ObjectId = 0xA99, Name = "New" }, Player, 30);
        deck.Refresh();
        Assert.NotSame(before, deck.Model);
        Assert.Equal(31, deck.Items.Count);
    }

    [Fact]
    public void TheModelIsRebuiltWhenThePlayerOrStrengthChanges()
    {
        var t = World();
        uint player = Player;
        int? strength = 100;
        UiGearDeck deck = Deck(t, playerGuid: () => player, strength: () => strength);
        var before = deck.Model;

        strength = 200;
        deck.Refresh();
        Assert.NotSame(before, deck.Model);
        Assert.NotEqual(before.CapacityUnits, deck.Model.CapacityUnits);

        before = deck.Model;
        deck.Refresh();
        Assert.Same(before, deck.Model);

        player = 0;
        deck.Refresh();
        Assert.Same(GearInventoryModel.Empty, deck.Model);
    }

    [Fact]
    public void SlotOccupantsFollowTheTable()
    {
        const uint Gloves = 0xC3, GloveIcon = 0x06001234;
        int hands = GearSlotCatalog.Slots.ToList().FindIndex(s => s.Mask == EquipMask.HandWear);
        var t = World();
        UiGearDeck deck = Deck(t, resolveIcon: (_, iconId, _, _, _) => iconId);
        Assert.Equal(0u, deck.SlotOccupant(hands));
        Assert.DoesNotContain(MarkupDrawProbe.Draw(deck, GearDeckGeometry.Width, deck.Height).DebugSpriteSegmentVerts,
            s => s.Texture == GloveIcon);

        Wear(t, new ClientObject { ObjectId = Gloves, WielderId = Player, Name = "Gloves", IconId = GloveIcon, ValidLocations = EquipMask.HandWear }, EquipMask.HandWear);
        deck.Refresh();
        Assert.Equal(Gloves, deck.SlotOccupant(hands));
        Assert.Contains(MarkupDrawProbe.Draw(deck, GearDeckGeometry.Width, deck.Height).DebugSpriteSegmentVerts,
            s => s.Texture == GloveIcon);
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(25, "25")]
    [InlineData(999, "999")]
    [InlineData(9000, "9k")]
    [InlineData(12500, "12k")]
    public void StackCountsShortenThousands(int stack, string expected) =>
        Assert.Equal(expected, UiGearDeck.StackText(stack));

    [Fact]
    public void MountOptions_ResizeFromEveryEdge_InTheScaleRange_AndAreHidden()
    {
        var o = UiGearDeck.MountOptions(10f, 120f);
        Assert.Equal(GearDeckGeometry.WidthAt(DeckGeometry.MinScale), o.MinWidth);
        Assert.Equal(GearDeckGeometry.WidthAt(DeckGeometry.MaxScale), o.MaxWidth);
        Assert.Equal(WindowNames.GearDeck, o.WindowName);
        Assert.Equal(RetailWindowChrome.Imported, o.Chrome);
        Assert.True(o.Resizable);
        Assert.True(o.ResizeX);
        Assert.True(o.ResizeY);
        Assert.Equal(ResizeEdges.Left | ResizeEdges.Right | ResizeEdges.Top | ResizeEdges.Bottom, o.ResizableEdges);
        Assert.Equal(GearDeckGeometry.MinHeight, o.MinHeight);
        Assert.True(o.ConstrainResizeToParent);
        Assert.False(o.Visible);
    }

    [Fact]
    public void ConstrainResize_AHeightEdgeKeepsTheWidthAndTheMinimumHeight()
    {
        UiGearDeck deck = Deck(World());
        float w = 420f, h = 100f;
        deck.ConstrainResize(ResizeEdges.Bottom, ref w, ref h);
        Assert.Equal(GearDeckGeometry.Width, w);
        float min = GearDeckGeometry.MinHeightFor(deck.Geometry.Input);
        Assert.True(min > GearDeckGeometry.MinHeight);
        Assert.Equal(min, deck.MinHeight);
        Assert.Equal(min, h);
        h = min - 1f;
        deck.ConstrainResize(ResizeEdges.Top, ref w, ref h);
        Assert.Equal(min, h);
    }

    [Fact]
    public void ALockedItemInTheGrid_DrawsAPadlockInItsBottomLeft()
    {
        static int Ink(TextRenderer r, DeckRect cell)
        {
            int n = 0;
            foreach (var s in r.DebugSpriteSegmentVerts)
            {
                if (s.Texture != 0u) continue;
                for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
                    if (s.Verts[i] >= cell.X && s.Verts[i] <= cell.X + cell.W / 2f
                        && s.Verts[i + 1] >= cell.Y + cell.H / 2f && s.Verts[i + 1] <= cell.Y + cell.H
                        && MathF.Abs(s.Verts[i + 4] - DeckChrome.TextInk.X) < Tol && MathF.Abs(s.Verts[i + 5] - DeckChrome.TextInk.Y) < Tol
                        && MathF.Abs(s.Verts[i + 6] - DeckChrome.TextInk.Z) < Tol)
                        n++;
            }
            return n;
        }
        var locks = new ItemLocks();
        UiGearDeck deck = Deck(World(), locks: locks);
        DeckRect cell = deck.Geometry.GridCell(2, 0)!.Value;
        Assert.Equal(0, Ink(DrawInside(deck), cell));
        locks.Set(deck.Shown[2].Id, true);
        deck.Refresh();
        Assert.True(Ink(DrawInside(deck), cell) > 0);
    }
}
