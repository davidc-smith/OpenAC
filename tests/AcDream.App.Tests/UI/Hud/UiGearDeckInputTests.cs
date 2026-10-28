using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class UiGearDeckInputTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Helm = 0xC1, Sword = 0xC2, First = 0xA00;

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

    /// <summary>A player with one bag, <paramref name="loose"/> loose items and two worn items.</summary>
    private static ClientObjectTable World(int loose = 30)
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, Type = ItemType.Creature, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24, Name = "Pack" }, Player, 0);
        for (int i = 0; i < loose; i++)
            Place(t, new ClientObject { ObjectId = First + (uint)i, Name = $"Item {i}", Type = ItemType.Misc }, Player, i);
        Wear(t, new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", ValidLocations = EquipMask.HeadWear }, EquipMask.HeadWear);
        Wear(t, new ClientObject { ObjectId = Sword, WielderId = Player, Name = "Sword", ValidLocations = EquipMask.MeleeWeapon }, EquipMask.MeleeWeapon);
        return t;
    }

    private sealed class Rig
    {
        public required UiGearDeck Deck;
        public required Fake Actions;
        public required GearDeckSession Session;
        public int Closed;
        public double Now;
    }

    private static Rig Make(Func<GearDeckSettings, GearDeckSettings>? settings = null, float? height = null, int loose = 30,
        UiElement? parent = null)
    {
        ClientObjectTable table = World(loose);
        var session = new GearDeckSession(null, () => "", () => false);
        if (settings is not null) session.Update(settings);
        var actions = new Fake();
        Rig rig = null!;
        var b = new GearDeckBindings(
            Objects: () => table,
            PlayerGuid: () => Player,
            Strength: () => 100,
            ResolveIcon: (_, _, _, _, _) => 0u,
            ResolveDragIcon: (_, _, _, _, _) => 77u,
            DisplayName: o => o.Name,
            Session: session,
            Actions: actions,
            IsPending: _ => false,
            Close: () => rig.Closed++);
        var deck = new UiGearDeck(b, null, null, () => rig.Now);
        rig = new Rig { Deck = deck, Actions = actions, Session = session };
        parent?.AddChild(deck);
        if (height is float h) deck.Height = h;
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

    /// <summary>A press and release on one spot, as the root sends it: MouseDown, then Click.</summary>
    private static void Click(UiGearDeck deck, DeckRect at)
    {
        Send(deck, UiEventType.MouseDown, at);
        Send(deck, UiEventType.Click, at);
    }

    /// <summary>A double-click as the root sends it: two clicks, the second followed by DoubleClick.</summary>
    private static void DoubleClick(UiGearDeck deck, DeckRect at)
    {
        Click(deck, at);
        Click(deck, at);
        Send(deck, UiEventType.DoubleClick, at);
    }

    private static int SlotIndex(EquipMask mask)
    {
        for (int i = 0; i < GearSlotCatalog.Slots.Count; i++)
            if (GearSlotCatalog.Slots[i].Mask == mask) return i;
        throw new InvalidOperationException();
    }

    [Fact]
    public void TheFoldStud_FlipsFolded()
    {
        Rig r = Make();
        Click(r.Deck, r.Deck.Geometry.FoldStud);
        Assert.True(r.Session.Settings.Folded);
        Assert.True(r.Deck.Geometry.Input.Folded);
        Click(r.Deck, r.Deck.Geometry.FoldStud);
        Assert.False(r.Session.Settings.Folded);
    }

    [Fact]
    public void TheViewStuds_SetTheirSettings_AndFigureDoesNothing()
    {
        Rig r = Make(s => s with { Folded = true });
        Click(r.Deck, r.Deck.Geometry.ListStud);
        Assert.Equal(GearPackView.List, r.Session.Settings.View);
        Click(r.Deck, r.Deck.Geometry.GridStud);
        Assert.Equal(GearPackView.Grid, r.Session.Settings.View);
        GearDeckSettings before = r.Session.Settings;
        Click(r.Deck, r.Deck.Geometry.FigureStud);
        Assert.Equal(before, r.Session.Settings);
        Click(r.Deck, r.Deck.Geometry.SlotsStud);
        Assert.False(r.Session.Settings.Folded);
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void ClickingSidePack1_SelectsAndOpensIt()
    {
        Rig r = Make(loose: 60);
        Send(r.Deck, UiEventType.Scroll, r.Deck.Geometry.Contents, data0: -1);
        Assert.Equal(1, r.Deck.ScrollRow);
        Click(r.Deck, r.Deck.Geometry.SidePack(1));
        Assert.Equal(Bag, r.Session.Settings.SelectedPack);
        Assert.Equal(Bag, r.Deck.SelectedPackId);
        Assert.Equal(0, r.Deck.ScrollRow);
        Assert.Equal(["open B1"], r.Actions.Calls);
    }

    [Fact]
    public void ClickingTheMainPack_SelectsItWithoutOpening()
    {
        Rig r = Make(s => s with { SelectedPack = Bag });
        Click(r.Deck, r.Deck.Geometry.SidePack(0));
        Assert.Equal(Player, r.Session.Settings.SelectedPack);
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void DoubleClickOnItem0_UsesIt()
    {
        Rig r = Make();
        Send(r.Deck, UiEventType.DoubleClick, r.Deck.Geometry.GridCell(0, 0)!.Value);
        Assert.Equal([$"use {First:X}"], r.Actions.Calls);
    }

    [Fact]
    public void DoubleClickOnAWornSlot_TakesItOff()
    {
        Rig r = Make();
        Send(r.Deck, UiEventType.DoubleClick, r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.HeadWear)));
        Assert.Single(r.Actions.Calls);
        Assert.StartsWith($"put {Helm:X} ", r.Actions.Calls[0]);
    }

    [Fact]
    public void DoubleClickOnAWornCell_TakesItOff()
    {
        Rig r = Make(s => s with { Folded = true });
        uint worn = r.Deck.Model.Worn[0].ObjectId;
        Send(r.Deck, UiEventType.DoubleClick, r.Deck.Geometry.WornCell(0));
        Assert.Single(r.Actions.Calls);
        Assert.StartsWith($"put {worn:X} ", r.Actions.Calls[0]);
    }

    [Fact]
    public void ADropOnSidePack1_PutsInIt()
    {
        Rig r = Make();
        var payload = new ItemDragPayload(First, ItemDragSource.Inventory, 0, null);
        Send(r.Deck, UiEventType.DropReleased, r.Deck.Geometry.SidePack(1), payload);
        Assert.Equal([$"put {First:X} B1 0"], r.Actions.Calls);
    }

    [Fact]
    public void ADropOnAnEquipmentSlot_WieldsWithItsMask()
    {
        Rig r = Make();
        var t = r.Deck.Bindings.Objects();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xE1, Name = "Boots", ValidLocations = EquipMask.FootWear, Type = ItemType.Armor });
        Assert.True(t.MoveItem(0xE1, Player, 30));
        r.Deck.Refresh();
        var payload = new ItemDragPayload(0xE1, ItemDragSource.Inventory, 30, null);
        Send(r.Deck, UiEventType.DropReleased, r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.FootWear)), payload);
        Assert.Equal(["wield E1 FootWear"], r.Actions.Calls);
    }

    [Fact]
    public void AShortcutBarPayload_DoesNothing()
    {
        Rig r = Make();
        var payload = new ItemDragPayload(First, ItemDragSource.ShortcutBar, 0, null);
        Send(r.Deck, UiEventType.DropReleased, r.Deck.Geometry.SidePack(1), payload);
        Send(r.Deck, UiEventType.DropReleased, r.Deck.Geometry.GridCell(3, 0)!.Value, payload);
        Assert.Empty(r.Actions.Calls);
    }

    [Fact]
    public void DragEnter_Highlights_AndLeaveAndDropClear()
    {
        Rig r = Make();
        var payload = new ItemDragPayload(First, ItemDragSource.Inventory, 0, null);
        Send(r.Deck, UiEventType.DragEnter, r.Deck.Geometry.SidePack(1), payload);
        Assert.Equal(new GearDeckHit(GearZone.SidePack, 1), r.Deck.DropHighlight?.Hit);
        Send(r.Deck, UiEventType.DragOver, r.Deck.Geometry.SidePack(1), payload);
        Assert.Null(r.Deck.DropHighlight);
        Send(r.Deck, UiEventType.DragEnter, r.Deck.Geometry.SidePack(1), payload);
        Send(r.Deck, UiEventType.DropReleased, r.Deck.Geometry.SidePack(1), payload);
        Assert.Null(r.Deck.DropHighlight);
    }

    [Fact]
    public void SixWheelNotchesDown_ClampAtTheLastRow()
    {
        Rig r = Make(height: GearDeckGeometry.MinHeightFor(new(false, false, 2, 60, 7, 0)), loose: 60);
        Assert.Equal(7, r.Deck.Geometry.VisibleGridRows);
        for (int i = 0; i < 6; i++)
            Send(r.Deck, UiEventType.Scroll, r.Deck.Geometry.Contents, data0: -1);
        Assert.Equal(10 - 7, r.Deck.MaxScrollRow);
        Assert.Equal(r.Deck.MaxScrollRow, r.Deck.ScrollRow);
        Send(r.Deck, UiEventType.Scroll, r.Deck.Geometry.Contents, data0: 3);
        Assert.Equal(0, r.Deck.ScrollRow);
    }

    [Fact]
    public void TheCloseStud_CallsClose()
    {
        Rig r = Make();
        Click(r.Deck, r.Deck.Geometry.Close);
        Assert.Equal(1, r.Closed);
    }

    [Fact]
    public void DragSource_ItemsAndWornOnly_WithPayloadAndGhost()
    {
        Rig r = Make();
        Send(r.Deck, UiEventType.MouseDown, r.Deck.Geometry.GridCell(2, 0)!.Value);
        Assert.True(r.Deck.IsDragSource);
        Assert.Equal(new ItemDragPayload(First + 2, ItemDragSource.Inventory, 2, null), r.Deck.GetDragPayload());
        Assert.Equal((77u, 32, 32), r.Deck.GetDragGhost());

        int head = SlotIndex(EquipMask.HeadWear);
        Send(r.Deck, UiEventType.MouseDown, r.Deck.Geometry.EquipSlot(head));
        Assert.True(r.Deck.IsDragSource);
        Assert.Equal(new ItemDragPayload(Helm, ItemDragSource.Equipment, head, null), r.Deck.GetDragPayload());

        Send(r.Deck, UiEventType.MouseDown, r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.FootWear)));
        Assert.False(r.Deck.IsDragSource);
        Assert.Null(r.Deck.GetDragPayload());
    }

    [Fact]
    public void EmptyFrameSpace_LeavesThePressToTheWindowDrag()
    {
        Rig r = Make();
        DeckRect header = r.Deck.Geometry.EquipHeader;
        Assert.True(r.Deck.HitTestLocal(header.X + 100f, header.Y + header.H / 2f));
        Assert.False(r.Deck.CapturesPointerDrag);
        Assert.False(r.Deck.HandlesClick);
        Assert.False(r.Deck.IsDragSource);

        var (x, y) = Centre(r.Deck.Geometry.ListStud);
        r.Deck.HitTestLocal(x, y);
        Assert.True(r.Deck.CapturesPointerDrag);
    }

    [Fact]
    public void HoveringAnItem_ShowsItsCaptionAfterTheDwell_AndAnEmptySlotItsLabel()
    {
        Rig r = Make();
        Send(r.Deck, UiEventType.MouseMove, r.Deck.Geometry.GridCell(1, 0)!.Value);
        Assert.Null(r.Deck.VisibleTooltip);
        r.Now += UiCommandDeck.TooltipDwell;
        Assert.Equal("Item 1", r.Deck.VisibleTooltip?.Title);

        Send(r.Deck, UiEventType.MouseMove, r.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.FootWear)));
        Assert.Null(r.Deck.VisibleTooltip);
        r.Now += UiCommandDeck.TooltipDwell;
        Assert.Equal("Feet", r.Deck.VisibleTooltip?.Title);

        Send(r.Deck, UiEventType.HoverLeave, r.Deck.Geometry.Frame);
        Assert.Null(r.Deck.VisibleTooltip);
    }

    [Fact]
    public void Unfolding_GrowsTheDeckToItsNewMinimum()
    {
        float folded = GearDeckGeometry.MinHeightFor(new(true, false, 2, 30, 7, 0));
        float expanded = GearDeckGeometry.MinHeightFor(new(false, false, 2, 30, 7, 0));
        Rig r = Make(s => s with { Folded = true }, height: folded);
        Assert.Equal(folded, r.Deck.Height);
        Assert.Equal(folded, r.Deck.MinHeight);
        Click(r.Deck, r.Deck.Geometry.FoldStud);
        Assert.Equal(expanded, r.Deck.Height);
        Assert.Equal(expanded, r.Deck.MinHeight);
        Assert.Equal(expanded, r.Deck.Geometry.Frame.H);
        Assert.Equal(8, r.Deck.Geometry.VisibleSidePacks);
    }

    [Fact]
    public void Unfolding_GrowsNoTallerThanTheParent_AndKeepsTheDeckInsideIt()
    {
        var screen = new UiPanel { Width = 800, Height = 520 };
        Rig r = Make(s => s with { Folded = true }, height: 470f, parent: screen);
        r.Deck.Top = 40f;
        Click(r.Deck, r.Deck.Geometry.FoldStud);
        Assert.Equal(520f, r.Deck.Height);
        Assert.Equal(0f, r.Deck.Top);

        var tall = new UiPanel { Width = 800, Height = 700 };
        Rig t = Make(s => s with { Folded = true }, height: 470f, parent: tall);
        t.Deck.Top = 300f;
        Click(t.Deck, t.Deck.Geometry.FoldStud);
        float expanded = GearDeckGeometry.MinHeightFor(t.Deck.Geometry.Input);
        Assert.Equal(expanded, t.Deck.Height);
        Assert.Equal(700f - expanded, t.Deck.Top);
    }

    [Fact]
    public void ADropOnEmptyGridSpace_PutsAfterTheLastItem()
    {
        Rig r = Make();
        var t = r.Deck.Bindings.Objects();
        Place(t, new ClientObject { ObjectId = 0xD1, Name = "Gem", Type = ItemType.Gem }, Bag, 0);
        r.Deck.Refresh();
        Assert.Equal(30, r.Deck.Items.Count);
        DeckRect empty = r.Deck.Geometry.GridCell(31, 0)!.Value;   // past the last item
        Send(r.Deck, UiEventType.DropReleased, empty, new ItemDragPayload(0xD1, ItemDragSource.Inventory, 0, null));
        Assert.Equal([$"put D1 {Player:X} 30"], r.Actions.Calls);
    }

    [Fact]
    public void ADropOnEmptyGridSpace_OfAnItemInThisPack_MovesItToTheEnd()
    {
        Rig r = Make();
        DeckRect empty = r.Deck.Geometry.GridCell(31, 0)!.Value;
        Send(r.Deck, UiEventType.DropReleased, empty, new ItemDragPayload(First, ItemDragSource.Inventory, 0, null));
        Assert.Equal([$"put {First:X} {Player:X} 29"], r.Actions.Calls);   // its old slot closes up first
    }

    [Fact]
    public void ABagDroppedOnASidePacksEmptySpace_GoesToTheMainPack()
    {
        Rig r = Make(s => s with { SelectedPack = Bag });
        var t = r.Deck.Bindings.Objects();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xB2, Type = ItemType.Container, ItemsCapacity = 24, Name = "Sack" });
        r.Deck.Refresh();
        Assert.Equal(Bag, r.Deck.SelectedPackId);
        DeckRect empty = r.Deck.Geometry.GridCell(2, 0)!.Value;
        Send(r.Deck, UiEventType.DropReleased, empty, new ItemDragPayload(0xB2, ItemDragSource.Ground, 0, null));
        Assert.Single(r.Actions.Calls);
        Assert.StartsWith($"put B2 {Player:X} ", r.Actions.Calls[0]);
    }

    [Fact]
    public void DoubleClickingSidePack1_OpensItOnce_AndClickingItAgainKeepsTheScroll()
    {
        Rig r = Make();
        var t = r.Deck.Bindings.Objects();
        for (int i = 0; i < 60; i++)   // more rows than show, so it can scroll
            Place(t, new ClientObject { ObjectId = 0xD00 + (uint)i, Name = $"Gem {i}", Type = ItemType.Gem }, Bag, i);
        r.Deck.Refresh();
        DoubleClick(r.Deck, r.Deck.Geometry.SidePack(1));
        Assert.Equal(Bag, r.Deck.SelectedPackId);
        Assert.Equal(["open B1"], r.Actions.Calls);

        Send(r.Deck, UiEventType.Scroll, r.Deck.Geometry.Contents, data0: -1);
        Assert.Equal(1, r.Deck.ScrollRow);
        Click(r.Deck, r.Deck.Geometry.SidePack(1));
        Assert.Equal(1, r.Deck.ScrollRow);
        Assert.Equal(["open B1"], r.Actions.Calls);
    }
}
