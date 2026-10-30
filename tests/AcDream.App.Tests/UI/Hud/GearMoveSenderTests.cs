using AcDream.App.UI.Hud;
using AcDream.Core.Items;
using AcDream.Core.Items.Gear;
using Xunit;

namespace AcDream.App.Tests.UI.Hud;

public sealed class GearMoveSenderTests
{
    private const uint Player = 0x50000001, A = 0xA1, B = 0xA2, FullPack = 0xB1, Filler = 0xC1;

    private sealed class Fake : IGearItemActions
    {
        public readonly List<string> Calls = [];
        public bool Ready() => true;
        public void Report(string text) => Calls.Add("report");
        public bool PutInContainer(uint item, uint container, int placement, bool fromGround) { Calls.Add($"put {item:X} {(container == Player ? "Player" : container.ToString("X"))} {placement}"); return true; }
        public bool Merge(uint from, uint to) { Calls.Add($"merge {from:X} {to:X}"); return true; }
        public bool WieldFromPaperdoll(uint item, EquipMask mask) => true;
        public bool WieldSecondary(uint item) => true;
        public bool Activate(uint item) => true;
        public bool OpenPack(uint pack) => true;
        public bool Split(uint stack, uint container, int placement, int amount) { Calls.Add($"split {stack:X} {(container == Player ? "Player" : container.ToString("X"))} {placement} {amount}"); return true; }
    }

    private static (GearMoveSender s, Fake f) Rig()
    {
        var t = new ClientObjectTable();
        t.AddOrUpdate(new ClientObject { ObjectId = Player, ItemsCapacity = 102, ContainersCapacity = 7 });
        void Place(ClientObject o, uint c, int slot) { t.AddOrUpdate(o); Assert.True(t.MoveItem(o.ObjectId, c, slot)); }
        Place(new ClientObject { ObjectId = FullPack, Type = ItemType.Container, ItemsCapacity = 1 }, Player, 0);
        Place(new ClientObject { ObjectId = A, Type = ItemType.MeleeWeapon }, Player, 0);
        Place(new ClientObject { ObjectId = B, Type = ItemType.MeleeWeapon }, Player, 1);
        Place(new ClientObject { ObjectId = Filler, Type = ItemType.MeleeWeapon }, FullPack, 0);
        var f = new Fake();
        return (new GearMoveSender(t, () => Player, f), f);
    }

    [Fact]
    public void PutWhereItAlreadySits_IsSkipped()
    {
        var (s, f) = Rig();
        int slot = 0;
        Assert.Equal(GearMoveResult.Skipped, s.Send(GearMove.Put(A, Player, slot)));
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void PutToTheEnd_ResolvesToTheContainedCount()
    {
        var (s, f) = Rig();
        Assert.Equal(GearMoveResult.Sent, s.Send(GearMove.Put(A, Player, -1)));
        Assert.Single(f.Calls);
        Assert.StartsWith("put A1 Player ", f.Calls[0]);
    }

    [Fact]
    public void PutIntoAFullPack_IsRefusedWithOneReport()
    {
        var (s, f) = Rig();
        Assert.Equal(GearMoveResult.Refused, s.Send(GearMove.Put(A, FullPack, 0)));
        Assert.Equal(["report"], f.Calls);
    }

    [Fact]
    public void MergeWithMissingSource_IsSkipped()
    {
        var (s, f) = Rig();
        Assert.Equal(GearMoveResult.Skipped, s.Send(GearMove.Merge(0xDEAD, A, 1)));
        Assert.Empty(f.Calls);
    }

    [Fact]
    public void Split_GoesToTheAction()
    {
        var (s, f) = Rig();
        Assert.Equal(GearMoveResult.Sent, s.Send(GearMove.Split(A, 3, Player, 1)));
        Assert.Equal(["split A1 Player 1 3"], f.Calls);
    }
}
