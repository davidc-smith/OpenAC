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
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) { Calls.Add($"put {item:X} {container:X} {placement}"); return true; }
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

        public World()
        {
            var t = new ClientObjectTable();
            t.AddOrUpdate(new ClientObject { ObjectId = Player, ItemsCapacity = 102, ContainersCapacity = 7 });
            t.AddOrUpdate(new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24 });
            t.MoveItem(Bag, Player, 0);
            t.AddOrUpdate(new ClientObject { ObjectId = Sword, Name = "Sword", Type = ItemType.MeleeWeapon, ValidLocations = EquipMask.MeleeWeapon });
            t.MoveItem(Sword, Player, 0);
            t.AddOrUpdate(new ClientObject { ObjectId = Helm, WielderId = Player, Name = "Helm", ValidLocations = EquipMask.HeadWear });
            t.MoveItem(Helm, Player, -1, EquipMask.HeadWear);
            var session = Session = new GearDeckSession(null, () => "Davian", () => true, () => 0.0);
            Deck = new UiGearDeck(
                new GearDeckBindings(() => t, () => Player, () => 100, (_, _, _, _, _) => 0u, (_, _, _, _, _) => 0u,
                    o => o.Name, session, Actions, _ => false, () => { }),
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
}
