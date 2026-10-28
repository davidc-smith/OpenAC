using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearDropDispatcherTests
{
    private const uint Player = 0x50000001, Bag = 0xB1, Sword = 0xA1;

    private sealed class Fake : IGearItemActions
    {
        public bool IsReady = true;
        public readonly List<string> Calls = [];
        public bool Ready() => IsReady;
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

    private static (ClientObjectTable t, Fake a, GearDropDispatcher d) World()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, ItemsCapacity = 102, ContainersCapacity = 7 });
        Place(t, new ClientObject { ObjectId = Bag, Type = ItemType.Container, ItemsCapacity = 24 }, Player, 0);
        Place(t, new ClientObject { ObjectId = Sword, Type = ItemType.MeleeWeapon, ValidLocations = EquipMask.MeleeWeapon }, Player, 0);
        var a = new Fake();
        return (t, a, new GearDropDispatcher(t, () => Player, a));
    }

    [Fact]
    public void DropOnPack_PutsAtTop()
    {
        var (_, a, d) = World();
        d.DropOnPack(Sword, Bag, fromGround: false);
        Assert.Equal(["put A1 B1 0"], a.Calls);
    }

    [Fact]
    public void DropOnPack_BagGoesToMainPack()
    {
        var (t, a, d) = World();
        Place(t, new ClientObject { ObjectId = 0xB2, Type = ItemType.Container, ItemsCapacity = 24 }, Player, 1);
        d.DropOnPack(0xB2, Bag, fromGround: false);
        Assert.Equal(["put B2 50000001 0"], a.Calls);
    }

    private static (ClientObjectTable t, Fake a, GearDropDispatcher d) ThreeBags()
    {
        var w = World();
        Place(w.t, new ClientObject { ObjectId = 0xB2, Type = ItemType.Container, ItemsCapacity = 24 }, Player, 1);
        Place(w.t, new ClientObject { ObjectId = 0xB3, Type = ItemType.Container, ItemsCapacity = 24 }, Player, 2);
        return w;
    }

    [Fact]
    public void DropOnPack_Pack3OntoPack1_TakesPack1sPlace()
    {
        var (_, a, d) = ThreeBags();
        d.DropOnPack(0xB3, Bag, fromGround: false);
        Assert.Equal(["put B3 50000001 0"], a.Calls);
    }

    [Fact]
    public void DropOnPack_Pack1OntoPack3_TakesPack3sPlace()
    {
        var (_, a, d) = ThreeBags();
        d.DropOnPack(Bag, 0xB3, fromGround: false);
        Assert.Equal(["put B1 50000001 1"], a.Calls);   // its old slot closes up first
    }

    [Fact]
    public void DropOnPack_PackOntoItsOwnCell_SendsNothing()
    {
        var (_, a, d) = ThreeBags();
        d.DropOnPack(0xB2, 0xB2, fromGround: false);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void DropOnPack_PackOntoTheCellAfterItsOwn_SendsNothing()
    {
        var (_, a, d) = ThreeBags();
        d.DropOnPack(Bag, 0xB2, fromGround: false);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void DropOnPack_PackOntoMainPack_GoesToTheTop()
    {
        var (_, a, d) = ThreeBags();
        d.DropOnPack(0xB3, Player, fromGround: false);
        Assert.Equal(["put B3 50000001 0"], a.Calls);
    }

    [Fact]
    public void DropOnPack_FullPack_FallsThroughToMainPack()
    {
        var (t, a, d) = World();
        t.Get(Bag)!.ItemsCapacity = 1;
        Place(t, new ClientObject { ObjectId = 0xE1 }, Bag, 0);
        d.DropOnPack(Sword, Bag, fromGround: false);
        Assert.Equal(["put A1 50000001 0"], a.Calls);
    }

    [Fact]
    public void NotReady_SendsNothing()
    {
        var (_, a, d) = World();
        a.IsReady = false;
        d.DropOnPack(Sword, Bag, false);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void DropOnCell_SameKindStack_Merges()
    {
        var (t, a, d) = World();
        Place(t, new ClientObject { ObjectId = 0xD1, WeenieClassId = 300, StackSize = 5, StackSizeMax = 100 }, Player, 1);
        Place(t, new ClientObject { ObjectId = 0xD2, WeenieClassId = 300, StackSize = 7, StackSizeMax = 100 }, Bag, 0);
        d.DropOnCell(0xD2, Player, targetItemId: 0xD1, targetIndex: 1, fromGround: false);
        Assert.Equal(["merge D2 D1"], a.Calls);
    }

    [Fact]
    public void DropOnCell_OtherKind_PlacesAtCell()
    {
        var (t, a, d) = World();
        Place(t, new ClientObject { ObjectId = 0xD1, WeenieClassId = 300 }, Player, 1);
        Place(t, new ClientObject { ObjectId = 0xD2, WeenieClassId = 301 }, Bag, 0);
        d.DropOnCell(0xD2, Player, targetItemId: 0xD1, targetIndex: 1, fromGround: false);
        Assert.Single(a.Calls);
        Assert.StartsWith("put D2 50000001 ", a.Calls[0]);
    }

    [Fact]
    public void DropOnCell_OntoItself_NoMove()
    {
        var (_, a, d) = World();
        d.DropOnCell(Sword, Player, targetItemId: Sword, targetIndex: 0, fromGround: false);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void DropOnSlot_Wields()
    {
        var (_, a, d) = World();
        d.DropOnSlot(Sword, GearSlotCatalog.Weapon);
        Assert.Single(a.Calls, c => c.StartsWith("wield A1"));
    }

    [Fact]
    public void DropOnSlot_ZeroMask_SendsNothing()
    {
        var (_, a, d) = World();
        d.DropOnSlot(Sword, EquipMask.None);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void NoRoomAnywhere_ReportsOnce_SendsNothing()
    {
        var (t, a, d) = World();
        t.Get(Player)!.ItemsCapacity = 1;      // the sword fills the main pack
        t.Get(Bag)!.ItemsCapacity = 1;
        Place(t, new ClientObject { ObjectId = 0xE1 }, Bag, 0);
        t.AddOrUpdate(new ClientObject { ObjectId = 0xF1, ContainerId = 0 });
        d.DropOnPack(0xF1, Bag, fromGround: true);
        Assert.Single(a.Calls);
        Assert.StartsWith("report", a.Calls[0]);
    }

    [Fact]
    public void TakeOff_MovesToPackWithRoom()
    {
        var (t, a, d) = World();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, WielderId = Player, ValidLocations = EquipMask.HeadWear });
        Assert.True(t.MoveItem(0xC1, Player, -1, EquipMask.HeadWear));
        d.TakeOff(0xC1);
        Assert.Equal(["put C1 50000001 0"], a.Calls);
    }

    [Fact]
    public void DropOnCell_GroundStack_Merges()
    {
        var (t, a, d) = World();
        Place(t, new ClientObject { ObjectId = 0xD1, WeenieClassId = 300, StackSize = 5, StackSizeMax = 100 }, Player, 1);
        t.AddOrUpdate(new ClientObject { ObjectId = 0xD3, WeenieClassId = 300, StackSize = 9, StackSizeMax = 100, ContainerId = 0 });
        d.DropOnCell(0xD3, Player, 0xD1, 1, fromGround: true);
        Assert.Equal(["merge D3 D1"], a.Calls);
    }

    [Fact]
    public void DropOnSlot_OffHandWeapon_WieldsSecondary()
    {
        var (_, a, d) = World();
        d.DropOnSlot(Sword, EquipMask.Shield);
        Assert.Equal(["secondary A1"], a.Calls);
    }

    [Fact]
    public void DropOnSlot_NotReady_SendsNothing()
    {
        var (_, a, d) = World();
        a.IsReady = false;
        d.DropOnSlot(Sword, GearSlotCatalog.Weapon);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void TakeOff_NoRoom_Reports()
    {
        var (t, a, d) = World();
        t.Get(Player)!.ItemsCapacity = 1;
        t.Get(Bag)!.ItemsCapacity = 1;
        Place(t, new ClientObject { ObjectId = 0xE1 }, Bag, 0);
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, WielderId = Player, ValidLocations = EquipMask.HeadWear });
        // not indexed in the pack's contents: a worn item already in the pack would count as fitting
        d.TakeOff(0xC1);
        Assert.Single(a.Calls);
        Assert.StartsWith("report", a.Calls[0]);
    }

    [Fact]
    public void TakeOff_NotReady_SendsNothing()
    {
        var (t, a, d) = World();
        t.AddOrUpdate(new ClientObject { ObjectId = 0xC1, WielderId = Player, ValidLocations = EquipMask.HeadWear });
        Assert.True(t.MoveItem(0xC1, Player, -1, EquipMask.HeadWear));
        a.IsReady = false;
        d.TakeOff(0xC1);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void DropOnCell_NotReady_SendsNothing()
    {
        var (t, a, d) = World();
        Place(t, new ClientObject { ObjectId = 0xD2, WeenieClassId = 301 }, Bag, 0);
        a.IsReady = false;
        d.DropOnCell(0xD2, Player, 0, 0, false);
        Assert.Empty(a.Calls);
    }

    [Fact]
    public void DropOnCell_FullPack_FallsThroughToTopOfRoomyPack()
    {
        var (t, a, d) = World();
        t.Get(Player)!.ItemsCapacity = 1;      // sword fills the main pack
        Place(t, new ClientObject { ObjectId = 0xD2, WeenieClassId = 301 }, Bag, 0);
        d.DropOnCell(0xD2, Player, Sword, 0, false);
        Assert.Equal(["put D2 B1 0"], a.Calls);
    }

    [Fact]
    public void DropOnCell_Bag_GoesToMainPackTop()
    {
        var (t, a, d) = World();
        Place(t, new ClientObject { ObjectId = 0xB2, Type = ItemType.Container, ItemsCapacity = 24 }, Player, 1);
        d.DropOnCell(0xB2, Bag, 0, 0, false);
        Assert.Equal(["put B2 50000001 0"], a.Calls);
    }
}
