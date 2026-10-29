using AcDream.App.Rendering;
using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>The sort menu and direction stud, the star save flow and the saved-filter row.</summary>
public sealed class UiGearDeckSavedFilterTests
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
        return t;
    }

    private static (UiRoot Root, UiGearDeck Deck) Rooted(DeckFonts? fonts = null)
    {
        ClientObjectTable table = World();
        var session = new GearDeckSession(null, () => "", () => false);
        var b = new GearDeckBindings(() => table, () => Player, () => 100,
            (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u, o => o.Name, session, new NoActions(), _ => false, () => { });
        var root = new UiRoot { Width = 1200, Height = 900 };
        var deck = new UiGearDeck(b, fonts, null, () => 0.0);
        root.AddChild(deck);
        deck.Refresh();
        return (root, deck);
    }

    private static (int X, int Y) Centre(UiGearDeck deck, DeckRect r)
    {
        var o = deck.ScreenPosition;
        return ((int)(o.X + r.X + r.W / 2f), (int)(o.Y + r.Y + r.H / 2f));
    }

    private static void RootClick(UiRoot root, UiGearDeck deck, DeckRect r, UiMouseButton button = UiMouseButton.Left)
    {
        var (x, y) = Centre(deck, r);
        root.OnMouseMove(x, y);
        root.OnMouseDown(button, x, y);
        root.OnMouseUp(button, x, y);
    }

    private static GearQuery Steely => GearQuery.Empty.With(GearChip.OfMaterial(Steel));

    /// <summary>Saves <paramref name="query"/> as <paramref name="name"/> through the star and its name field.</summary>
    private static void Save(UiRoot root, UiGearDeck deck, GearQuery query, string name)
    {
        deck.SetQuery(query);
        RootClick(root, deck, deck.Geometry.SaveStud);
        Assert.True(deck.SaveNameInput.Visible);
        deck.SaveNameInput.SetText(name);
        deck.SaveNameInput.OnSubmit!(name);
    }

    [Fact]
    public void ChoosingWorkFromTheSortMenu_SortsByWorkmanshipDescending_AndTheDirectionStudFlipsIt()
    {
        var (root, deck) = Rooted();
        RootClick(root, deck, deck.Geometry.SortStud);
        Assert.True(deck.Menu.IsOpen);
        Assert.Equal(new[] { "* Name", "Type", "Material", "Work", "Value", "Burden", "Stack" }, deck.Menu.ItemsSource());

        deck.Menu.Selected!(3);
        deck.Menu.Close();
        Assert.Equal(new GearSort(GearSortField.Workmanship, true), deck.Query.Sort);

        RootClick(root, deck, deck.Geometry.DirStud);
        Assert.Equal(new GearSort(GearSortField.Workmanship, false), deck.Query.Sort);
        RootClick(root, deck, deck.Geometry.DirStud);
        Assert.True(deck.Query.Sort.Descending);
    }

    [Fact]
    public void TheStarWithAnEmptyQuery_DoesNothing()
    {
        var (root, deck) = Rooted();
        RootClick(root, deck, deck.Geometry.SaveStud);
        Assert.False(deck.SaveNameInput.Visible);
    }

    [Fact]
    public void TheStar_OpensThePrefilledNameField_AndEnterSavesAndGrowsTheWindow()
    {
        var (root, deck) = Rooted();
        float before = deck.MinHeight;
        deck.SetQuery(Steely);
        RootClick(root, deck, deck.Geometry.SaveStud);
        Assert.True(deck.SaveNameInput.Visible);
        Assert.Equal("Steel", deck.SaveNameInput.Text);
        Assert.Same(deck.SaveNameInput, root.KeyboardFocus);

        deck.SaveNameInput.OnSubmit!("Best steel");
        Assert.False(deck.SaveNameInput.Visible);
        SavedFilter saved = Assert.Single(deck.Bindings.Session.Settings.SavedFilters);
        Assert.Equal("Best steel", saved.Name);
        Assert.Equal(Steely, saved.Query);
        Assert.Single(deck.Geometry.SavedStuds);
        Assert.Equal(before + GearDeckGeometry.SavedH + GearDeckGeometry.Gap, deck.MinHeight);
    }

    [Fact]
    public void EscapeAndFocusLoss_CancelTheName()
    {
        var (root, deck) = Rooted();
        deck.SetQuery(Steely);
        RootClick(root, deck, deck.Geometry.SaveStud);
        deck.SaveNameInput.OnEscape!();
        Assert.False(deck.SaveNameInput.Visible);

        RootClick(root, deck, deck.Geometry.SaveStud);
        deck.SaveNameInput.OnFocusLost!("Lost");
        Assert.False(deck.SaveNameInput.Visible);
        Assert.Empty(deck.Bindings.Session.Settings.SavedFilters);
    }

    [Fact]
    public void ClickingASavedStud_AppliesItsQuery_AndASecondClickClearsIt()
    {
        var (root, deck) = Rooted();
        Save(root, deck, Steely, "Steel");
        deck.SetQuery(GearQuery.Empty);

        RootClick(root, deck, deck.Geometry.SavedStuds[0]);
        Assert.Equal(Steely, deck.Query);
        RootClick(root, deck, deck.Geometry.SavedStuds[0]);
        Assert.True(deck.Query.IsEmpty);
    }

    [Fact]
    public void ARightClickOnASavedStud_ListsDelete_AndChoosingItRemovesTheFilter()
    {
        var (root, deck) = Rooted();
        Save(root, deck, Steely, "Steel");
        RootClick(root, deck, deck.Geometry.SavedStuds[0], UiMouseButton.Right);
        Assert.True(deck.Menu.IsOpen);
        Assert.Equal(new[] { "Delete" }, deck.Menu.ItemsSource());

        deck.Menu.Selected!(0);
        Assert.Empty(deck.Bindings.Session.Settings.SavedFilters);
        Assert.Empty(deck.Geometry.SavedStuds);
    }

    [Fact]
    public void SavingTheSameNameInAnotherCase_ReplacesTheFirst()
    {
        var (root, deck) = Rooted();
        Save(root, deck, Steely, "Steel");
        Save(root, deck, GearQuery.Empty.With(GearChip.WornOnly), "STEEL");
        SavedFilter saved = Assert.Single(deck.Bindings.Session.Settings.SavedFilters);
        Assert.Equal("STEEL", saved.Name);
        Assert.Equal(GearQuery.Empty.With(GearChip.WornOnly), saved.Query);
    }

    [Fact]
    public void TheMoreMenu_ListsTheHiddenFilters_AndChoosingOneAppliesIt()
    {
        var (root, deck) = Rooted();
        for (int i = 0; i < 6; i++)
            Save(root, deck, GearQuery.Empty.With(GearChip.NameContains($"filter number {i}")), $"Filter number {i}");
        Assert.NotNull(deck.Geometry.SavedMore);
        int shown = deck.Geometry.SavedStuds.Count;

        RootClick(root, deck, deck.Geometry.SavedMore!.Value);
        Assert.True(deck.Menu.IsOpen);
        Assert.Equal(6 - shown, deck.Menu.ItemsSource().Count);
        Assert.Equal($"Filter number {shown}", deck.Menu.ItemsSource()[0]);

        deck.Menu.Selected!(0);
        Assert.Equal(GearQuery.Empty.With(GearChip.NameContains($"filter number {shown}")), deck.Query);
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
    public void SavedFilters_Overflow_AndTheNameField_DrawInsideTheFrame(bool fonts)
    {
        DeckFonts? f = fonts ? new DeckFonts((_, _) => null!) : null;

        var (root, saved) = Rooted(f);
        Save(root, saved, Steely, "Steel");
        saved.SetQuery(Steely);
        DrawInside(saved);

        var (root2, overflow) = Rooted(f);
        for (int i = 0; i < 6; i++)
            Save(root2, overflow, GearQuery.Empty.With(GearChip.NameContains($"filter number {i}")), $"Long filter {i} name");
        Assert.NotNull(overflow.Geometry.SavedMore);
        DrawInside(overflow);

        var (root3, naming) = Rooted(f);
        naming.SetQuery(Steely);
        RootClick(root3, naming, naming.Geometry.SaveStud);
        Assert.True(naming.SaveNameInput.Visible);
        DrawInside(naming);
    }
}
