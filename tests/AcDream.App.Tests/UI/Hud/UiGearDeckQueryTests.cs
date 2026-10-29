using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Silk.NET.Input;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The query bar's input: the field, its chips, the suggestions and the hidden-chip menu.</summary>
public sealed class UiGearDeckQueryTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, First = 0xA00;
    private const uint Steel = 64;

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

    private static void Place(ClientObjectTable t, ClientObject o, uint container, int slot)
    {
        t.AddOrUpdate(o);
        Assert.True(t.MoveItem(o.ObjectId, container, slot));
    }

    /// <summary>The main pack holds a side pack, two steel salvage bags and a few misc items.</summary>
    private static ClientObjectTable World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        for (int i = 0; i < 2; i++)
            Place(t, new ClientObject
            {
                ObjectId = First + (uint)i, Name = $"Salvaged Steel {i}", Type = ItemType.TinkeringMaterial,
                MaterialType = Steel, Workmanship = 5f + i,
            }, Player, i);
        for (int i = 2; i < 6; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"Item {i}", Type = ItemType.Misc }, Player, i);
        return t;
    }

    private static UiGearDeck Make(DeckFonts? fonts = null, Func<double>? clock = null, Func<uint>? player = null,
        Func<float>? scale = null, ClientObjectTable? world = null)
    {
        ClientObjectTable table = world ?? World();
        var session = new GearDeckSession(null, () => "", () => false);
        var b = new GearDeckBindings(
            Objects: () => table,
            PlayerGuid: player ?? (() => Player),
            Strength: () => 100,
            ResolveIcon: (_, _, _, _, _) => 0u,
            ResolveDragIcon: (_, _, _, _, _) => 0u,
            DisplayName: o => o.Name,
            Session: session,
            Actions: new NoActions(),
            IsPending: _ => false,
            Close: () => { },
            Scale: scale);
        var deck = new UiGearDeck(b, fonts, null, clock ?? (() => 0.0));
        deck.Refresh();
        return deck;
    }

    /// <summary>A deck at the root's origin, so its screen and local points agree.</summary>
    private static (UiRoot Root, UiGearDeck Deck) Rooted(DeckFonts? fonts = null, Func<float>? scale = null)
    {
        var root = new UiRoot { Width = 1200, Height = 900 };
        UiGearDeck deck = Make(fonts, scale: scale);
        root.AddChild(deck);
        deck.Refresh();
        return (root, deck);
    }

    /// <summary>Focuses the field and types <paramref name="text"/> into it.</summary>
    private static void Type(UiRoot root, UiGearDeck deck, string text)
    {
        root.SetKeyboardFocus(deck.QueryInput);
        deck.QueryInput.SetText(text);
    }

    private static (int X, int Y) Centre(UiGearDeck deck, DeckRect r)
    {
        var o = deck.ScreenPosition;
        return ((int)(o.X + r.X + r.W / 2f), (int)(o.Y + r.Y + r.H / 2f));
    }

    /// <summary>A left click through the root's pointer path, as the client delivers it.</summary>
    private static void RootClick(UiRoot root, UiGearDeck deck, DeckRect r)
    {
        var (x, y) = Centre(deck, r);
        root.OnMouseMove(x, y);
        root.OnMouseDown(UiMouseButton.Left, x, y);
        root.OnMouseUp(UiMouseButton.Left, x, y);
    }

    /// <summary>Clicks row <paramref name="index"/> of the open menu (no separators above it) through the root.</summary>
    private static void ClickMenuRow(UiRoot root, UiGearDeck deck, int index)
    {
        var (x, y, _, _) = deck.Menu.PopupScreenRect;
        float s = deck.Menu.Scale;
        int px = (int)(x + 10f * s);
        int py = (int)(y + (UiMarkupContextMenu.Inset + (index + 0.5f) * UiMarkupContextMenu.ItemHeight) * s);
        root.OnMouseMove(px, py);
        root.OnMouseDown(UiMouseButton.Left, px, py);
        root.OnMouseUp(UiMouseButton.Left, px, py);
    }

    private static void Key(UiField field, Key key) =>
        Assert.True(field.OnEvent(new UiEvent(0, field, UiEventType.KeyDown, Data0: (int)key)));

    private static GearChip LongName(int i) => GearChip.NameContains($"a rather long name {i}");

    [Fact]
    public void Typing_SetsTheQueryTextLive_AndSuggestsTheMaterialFirst()
    {
        UiGearDeck deck = Make();
        deck.QueryInput.SetText("steel ");
        Assert.Equal("steel", deck.Query.Text);
        Assert.True(deck.ShowsResults);
        Assert.Equal(GearChip.OfMaterial(Steel), deck.Suggestions[0]);
        Assert.Equal(0, deck.SuggestIndex);
    }

    [Fact]
    public void Submitting_AddsTheFirstSuggestion_AndClearsTheText()
    {
        UiGearDeck deck = Make();
        deck.QueryInput.SetText("steel");
        deck.QueryInput.OnSubmit!("steel");
        Assert.Equal(new[] { GearChip.OfMaterial(Steel) }, deck.Query.Chips);
        Assert.Equal("", deck.Query.Text);
        Assert.Equal("", deck.QueryInput.Text);
        Assert.Empty(deck.Suggestions);
    }

    [Fact]
    public void Enter_TakesTheHighlightedSuggestion_AndKeepsFocus()
    {
        var (root, deck) = Rooted();
        Type(root, deck, "steel");
        Assert.True(deck.Suggestions.Count > 1);
        GearChip second = deck.Suggestions[1];
        Key(deck.QueryInput, Silk.NET.Input.Key.Down);
        Assert.Equal(1, deck.SuggestIndex);
        Key(deck.QueryInput, Silk.NET.Input.Key.Up);
        Key(deck.QueryInput, Silk.NET.Input.Key.Up);
        Assert.Equal(0, deck.SuggestIndex);
        Key(deck.QueryInput, Silk.NET.Input.Key.Down);

        root.OnKeyDown((int)Silk.NET.Input.Key.Enter);
        Assert.Equal(new[] { second }, deck.Query.Chips);
        Assert.Equal("", deck.QueryInput.Text);
        Assert.Same(deck.QueryInput, root.KeyboardFocus);
    }

    [Fact]
    public void Escape_ClearsTheTextAndSuggestions_AndReleasesFocus()
    {
        var (root, deck) = Rooted();
        Type(root, deck, "steel");
        deck.Refresh();
        Assert.True(deck.Geometry.Input.Suggestions > 0);

        root.OnKeyDown((int)Silk.NET.Input.Key.Escape);
        deck.Refresh();
        Assert.Equal("", deck.QueryInput.Text);
        Assert.Equal("", deck.Query.Text);
        Assert.Empty(deck.Suggestions);
        Assert.Null(root.KeyboardFocus);
        Assert.Equal(0, deck.Geometry.Input.Suggestions);
    }

    [Fact]
    public void BackspaceOnAnEmptyField_RemovesTheLastChip()
    {
        UiGearDeck deck = Make();
        deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly).With(GearChip.OfMaterial(Steel)));
        Key(deck.QueryInput, Silk.NET.Input.Key.Backspace);
        Assert.Equal(new[] { GearChip.WornOnly }, deck.Query.Chips);

        deck.QueryInput.SetText("ab");
        Key(deck.QueryInput, Silk.NET.Input.Key.Backspace);
        Assert.Equal("a", deck.QueryInput.Text);
        Assert.Single(deck.Query.Chips);
    }

    [Fact]
    public void Suggestions_ShowOnlyWhileTheFieldHasFocusAndText()
    {
        var (root, deck) = Rooted();
        deck.QueryInput.SetText("steel");
        deck.Refresh();
        Assert.Equal(0, deck.Geometry.Input.Suggestions);

        root.SetKeyboardFocus(deck.QueryInput);
        deck.Refresh();
        Assert.Equal(deck.Suggestions.Count, deck.Geometry.Input.Suggestions);

        deck.QueryInput.SetText("  ");
        Assert.Equal(0, deck.Geometry.Input.Suggestions);
    }

    [Fact]
    public void ClickingASuggestion_ThroughTheRoot_AddsIt()
    {
        var (root, deck) = Rooted();
        Type(root, deck, "steel");
        GearChip second = deck.Suggestions[1];

        RootClick(root, deck, deck.Geometry.Suggestion(1));
        Assert.Equal(new[] { second }, deck.Query.Chips);
        Assert.Equal("", deck.QueryInput.Text);
        Assert.Same(deck.QueryInput, root.KeyboardFocus);
    }

    [Fact]
    public void ClickingAChip_RemovesIt()
    {
        var (root, deck) = Rooted();
        deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly).With(GearChip.OfMaterial(Steel)));
        Assert.NotEmpty(deck.Geometry.Chips);

        RootClick(root, deck, deck.Geometry.Chips[0]);
        Assert.Equal(new[] { GearChip.OfMaterial(Steel) }, deck.Query.Chips);
    }

    [Fact]
    public void TheChipRects_FollowTheChips_WithAMorePillForLongOnes()
    {
        UiGearDeck deck = Make();
        Assert.Empty(deck.Geometry.Chips);
        deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        Assert.Single(deck.Geometry.Chips);
        Assert.Null(deck.Geometry.ChipMore);

        GearQuery q = GearQuery.Empty;
        for (int i = 0; i < 6; i++) q = q.With(LongName(i));
        deck.SetQuery(q);
        Assert.NotNull(deck.Geometry.ChipMore);
        Assert.Equal(6, deck.Geometry.Chips.Count + deck.Geometry.HiddenChips);
        Assert.True(deck.Geometry.HiddenChips > 0);
    }

    [Fact]
    public void TheMorePill_ListsTheHiddenChips_AndChoosingOneRemovesIt()
    {
        var (root, deck) = Rooted();
        GearQuery q = GearQuery.Empty.With(GearChip.WornOnly);
        for (int i = 0; i < 3; i++) q = q.With(LongName(i));
        deck.SetQuery(q);
        int shown = deck.Geometry.Chips.Count;
        Assert.NotNull(deck.Geometry.ChipMore);

        RootClick(root, deck, deck.Geometry.ChipMore!.Value);
        Assert.True(deck.Menu.IsOpen);
        Assert.Equal(deck.ChipLabels.Skip(shown).ToArray(), deck.Menu.ItemsSource());

        ClickMenuRow(root, deck, 0);
        Assert.False(deck.Menu.IsOpen);
        Assert.Equal(q.Without(shown).Chips, deck.Query.Chips);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void TheSortMenu_FollowsTheDeckScale_InSizeAndHitTesting(float scale)
    {
        var (root, deck) = Rooted(scale: () => scale);
        deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        RootClick(root, deck, deck.Geometry.SortStud);
        Assert.True(deck.Menu.IsOpen);
        Assert.Equal(scale, deck.Menu.Scale);
        var (_, _, w, h) = deck.Menu.PopupScreenRect;
        // Seven rows of 18 between insets of 3, and at least the 80-point minimum width, all at the scale.
        Assert.Equal((2f * UiMarkupContextMenu.Inset + 7f * UiMarkupContextMenu.ItemHeight) * scale, h, 3);
        Assert.True(w >= UiMarkupContextMenu.MinPopupWidth * scale);

        ClickMenuRow(root, deck, 4);   // Name, Type, Material, Workmanship, Value
        Assert.Equal(GearSortField.Value, deck.Query.Sort.Field);
    }

    [Fact]
    public void ChipsChangingWhileTheMenuIsOpen_RemoveTheListedChipByValue()
    {
        var (root, deck) = Rooted();
        GearQuery q = GearQuery.Empty.With(GearChip.WornOnly);
        for (int i = 0; i < 3; i++) q = q.With(LongName(i));
        deck.SetQuery(q);
        Assert.Single(deck.Geometry.Chips);   // Worn shows; the long names are behind "+3"

        RootClick(root, deck, deck.Geometry.ChipMore!.Value);
        Assert.True(deck.Menu.IsOpen);
        deck.SetQuery(deck.Query.Without(0));   // Worn goes: an index would now name LongName(1)
        deck.Menu.Selected!(0);
        Assert.Equal(new[] { LongName(1), LongName(2) }, deck.Query.Chips);
    }

    [Fact]
    public void AChipGoneWhileTheMenuIsOpen_IsNotReplacedByAnother()
    {
        var (root, deck) = Rooted();
        GearQuery q = GearQuery.Empty.With(GearChip.WornOnly);
        for (int i = 0; i < 3; i++) q = q.With(LongName(i));
        deck.SetQuery(q);

        RootClick(root, deck, deck.Geometry.ChipMore!.Value);
        deck.SetQuery(deck.Query.Without(1));   // LongName(0), the first listed, goes
        GearQuery before = deck.Query;
        deck.Menu.Selected!(0);
        Assert.Equal(before, deck.Query);
    }

    [Fact]
    public void HidingTheField_WithoutAPlayer_ReleasesItsFocus()
    {
        uint player = Player;
        var root = new UiRoot { Width = 1200, Height = 900 };
        UiGearDeck deck = Make(player: () => player);
        root.AddChild(deck);
        root.SetKeyboardFocus(deck.QueryInput);
        Assert.Same(deck.QueryInput, root.KeyboardFocus);

        player = 0u;
        deck.Refresh();
        Assert.False(deck.QueryInput.Visible);
        Assert.Null(root.KeyboardFocus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Enter_WithNothingToTake_ReleasesFocus(string text)
    {
        var (root, deck) = Rooted();
        Type(root, deck, text);
        root.OnKeyDown((int)Silk.NET.Input.Key.Enter);
        Assert.Empty(deck.Query.Chips);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void Enter_ThatTakesASuggestion_KeepsFocus_ThenABlankEnterReleasesIt()
    {
        var (root, deck) = Rooted();
        Type(root, deck, "steel");
        root.OnKeyDown((int)Silk.NET.Input.Key.Enter);
        Assert.Single(deck.Query.Chips);
        Assert.Same(deck.QueryInput, root.KeyboardFocus);

        root.OnKeyDown((int)Silk.NET.Input.Key.Enter);
        Assert.Null(root.KeyboardFocus);
    }

    [Fact]
    public void ARightClickOffTheMorePill_OpensNoMenu()
    {
        var (_, deck) = Rooted();
        var (x, y) = Centre(deck, deck.Geometry.Contents);
        Assert.False(deck.Menu.TryOpen(x, y));
    }

    [Fact]
    public void PackCellsInResults_StillTogglePackChips_LabelledByTheirPlace()
    {
        var (root, deck) = Rooted();
        deck.SetQuery(GearQuery.Empty.With(GearChip.OfMaterial(Steel)));
        RootClick(root, deck, deck.Geometry.SidePack(1));
        RootClick(root, deck, deck.Geometry.SidePack(0));
        Assert.Equal(new[] { GearChip.OfMaterial(Steel), GearChip.InPack(Bag), GearChip.InPack(Player) }, deck.Query.Chips);
        Assert.Equal(new[] { "Steel", "Pack 2", "Main" }, deck.ChipLabels);
    }

    [Fact]
    public void APackChip_KeepsThePillsAndGeometry_ThroughATableChangeThatLeavesThePacks_ButFollowsAPackGoing()
    {
        ClientObjectTable table = World();
        UiGearDeck deck = Make(world: table);
        deck.SetQuery(GearQuery.Empty.With(GearChip.OfMaterial(Steel)).With(GearChip.InPack(Bag)));
        deck.Refresh();
        GearDeckGeometry before = deck.Geometry;
        Assert.Equal(new[] { "Steel", "Pack 2" }, deck.ChipLabels);

        Place(table, new ClientObject { ObjectId = 0xE01, Name = "Apple", Type = ItemType.Food }, Player, 6);   // no match
        deck.Refresh();
        Assert.Same(before, deck.Geometry);

        Assert.True(table.Remove(Bag));
        deck.Refresh();
        Assert.Equal(new[] { "Steel", "Pack ?" }, deck.ChipLabels);
    }

    [Fact]
    public void AChip_SaysClickToRemove()
    {
        double now = 0;
        UiGearDeck deck = Make(clock: () => now);
        deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        var (x, y) = Centre(deck, deck.Geometry.Chips[0]);
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, UiEventType.MouseMove, Data1: x, Data2: y));
        now = 10;
        Assert.Equal("Click to remove", deck.VisibleTooltip?.Title);
    }

    [Fact]
    public void TheField_SitsOnTheBar_AndHidesWithoutAPlayer()
    {
        UiGearDeck deck = Make();
        Assert.True(deck.QueryInput.Visible);
        Assert.Equal(AnchorEdges.None, deck.QueryInput.Anchors);
        Assert.InRange(deck.QueryInput.Left, deck.Geometry.QueryField.X, deck.Geometry.QueryField.X + 4f);
        Assert.Equal(deck.Geometry.QueryField.Y, deck.QueryInput.Top);

        var session = new GearDeckSession(null, () => "", () => false);
        var empty = new UiGearDeck(new GearDeckBindings(() => new ClientObjectTable(), () => 0u, () => null,
            (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u, o => o.Name, session, new NoActions(), _ => false, () => { }), null, null);
        empty.Refresh();
        Assert.False(empty.QueryInput.Visible);
    }

    // ---- draw probes ----

    private static void AssertInside(TextRenderer renderer, float height)
    {
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                Assert.InRange(s.Verts[i], -0.01f, GearDeckGeometry.Width + 0.01f);
                Assert.InRange(s.Verts[i + 1], -0.01f, height + 0.01f);
            }
    }

    private static void DrawInside(UiGearDeck deck)
    {
        AssertInside(MarkupDrawProbe.Draw(deck, GearDeckGeometry.Width, deck.Height), deck.Height);
        AssertInside(MarkupDrawProbe.DrawOverlays(deck, GearDeckGeometry.Width, deck.Height), deck.Height);
    }

    public static TheoryData<bool> WithFonts => new() { false, true };

    [Theory]
    [MemberData(nameof(WithFonts))]
    public void ThreeChips_Overflow_AndOpenSuggestions_DrawInsideTheFrame(bool fonts)
    {
        DeckFonts? f = fonts ? new DeckFonts((_, _) => null!) : null;

        var (_, three) = Rooted(f);
        three.SetQuery(GearQuery.Empty.With(GearChip.WornOnly).With(GearChip.OfMaterial(Steel)).With(GearChip.InPack(Bag)));
        DrawInside(three);

        var (_, overflow) = Rooted(f);
        GearQuery q = GearQuery.Empty;
        for (int i = 0; i < 6; i++) q = q.With(LongName(i));
        overflow.SetQuery(q);
        Assert.NotNull(overflow.Geometry.ChipMore);
        DrawInside(overflow);

        var (root, typing) = Rooted(f);
        typing.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        Type(root, typing, "steel");
        typing.Refresh();
        Assert.True(typing.Geometry.Input.Suggestions > 0);
        TextRenderer overlay = MarkupDrawProbe.DrawOverlays(typing, GearDeckGeometry.Width, typing.Height);
        Assert.Contains(overlay.DebugSpriteSegmentVerts, s => s.Verts.Count > 0);
        DrawInside(typing);
    }
}
