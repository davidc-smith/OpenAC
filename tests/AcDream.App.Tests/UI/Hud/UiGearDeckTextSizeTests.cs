using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckTextSizeTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, First = 0xA00;

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

    private static HudText At(int percent) => new(HudTextSettings.Default.WithGlobal(percent));

    private static UiGearDeck Make(HudText text, DeckFonts? fonts = null, bool list = false, float scale = 1f)
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        for (int i = 0; i < 30; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"A long item name {i}", Type = ItemType.Misc, StackSize = 25 }, Player, i);
        var session = new GearDeckSession(null, () => "", () => false);
        if (list) session.Update(s => s with { View = GearPackView.List });
        var b = new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
            o => o.Name, session, new NoActions(), _ => false, () => { }, Text: text);
        var deck = new UiGearDeck(b, fonts, null, () => 0.0);
        if (scale != 1f) deck.SetScale(scale);
        deck.Refresh();
        return deck;
    }

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(150)]
    public void TheWindowKeepsItsSize_AtEveryTextSize(int percent)
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        UiGearDeck plain = Make(new HudText(), fonts);
        UiGearDeck deck = Make(At(percent), fonts);
        Assert.Equal(plain.Width, deck.Width);
        Assert.Equal(plain.Height, deck.Height);
        Assert.Equal(plain.MinHeight, deck.MinHeight);
    }

    [Fact]
    public void ListAndSuggestionRowsGrow_FixedBoxesDoNot()
    {
        DeckFonts fonts = FakeDeckFonts.Make();
        GearDeckGeometry normal = Make(new HudText(), fonts, list: true).Geometry;
        GearDeckGeometry large = Make(At(150), fonts, list: true).Geometry;

        Assert.True(large.ListRow(0, 0)!.Value.H > normal.ListRow(0, 0)!.Value.H);
        Assert.True(large.Suggestion(0).H > normal.Suggestion(0).H);
        Assert.True(large.VisibleListRows < normal.VisibleListRows);
        Assert.Equal(normal.SortStud, large.SortStud);
        Assert.Equal(normal.SidePack(0), large.SidePack(0));
        Assert.Equal(normal.Footer, large.Footer);
        Assert.Equal(normal.Title, large.Title);
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(false, 0.75f)]
    [InlineData(true, 0.75f)]
    public void AtLargeText_NoFaceIsTallerThanItsBox(bool list, float scale)
    {
        var fits = new List<DeckFit>();
        UiGearDeck deck = Make(At(150), FakeDeckFonts.Make(fits), list, scale);
        MarkupDrawProbe.Draw(deck, deck.Width, deck.Height);
        Assert.NotEmpty(fits);
        FakeDeckFonts.AssertAllFit(fits);
        Assert.Contains(fits, f => MathF.Abs(f.BoxHeight - deck.Geometry.Footer.H) < 0.01f);
    }

    [Fact]
    public void ARightClickOnTheTitle_ListsTextSize_AndAChoiceOverridesOnlyThisDeck()
    {
        HudText text = At(110);
        UiGearDeck deck = Make(text);
        var root = new UiRoot { Width = 1200, Height = 900 };
        root.AddChild(deck);
        deck.Refresh();
        DeckRect title = deck.Geometry.Title;
        HudTestInput.RightClick(root, deck, title.X + 40f, title.Y + title.H / 2f);

        Assert.True(deck.Menu.IsOpen);
        Assert.Equal(HudTextMenu.Labels(text.Settings, HudDeck.Gear), deck.Menu.ItemsSource());
        Assert.False(deck.Menu.ItemsEnabledSource!()[0]);

        deck.Menu.Selected!(9);   // 150%
        Assert.Equal(150, text.Settings.PercentFor(HudDeck.Gear));
        Assert.Equal(110, text.Settings.PercentFor(HudDeck.Command));
        Assert.Equal(1.5f, deck.TextFactor, 3);
    }

    [Fact]
    public void AChange_RebuildsAtOnce_AndSameAsHudFollowsTheGlobal()
    {
        var text = new HudText();
        UiGearDeck deck = Make(text, FakeDeckFonts.Make(), list: true);
        float before = deck.Geometry.ListRow(0, 0)!.Value.H;

        text.SetGlobal(150);
        Assert.Equal(1.5f, deck.TextFactor, 3);
        Assert.True(deck.Geometry.ListRow(0, 0)!.Value.H > before);

        text.SetOverride(HudDeck.Gear, 80);
        Assert.Equal(0.8f, deck.TextFactor, 3);
        Assert.Equal(before, deck.Geometry.ListRow(0, 0)!.Value.H, 3);

        text.SetOverride(HudDeck.Gear, null);
        Assert.Equal(1.5f, deck.TextFactor, 3);

        deck.Detach();
        text.SetGlobal(100);
        Assert.Equal(1.5f, deck.TextFactor, 3);   // no longer listening
    }

    [Fact]
    public void TheDeckMenuAndQueryFieldFollowTheTextSize()
    {
        UiGearDeck deck = Make(At(150), FakeDeckFonts.Make());
        Assert.Equal(FakeDeckFonts.Line(18f), deck.Menu.DatFont!.LineHeight);
        Assert.Equal(6f, deck.Menu.RowGrow, 3);
        // The field's box is 22 - 2 x 4 padding = 14: the 18 px face (line 20) drops to the 12 px face (line 14).
        Assert.Equal(FakeDeckFonts.Line(12f), deck.QueryInput.DatFont!.LineHeight);
    }
}
