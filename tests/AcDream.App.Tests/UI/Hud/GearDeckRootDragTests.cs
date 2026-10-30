using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

/// <summary>Drags driven through the real <see cref="UiRoot"/> pointer path, the way the
/// client delivers them, not by sending events to the deck directly.</summary>
public sealed class GearDeckRootDragTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Sword = 0xA1, Helm = 0xC1;

    private sealed class Recorder : IGearItemActions
    {
        public readonly List<string> Calls = [];
        public bool Ready() => true;
        public void Report(string text) => Calls.Add($"report {text}");
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround)
        {
            Calls.Add($"put {item:X} {container:X} {placement}");
            if (fromGround) Calls.Add($"pickup {item:X}");
            return true;
        }
        public bool Merge(uint from, uint to) { Calls.Add($"merge {from:X} {to:X}"); return true; }
        public bool WieldFromPaperdoll(uint item, EquipMask mask) { Calls.Add($"wield {item:X} {mask}"); return true; }
        public bool WieldSecondary(uint item) { Calls.Add($"secondary {item:X}"); return true; }
        public bool Activate(uint item) { Calls.Add($"use {item:X}"); return true; }
        public bool OpenPack(uint pack) { Calls.Add($"open {pack:X}"); return true; }
    }

    private sealed class World
    {
        public readonly UiRoot Root = new() { Width = 1600, Height = 900 };
        public readonly Recorder Actions = new();
        public readonly UiGearDeck Deck;
        public readonly GearDeckSession Session;
        public readonly List<string> Log = [];
        public readonly ClientObjectTable Objects;

        public World()
        {
            var t = Objects = new ClientObjectTable();
            t.AddOrUpdate(new ClientObject { ObjectId = Player, ItemsCapacity = 102, ContainersCapacity = 7 });
            t.AddOrUpdate(new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24 });
            t.MoveItem(Bag, Player, 0);
            t.AddOrUpdate(new ClientObject { ObjectId = 0xB2, Type = ItemType.Container, ItemsCapacity = 24 });
            t.MoveItem(0xB2, Player, 1);
            t.AddOrUpdate(new ClientObject { ObjectId = 0xB3, Type = ItemType.Container, ItemsCapacity = 24 });
            t.MoveItem(0xB3, Player, 2);
            t.AddOrUpdate(new ClientObject { ObjectId = Sword, Name = "Sword", Type = ItemType.MeleeWeapon, ValidLocations = EquipMask.MeleeWeapon });
            t.MoveItem(Sword, Player, 0);
            t.AddOrUpdate(new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", ValidLocations = EquipMask.HeadWear });
            t.MoveItem(Helm, Player, -1, EquipMask.HeadWear);
            var session = Session = new GearDeckSession(null, () => "Davian", () => true, () => 0.0);
            Deck = new UiGearDeck(
                new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
                    o => o.Name, session, Actions, _ => false, () => { }, Log: Log.Add),
                fonts: null, icons: null, clock: () => 0.0);
            RetailWindowFrame.Mount(Root, Deck, _ => (0u, 0, 0), UiGearDeck.MountOptions(100, 50));
            Deck.Visible = true;
            Root.Tick(0.016, 0);
        }

        public (int X, int Y) Screen(DeckRect r)
        {
            var o = Deck.ScreenPosition;
            return ((int)(o.X + r.X + r.W / 2), (int)(o.Y + r.Y + r.H / 2));
        }
    }

    private static int SlotIndex(EquipMask mask)
    {
        for (int i = 0; i < GearSlotCatalog.Slots.Count; i++)
            if (GearSlotCatalog.Slots[i].Mask == mask) return i;
        throw new InvalidOperationException();
    }

    /// <summary>Press at one rect, move past the drag threshold, and release at another, as the client does.</summary>
    private static void Drag(World w, DeckRect from, (int X, int Y) to)
    {
        var (sx, sy) = w.Screen(from);
        w.Root.OnMouseMove(sx, sy);
        w.Root.OnMouseDown(UiMouseButton.Left, sx, sy);
        w.Root.OnMouseMove(sx + 10, sy + 10);
        Assert.NotNull(w.Root.DragSource);
        w.Root.OnMouseMove(to.X, to.Y);
        w.Root.OnMouseUp(UiMouseButton.Left, to.X, to.Y);
        Assert.Null(w.Root.DragSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragItemOntoSidePack_ThroughTheRoot_PutsItThere(bool uiLocked)
    {
        var w = new World();
        w.Root.UiLocked = uiLocked;
        Drag(w, w.Deck.Geometry.GridCell(0, 0)!.Value, w.Screen(w.Deck.Geometry.SidePack(1)));
        Assert.Contains($"put {Sword:X} {Bag:X} 0", w.Actions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragWornSlotOntoSidePack_ThroughTheRoot_PutsItThere(bool uiLocked)
    {
        var w = new World();
        w.Root.UiLocked = uiLocked;
        Drag(w, w.Deck.Geometry.EquipSlot(SlotIndex(EquipMask.HeadWear)), w.Screen(w.Deck.Geometry.SidePack(1)));
        Assert.Contains($"put {Helm:X} {Bag:X} 0", w.Actions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragPackItemOntoEquipmentSlot_ThroughTheRoot_WieldsIt(bool uiLocked)
    {
        var w = new World();
        w.Root.UiLocked = uiLocked;
        Drag(w, w.Deck.Geometry.GridCell(0, 0)!.Value, w.Screen(w.Deck.Geometry.EquipSlot(SlotIndex(GearSlotCatalog.Weapon))));
        Assert.Contains($"wield {Sword:X} {EquipMask.MeleeWeapon}", w.Actions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StudClick_ThroughTheRoot_StillActs(bool uiLocked)
    {
        var w = new World();
        w.Root.UiLocked = uiLocked;
        float left = w.Deck.ScreenPosition.X;
        var (x, y) = w.Screen(w.Deck.Geometry.ListStud);
        w.Root.OnMouseMove(x, y);
        w.Root.OnMouseDown(UiMouseButton.Left, x, y);
        w.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.Equal(GearPackView.List, w.Session.Settings.View);
        Assert.Null(w.Root.DragSource);
        Assert.Equal(left, w.Deck.ScreenPosition.X);
    }

    /// <summary>Live bug: with the Modern HUD on, dragging an item lying on the ground into the
    /// deck did nothing (the world lifted only the player). The world lift of a loose item
    /// carries a ground payload, and the deck's drop sends the pickup.</summary>
    [Fact]
    public void GroundItemDraggedFromTheWorldOntoASidePack_PicksItUp()
    {
        const uint Loot = 0x7000_0D01u;
        var w = new World();
        w.Objects.AddOrUpdate(new ClientObject { ObjectId = Loot, Name = "Loot", Type = ItemType.Misc, IconId = 0x0600_1234u });
        w.Root.WorldDragRequested = (_, _) => WorldDrag.Build(
            Loot, Player, outOfCombat: true, (_, _, _, _, _) => 0u, null,
            groundItem: id => w.Objects.Get(id));
        int wx = (int)w.Root.Width - 20, wy = (int)w.Root.Height - 20;

        w.Root.OnMouseMove(wx, wy);
        w.Root.OnMouseDown(UiMouseButton.Left, wx, wy);
        w.Root.OnMouseMove(wx - 30, wy - 30);
        Assert.Same(w.Root, w.Root.DragSource);   // a world lift, not a drag off the deck
        var to = w.Screen(w.Deck.Geometry.SidePack(1));
        w.Root.OnMouseMove(to.X, to.Y);
        w.Root.OnMouseUp(UiMouseButton.Left, to.X, to.Y);

        Assert.Contains($"put {Loot:X} {Bag:X} 0", w.Actions.Calls);
        Assert.Contains($"pickup {Loot:X}", w.Actions.Calls);
    }

    [Fact]
    public void PressOnAStudMovedAway_UnlockedDoesNotDragTheWindow()
    {
        var w = new World();
        float left = w.Deck.ScreenPosition.X;
        var (x, y) = w.Screen(w.Deck.Geometry.ListStud);
        w.Root.OnMouseMove(x, y);
        w.Root.OnMouseDown(UiMouseButton.Left, x, y);
        w.Root.OnMouseMove(x + 40, y + 40);
        w.Root.OnMouseUp(UiMouseButton.Left, x + 40, y + 40);
        Assert.Equal(left, w.Deck.ScreenPosition.X);
        Assert.Null(w.Root.DragSource);
    }

    [Fact]
    public void PressOnEmptyContentsSpace_ThroughTheRoot_ClearsTheSelectionAndDragsTheWindow()
    {
        var w = new World();
        var (ix, iy) = w.Screen(w.Deck.Geometry.GridCell(0, 0)!.Value);
        w.Root.OnMouseMove(ix, iy);
        w.Root.OnMouseDown(UiMouseButton.Left, ix, iy);
        w.Root.OnMouseUp(UiMouseButton.Left, ix, iy);
        Assert.Equal(new[] { Sword }, w.Deck.Selection);

        float left = w.Deck.ScreenPosition.X;
        DeckRect empty = w.Deck.Geometry.GridCell(5, 0)!.Value;   // the main pack holds one item
        Assert.Equal(GearZone.Frame, w.Deck.Geometry.HitAt(empty.X + empty.W / 2, empty.Y + empty.H / 2).Zone);
        var (x, y) = w.Screen(empty);
        w.Root.OnMouseMove(x + 30, y + 30);   // away, so the press is no double-click on the item
        w.Root.OnMouseMove(x, y);
        w.Root.OnMouseDown(UiMouseButton.Left, x, y);
        w.Root.OnMouseMove(x + 40, y + 20);
        w.Root.OnMouseUp(UiMouseButton.Left, x + 40, y + 20);
        Assert.Empty(w.Deck.Selection);
        Assert.Equal(left + 40, w.Deck.ScreenPosition.X);
        Assert.Null(w.Root.DragSource);
    }

    [Fact]
    public void PressOnEmptyFrameSpace_UnlockedDragsTheWindow()
    {
        var w = new World();
        float left = w.Deck.ScreenPosition.X;
        DeckRect header = w.Deck.Geometry.EquipHeader;
        var o = w.Deck.ScreenPosition;
        int x = (int)(o.X + header.X + 100), y = (int)(o.Y + header.Y + header.H / 2);
        w.Root.OnMouseMove(x, y);
        w.Root.OnMouseDown(UiMouseButton.Left, x, y);
        w.Root.OnMouseMove(x + 40, y + 20);
        w.Root.OnMouseUp(UiMouseButton.Left, x + 40, y + 20);
        Assert.Equal(left + 40, w.Deck.ScreenPosition.X);
        Assert.Null(w.Root.DragSource);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragSidePackOutOfTheDeck_ReachesTheWorldDrop(bool uiLocked)
    {
        var w = new World();
        w.Root.UiLocked = uiLocked;
        object? dropped = null;
        w.Root.DragReleasedOutsideUi += (payload, _, _) => dropped = payload;
        Drag(w, w.Deck.Geometry.SidePack(1), (1500, 800));
        Assert.Equal(new ItemDragPayload(Bag, ItemDragSource.Inventory, 1, null), dropped);
        Assert.Empty(w.Actions.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragSidePackOntoTheGrid_PutsItInTheMainPack(bool uiLocked)
    {
        var w = new World();
        w.Root.UiLocked = uiLocked;
        Drag(w, w.Deck.Geometry.SidePack(1), w.Screen(w.Deck.Geometry.GridCell(0, 0)!.Value));
        Assert.Contains(w.Actions.Calls, c => c.StartsWith($"put {Bag:X} {Player:X} "));
    }

    [Fact]
    public void TheMainPack_IsNotADragSource()
    {
        var w = new World();
        var (x, y) = w.Screen(w.Deck.Geometry.SidePack(0));
        w.Root.OnMouseMove(x, y);
        w.Root.OnMouseDown(UiMouseButton.Left, x, y);
        w.Root.OnMouseMove(x + 10, y + 10);
        Assert.Null(w.Root.DragSource);
        w.Root.OnMouseUp(UiMouseButton.Left, x + 10, y + 10);
    }

    [Fact]
    public void DragSidePack3OntoSidePack1_ReordersItToPack1sPlace()
    {
        var w = new World();
        Drag(w, w.Deck.Geometry.SidePack(3), w.Screen(w.Deck.Geometry.SidePack(1)));
        Assert.Equal([$"put {0xB3:X} {Player:X} 0"], w.Actions.Calls);
    }

    [Fact]
    public void DragSidePackBackOntoItself_SendsNothing()
    {
        var w = new World();
        DeckRect pack = w.Deck.Geometry.SidePack(1);
        var (x, y) = w.Screen(pack);
        Drag(w, pack, (x + 2, y + 2));
        Assert.Empty(w.Actions.Calls);
    }

    [Fact]
    public void ADragAndDrop_LogsOneDragLine_AndOneDropLine()
    {
        var w = new World();
        Drag(w, w.Deck.Geometry.GridCell(0, 0)!.Value, w.Screen(w.Deck.Geometry.SidePack(1)));
        Assert.Equal(
        [
            "[gear-deck] drag 0x000000A1 from Item 0",
            "[gear-deck] drop 0x000000A1 on SidePack 1 -> DropOnPack put 0x000000A1 0x000000B1 0 ok",
        ], w.Log);
    }

    [Fact]
    public void ARefusedDrop_LogsWhatStoppedIt()
    {
        var w = new World();
        DeckRect pack = w.Deck.Geometry.SidePack(1);
        var (x, y) = w.Screen(pack);
        Drag(w, pack, (x + 2, y + 2));
        Assert.Equal("[gear-deck] drop 0x000000B1 on SidePack 1 -> DropOnPack nothing sent", w.Log[^1]);
    }

    [Fact]
    public void AStudClick_LogsNothing()
    {
        var w = new World();
        var (x, y) = w.Screen(w.Deck.Geometry.ListStud);
        w.Root.OnMouseMove(x, y);
        w.Root.OnMouseDown(UiMouseButton.Left, x, y);
        w.Root.OnMouseUp(UiMouseButton.Left, x, y);
        Assert.Empty(w.Log);
    }
}
