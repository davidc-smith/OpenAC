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

public sealed class UiGearDeckResultsTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Helm = 0xC1;
    private const uint Salvage75 = 0xD1, Salvage60 = 0xD2, Salvage90 = 0xD3, Food = 0xD4, First = 0xA00;
    private const uint Steel = 64;

    private sealed class Fake : IGearItemActions
    {
        public readonly List<string> Calls = [];
        public bool Ready() => true;
        public void Report(string text) => Calls.Add($"report {text}");
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) { Calls.Add($"put {item:X} {container:X} {placement}"); return true; }
        public bool Merge(uint from, uint to) { Calls.Add($"merge {from:X} {to:X}"); return true; }
        public bool WieldFromPaperdoll(uint item, EquipMask mask) { Calls.Add($"wield {item:X} {mask}"); return true; }
        public bool WieldSecondary(uint item) { Calls.Add($"secondary {item:X}"); return true; }
        public bool Activate(uint item) { Calls.Add($"use {item:X}"); return true; }
        public bool OpenPack(uint pack) { Calls.Add($"open {pack:X}"); return true; }
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

    private static ClientObject Salvage(uint id, float work) => new()
    {
        ObjectId = id, Name = $"Salvaged Steel {work}", Type = ItemType.TinkeringMaterial,
        MaterialType = Steel, Workmanship = work, WeenieClassId = 0x1000u + id,
    };

    /// <summary>The main pack holds two steel salvage bags and three misc items, the bag one
    /// steel salvage and some food, and a steel helm is worn.</summary>
    private static ClientObjectTable World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        Place(t, Salvage(Salvage75, 7.5f), Player, 0);
        Place(t, Salvage(Salvage60, 6.0f), Player, 1);
        for (int i = 0; i < 3; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"Item {i}", Type = ItemType.Misc }, Player, 2 + i);
        Place(t, Salvage(Salvage90, 9.0f), Bag, 0);
        Place(t, new ClientObject { ObjectId = Food, Name = "Apple", Type = ItemType.Food }, Bag, 1);
        Wear(t, new ClientObject
        {
            ObjectId = Helm, WielderId = Player, Name = "Steel Helm", Type = ItemType.Armor, MaterialType = Steel,
            ValidLocations = EquipMask.HeadWear,
        }, EquipMask.HeadWear);
        return t;
    }

    private sealed class Rig
    {
        public required UiGearDeck Deck;
        public required Fake Actions;
        public required GearDeckSession Session;
        public required List<string> Log;
    }

    private static Rig Make(Func<GearDeckSettings, GearDeckSettings>? settings = null, DeckFonts? fonts = null,
        EmbeddedSvgSpriteCache? icons = null, uint icon = 0u, Func<(bool Toggle, bool Range)>? modifiers = null)
    {
        ClientObjectTable table = World();
        var session = new GearDeckSession(null, () => "", () => false);
        if (settings is not null) session.Update(settings);
        var actions = new Fake();
        var log = new List<string>();
        var b = new GearDeckBindings(
            Objects: () => table,
            PlayerGuid: () => Player,
            Strength: () => 100,
            ResolveIcon: (_, _, _, _, _) => icon,
            ResolveDragIcon: (_, _, _, _, _) => 77u,
            DisplayName: o => o.Name,
            Session: session,
            Actions: actions,
            IsPending: _ => false,
            Close: () => { },
            Log: log.Add,
            Modifiers: modifiers);
        var deck = new UiGearDeck(b, fonts, icons, () => 0.0);
        var rig = new Rig { Deck = deck, Actions = actions, Session = session, Log = log };
        deck.Refresh();
        return rig;
    }

    private static (float X, float Y) Centre(DeckRect r) => (r.X + r.W / 2f, r.Y + r.H / 2f);

    private static void Send(UiGearDeck deck, int type, DeckRect at, object? payload = null, int data0 = 0)
    {
        var (x, y) = Centre(at);
        deck.HitTestLocal(x, y);
        deck.OnEvent(new UiEvent(0, deck, type, Data0: data0, Data1: (int)x, Data2: (int)y, Payload: payload));
    }

    private static void Click(UiGearDeck deck, DeckRect at)
    {
        Send(deck, UiEventType.MouseDown, at);
        Send(deck, UiEventType.Click, at);
    }

    private static void DoubleClick(UiGearDeck deck, DeckRect at)
    {
        Click(deck, at);
        Click(deck, at);
        Send(deck, UiEventType.DoubleClick, at);
    }

    private static GearQuery SteelBy(GearSort sort) => GearQuery.Empty.With(GearChip.OfMaterial(Steel)) with { Sort = sort };

    private static int IndexOf(UiGearDeck deck, uint id)
    {
        for (int i = 0; i < deck.Shown.Count; i++)
            if (deck.Shown[i].Id == id) return i;
        throw new InvalidOperationException($"{id:X} not shown");
    }

    private static DeckRect Cell(UiGearDeck deck, int index) => deck.Geometry.GridCell(index, deck.ScrollRow)!.Value;

    [Fact]
    public void AMaterialChip_ShowsTheSalvageFromEveryPack_ButNotTheWornHelm()
    {
        Rig r = Make();
        Assert.False(r.Deck.ShowsResults);
        r.Deck.SetQuery(GearQuery.Empty.With(GearChip.OfMaterial(Steel)));
        Assert.True(r.Deck.ShowsResults);
        Assert.Equal(new[] { Salvage75, Salvage60, Salvage90 }, r.Deck.Shown.Select(v => v.Id).Order());
        Assert.Equal(3, r.Deck.Geometry.Input.ItemCount);
        Assert.Equal("3 of 7 match", r.Deck.MatchText);
    }

    [Fact]
    public void AWorkmanshipSort_OrdersTheResultsBestFirst()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(new GearSort(GearSortField.Workmanship, true)));
        Assert.Equal(new[] { Salvage90, Salvage75, Salvage60 }, r.Deck.Shown.Select(v => v.Id));
    }

    [Fact]
    public void AnEmptyQuery_RestoresTheSelectedPacksRowsInSlotOrder()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        r.Deck.SetQuery(GearQuery.Empty);
        Assert.False(r.Deck.ShowsResults);
        Assert.Equal(r.Deck.Items.Select(o => o.ObjectId), r.Deck.Shown.Select(v => v.Id));
        Assert.Equal(new[] { Salvage75, Salvage60, First, First + 1, First + 2 }, r.Deck.Shown.Select(v => v.Id));
    }

    [Fact]
    public void ClickingSidePack1_WhileResultsAreShown_TogglesAPackChipWithoutOpening()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        Click(r.Deck, r.Deck.Geometry.SidePack(1));
        Assert.Contains(GearChip.InPack(Bag), r.Deck.Query.Chips);
        Assert.Equal(new[] { Salvage90 }, r.Deck.Shown.Select(v => v.Id));
        Assert.Equal(Player, r.Deck.SelectedPackId);
        Click(r.Deck, r.Deck.Geometry.SidePack(1));
        Assert.DoesNotContain(GearChip.InPack(Bag), r.Deck.Query.Chips);
        Assert.Equal(3, r.Deck.Shown.Count);
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void DoubleClickOnTheBagsResultRow_UsesIt()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        DoubleClick(r.Deck, Cell(r.Deck, IndexOf(r.Deck, Salvage90)));
        Assert.Equal([$"use {Salvage90:X}"], r.Actions.Calls);
    }

    [Fact]
    public void ADropOnTheBagsResultRow_PutsIntoTheBagAtThatRowsSlot()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(new GearSort(GearSortField.Workmanship, true)));
        int index = IndexOf(r.Deck, Salvage90);
        var payload = new ItemDragPayload(First, ItemDragSource.Inventory, 0, null);
        var (x, y) = Centre(Cell(r.Deck, index));
        Assert.True(r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DropReleased, Data1: (int)x, Data2: (int)y, Payload: payload)));
        Assert.Equal([$"put {First:X} B1 0"], r.Actions.Calls);
    }

    [Fact]
    public void ADropOnAResultRow_UsesTheRowsOwnSlot_NotItsPlaceInTheResults()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(new GearSort(GearSortField.Workmanship, false)));   // 6.0, 7.5, then the bag's 9.0
        int index = IndexOf(r.Deck, Salvage90);
        Assert.Equal(2, index);
        Assert.Equal(0, r.Deck.Shown[index].Slot);
        Drop(r, Cell(r.Deck, index));
        Assert.Equal([$"put {First:X} B1 0"], r.Actions.Calls);   // slot 0 of the bag, not 2
    }

    [Fact]
    public void ADropOnAWornResultRow_WieldsIntoThatItemsSlot()
    {
        Rig r = Make();
        r.Deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        Drop(r, Cell(r.Deck, 0));
        Assert.Contains(r.Log, l => l.Contains("on Item 0 -> DropOnSlot"));
    }

    [Fact]
    public void ADropOnAPackResultRow_PutsIntoThatPack()
    {
        Rig r = Make();
        r.Deck.SetQuery(GearQuery.Empty.With(GearChip.OfType(GearTypeGroup.Containers)));
        GearItemView bag = Assert.Single(r.Deck.Shown);
        Assert.True(bag.IsPack);
        Drop(r, Cell(r.Deck, 0));
        Assert.Contains(r.Log, l => l.Contains("on Item 0 -> DropOnPack"));
        Assert.Contains(r.Actions.Calls, c => c.StartsWith($"put {First:X} B1 "));
    }

    private static void Drop(Rig r, DeckRect at)
    {
        var payload = new ItemDragPayload(First, ItemDragSource.Inventory, 0, null);
        var (x, y) = Centre(at);
        Assert.True(r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DropReleased, Data1: (int)x, Data2: (int)y, Payload: payload)));
    }

    [Fact]
    public void ADoubleClickOnASidePack_WhileResultsAreShown_TogglesItsChipOnce()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        DoubleClick(r.Deck, r.Deck.Geometry.SidePack(1));
        Assert.Contains(GearChip.InPack(Bag), r.Deck.Query.Chips);
        DoubleClick(r.Deck, r.Deck.Geometry.SidePack(1));
        Assert.DoesNotContain(GearChip.InPack(Bag), r.Deck.Query.Chips);
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void WhileResultsAreShown_OnlyPacksWithAChipAreOutlined()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        TextRenderer before = MarkupDrawProbe.Draw(r.Deck, GearDeckGeometry.Width, r.Deck.Height);
        Assert.False(OutlinedLeft(before, r.Deck.Geometry.SidePack(0)), "the selected main pack is not outlined in results");
        Assert.False(OutlinedLeft(before, r.Deck.Geometry.SidePack(1)));

        Click(r.Deck, r.Deck.Geometry.SidePack(1));
        TextRenderer after = MarkupDrawProbe.Draw(r.Deck, GearDeckGeometry.Width, r.Deck.Height);
        Assert.True(OutlinedLeft(after, r.Deck.Geometry.SidePack(1)));
        Assert.False(OutlinedLeft(after, r.Deck.Geometry.SidePack(0)));
    }

    /// <summary>A handle-coloured vertex just outside <paramref name="r"/>'s left edge, within its height (the
    /// outline's 2-point stroke lies a point outside the cell; a neighbour's stays above or below it).</summary>
    private static bool OutlinedLeft(TextRenderer renderer, DeckRect r)
    {
        Vector4 c = DeckChrome.Handle;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
        {
            if (s.Texture != 0u) continue;
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                float x = s.Verts[i], y = s.Verts[i + 1];
                if (x >= r.X - 3.5f && x <= r.X - 0.5f && y >= r.Y && y <= r.Y + r.H
                    && MathF.Abs(s.Verts[i + 4] - c.X) < 0.01f && MathF.Abs(s.Verts[i + 5] - c.Y) < 0.01f
                    && MathF.Abs(s.Verts[i + 6] - c.Z) < 0.01f)
                    return true;
            }
        }
        return false;
    }

    [Fact]
    public void ADropOnEmptyResultsSpace_IsNotATarget()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        var payload = new ItemDragPayload(First, ItemDragSource.Inventory, 0, null);
        var (x, y) = Centre(Cell(r.Deck, 5));
        r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DragEnter, Data1: (int)x, Data2: (int)y, Payload: payload));
        Assert.Null(r.Deck.DropHighlight);
        Assert.False(r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DropReleased, Data1: (int)x, Data2: (int)y, Payload: payload)));
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void AWornChip_ShowsTheHelm_AndADoubleClickTakesItOff()
    {
        Rig r = Make();
        r.Deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        Assert.Equal(new[] { Helm }, r.Deck.Shown.Select(v => v.Id));
        DoubleClick(r.Deck, Cell(r.Deck, 0));
        Assert.Single(r.Actions.Calls);
        Assert.StartsWith($"put {Helm:X} ", r.Actions.Calls[0]);
    }

    [Fact]
    public void AWornResultRow_DragsAsEquipment_AndOthersAsInventory()
    {
        Rig r = Make();
        r.Deck.SetQuery(GearQuery.Empty.With(GearChip.WornOnly));
        Send(r.Deck, UiEventType.MouseDown, Cell(r.Deck, 0));
        Assert.Equal(ItemDragSource.Equipment, Assert.IsType<ItemDragPayload>(r.Deck.GetDragPayload()).SourceKind);
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        Send(r.Deck, UiEventType.MouseDown, Cell(r.Deck, 0));
        ItemDragPayload p = Assert.IsType<ItemDragPayload>(r.Deck.GetDragPayload());
        Assert.Equal(ItemDragSource.Inventory, p.SourceKind);
        Assert.Equal(r.Deck.Shown[0].Id, p.ObjId);
    }

    [Fact]
    public void EndingTheSession_ClearsTheQuery()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        r.Session.EndSession();
        Assert.Equal(GearQuery.Empty, r.Deck.Query);
        Assert.False(r.Deck.ShowsResults);
    }

    [Fact]
    public void ADetachedDeck_NoLongerFollowsTheSession()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(GearSort.Default));
        r.Deck.Detach();
        r.Session.EndSession();
        Assert.True(r.Deck.ShowsResults);
    }

    private static void DrawInside(UiGearDeck deck)
    {
        var renderer = MarkupDrawProbe.Draw(deck, GearDeckGeometry.Width, deck.Height);
        int vertices = 0;
        foreach (var s in renderer.DebugSpriteSegmentVerts)
            for (int i = 0; i + 8 <= s.Verts.Count; i += 8)
            {
                vertices++;
                Assert.InRange(s.Verts[i], -0.01f, GearDeckGeometry.Width + 0.01f);
                Assert.InRange(s.Verts[i + 1], -0.01f, deck.Height + 0.01f);
            }
        Assert.True(vertices > 0);
    }

    private static GearQuery Columns() => GearQuery.Empty
        .With(GearChip.OfMaterial(Steel))
        .With(GearChip.Range(GearField.Value, 0, null)) with { Sort = new GearSort(GearSortField.Workmanship, true) };

    [Fact]
    public void ResultsGridAndList_DrawInsideTheFrame_WithoutFonts()
    {
        Rig grid = Make();
        grid.Deck.SetQuery(Columns());
        DrawInside(grid.Deck);
        Rig list = Make(s => s with { View = GearPackView.List });
        list.Deck.SetQuery(Columns());
        DrawInside(list.Deck);
    }

    [Fact]
    public void ResultsGridAndList_DrawInsideTheFrame_WithFontsAndIcons()
    {
        var fonts = new DeckFonts((_, _) => null!);
        using var icons = new EmbeddedSvgSpriteCache(new FakeBackend());
        Rig grid = Make(fonts: fonts, icons: icons, icon: 7u);
        grid.Deck.SetQuery(Columns());
        DrawInside(grid.Deck);
        Rig list = Make(s => s with { Folded = true, View = GearPackView.List }, fonts, icons, 7u);
        list.Deck.SetQuery(Columns());
        DrawInside(list.Deck);
    }

    // ---- sorting the plain pack view ----

    /// <summary>A sort with no filter: the main pack's five items by name, Z to A.</summary>
    private static readonly GearQuery NameZtoA = GearQuery.Empty with { Sort = new GearSort(GearSortField.Name, true) };
    private static readonly uint[] MainZtoA = [Salvage75, Salvage60, First + 2, First + 1, First];
    private static readonly uint[] MainBySlot = [Salvage75, Salvage60, First, First + 1, First + 2];

    private static DeckRect Row(Rig r, int index, bool list) =>
        list ? r.Deck.Geometry.ListRow(index, r.Deck.ScrollRow)!.Value : Cell(r.Deck, index);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASortWithNoFilter_SortsTheOpenPack_InGridAndList(bool list)
    {
        Rig r = Make(list ? s => s with { View = GearPackView.List } : null);
        Assert.Equal(MainBySlot, r.Deck.Shown.Select(v => v.Id));
        r.Deck.SetQuery(NameZtoA);
        Assert.False(r.Deck.ShowsResults);
        Assert.Equal(MainZtoA, r.Deck.Shown.Select(v => v.Id));
        Assert.Equal(5, r.Deck.Geometry.Input.ItemCount);   // compacted: just the pack's items
        Assert.Equal("", r.Deck.FooterLeftText);   // no match text: this is the pack view

        // The row under the pointer is the shown one: it drags, and shows its caption.
        Send(r.Deck, UiEventType.MouseDown, Row(r, 2, list));
        Assert.Equal(First + 2, Assert.IsType<ItemDragPayload>(r.Deck.GetDragPayload()).ObjId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADropOnASortedPackRow_UsesThatRowsOwnSlot(bool list)
    {
        Rig r = Make(list ? s => s with { View = GearPackView.List } : null);
        r.Deck.SetQuery(NameZtoA);
        Assert.Equal(4, r.Deck.Shown[2].Slot);   // Item 2, shown third
        var payload = new ItemDragPayload(Food, ItemDragSource.Inventory, 0, null);
        var (x, y) = Centre(Row(r, 2, list));
        Assert.True(r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DropReleased, Data1: (int)x, Data2: (int)y, Payload: payload)));
        Assert.Equal([$"put {Food:X} {Player:X} 4"], r.Actions.Calls);
    }

    [Fact]
    public void ADropOnEmptySpace_OfASortedPack_StillPutsAfterTheLastItem()
    {
        Rig r = Make();
        r.Deck.SetQuery(NameZtoA);
        var payload = new ItemDragPayload(Food, ItemDragSource.Inventory, 0, null);
        var (x, y) = Centre(Cell(r.Deck, 7));
        Assert.True(r.Deck.OnEvent(new UiEvent(0, r.Deck, UiEventType.DropReleased, Data1: (int)x, Data2: (int)y, Payload: payload)));
        Assert.Equal([$"put {Food:X} {Player:X} 5"], r.Actions.Calls);
    }

    [Fact]
    public void ARangeClick_InASortedPack_SpansTheShownOrder()
    {
        (bool Toggle, bool Range) mods = (false, false);
        Rig r = Make(modifiers: () => mods);
        r.Deck.SetQuery(NameZtoA);
        Click(r.Deck, Cell(r.Deck, 1));
        mods = (false, true);
        Click(r.Deck, Cell(r.Deck, 3));
        Assert.Equal(new[] { Salvage60, First + 2, First + 1 }.Order(), r.Deck.Selection.Order());
    }

    [Fact]
    public void DoubleClickingASortedPackRow_UsesThatItem()
    {
        Rig r = Make();
        r.Deck.SetQuery(NameZtoA);
        DoubleClick(r.Deck, Cell(r.Deck, 2));
        Assert.Equal([$"use {First + 2:X}"], r.Actions.Calls);
    }

    [Fact]
    public void ASortWithAFilter_StillSortsTheResults_FromEveryPack()
    {
        Rig r = Make();
        r.Deck.SetQuery(SteelBy(new GearSort(GearSortField.Workmanship, false)));
        Assert.Equal(new[] { Salvage60, Salvage75, Salvage90 }, r.Deck.Shown.Select(v => v.Id));
        r.Deck.SetQuery(SteelBy(GearSort.Default));   // Pack order: main pack by slot, then the bag
        Assert.Equal(new[] { Salvage75, Salvage60, Salvage90 }, r.Deck.Shown.Select(v => v.Id));
    }

    [Fact]
    public void TheDirectionStud_IsDimAndInert_UnderPackOrderOnly_AndTheSortStudIsNeverDim()
    {
        Rig r = Make();
        Assert.Equal(GearSortField.Pack, r.Deck.Query.Sort.Field);
        Assert.False(r.Deck.SortStudDim);
        Assert.True(r.Deck.DirStudDim);
        Click(r.Deck, r.Deck.Geometry.DirStud);
        Assert.Equal(GearSort.Default, r.Deck.Query.Sort);
        Assert.Equal(MainBySlot, r.Deck.Shown.Select(v => v.Id));

        r.Deck.SetQuery(GearQuery.Empty with { Sort = new GearSort(GearSortField.Name, false) });
        Assert.False(r.Deck.ShowsResults);
        Assert.False(r.Deck.SortStudDim);
        Assert.False(r.Deck.DirStudDim);
        Click(r.Deck, r.Deck.Geometry.DirStud);
        Assert.Equal(new GearSort(GearSortField.Name, true), r.Deck.Query.Sort);
        Assert.Equal(MainZtoA, r.Deck.Shown.Select(v => v.Id));

        r.Deck.SetQuery(SteelBy(GearSort.Default));   // results under Pack order: still dim
        Assert.False(r.Deck.SortStudDim);
        Assert.True(r.Deck.DirStudDim);
    }

    [Fact]
    public void ShowInPack_KeepsTheSort_AndSelectsTheRowWhereItIsShown()
    {
        Rig r = Make();
        r.Deck.SetQuery(GearQuery.Empty.With(GearChip.NameContains("Item 0")) with { Sort = new GearSort(GearSortField.Name, true) });
        Send(r.Deck, UiEventType.MouseDown, Cell(r.Deck, 0));
        r.Deck.Menu.Opening!(Centre(Cell(r.Deck, 0)).X, Centre(Cell(r.Deck, 0)).Y);
        Assert.Equal("Show in pack", r.Deck.Menu.ItemsSource()[4]);
        r.Deck.Menu.Selected!(4);
        Assert.False(r.Deck.ShowsResults);
        Assert.Equal(new GearSort(GearSortField.Name, true), r.Deck.Query.Sort);
        Assert.Equal(MainZtoA, r.Deck.Shown.Select(v => v.Id));
        Assert.Equal(new[] { First }, r.Deck.Selection);
    }
}
