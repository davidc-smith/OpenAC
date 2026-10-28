using AcDream.Core.Items;
using AcDream.Core.Targeting;
using Xunit;

namespace AcDream.Core.Tests.Targeting;

public sealed class TargetKindClassifierTests
{
    private const uint Me = 0x50000001u;

    private static ClientObject Obj(uint id, ItemType type, PublicWeenieFlags flags = PublicWeenieFlags.None,
        uint petOwner = 0, uint hookType = 0, uint hookItemTypes = 0) =>
        new() { ObjectId = id, Type = type, PublicWeenieBitfield = (uint)flags, PetOwnerId = petOwner,
                HookType = hookType, HookItemTypes = hookItemTypes };

    public static TheoryData<ClientObject, TargetKind, WorldObjectKind> Cases => new()
    {
        { Obj(Me, ItemType.Creature, PublicWeenieFlags.Player), TargetKind.Self, WorldObjectKind.None },
        { Obj(2, ItemType.Creature, PublicWeenieFlags.Player | PublicWeenieFlags.PlayerKiller | PublicWeenieFlags.Attackable), TargetKind.Player, WorldObjectKind.None },
        { Obj(3, ItemType.Creature, PublicWeenieFlags.Attackable, petOwner: Me), TargetKind.Summon, WorldObjectKind.None },
        { Obj(4, ItemType.Creature, PublicWeenieFlags.None, petOwner: 0x50000009u), TargetKind.Summon, WorldObjectKind.None },
        { Obj(5, ItemType.Creature, PublicWeenieFlags.Attackable), TargetKind.Monster, WorldObjectKind.None },
        { Obj(6, ItemType.Creature, PublicWeenieFlags.Vendor), TargetKind.Npc, WorldObjectKind.None },
        { Obj(7, ItemType.Container, PublicWeenieFlags.Corpse | PublicWeenieFlags.Openable), TargetKind.Corpse, WorldObjectKind.None },
        { Obj(8, ItemType.Portal, PublicWeenieFlags.Portal), TargetKind.Portal, WorldObjectKind.None },
        { Obj(9, ItemType.Misc, PublicWeenieFlags.Door), TargetKind.WorldObject, WorldObjectKind.Door },
        { Obj(10, ItemType.LifeStone, PublicWeenieFlags.Lifestone), TargetKind.WorldObject, WorldObjectKind.LifeStone },
        { Obj(11, ItemType.Misc, PublicWeenieFlags.Bindstone), TargetKind.WorldObject, WorldObjectKind.BindStone },
        { Obj(12, ItemType.Misc, PublicWeenieFlags.Stuck, hookType: 1, hookItemTypes: 0x2), TargetKind.WorldObject, WorldObjectKind.Hook },
        { Obj(13, ItemType.Misc, PublicWeenieFlags.Stuck), TargetKind.WorldObject, WorldObjectKind.Fixture },
        { Obj(14, ItemType.Container, PublicWeenieFlags.Openable), TargetKind.Container, WorldObjectKind.None },
        { Obj(15, ItemType.MeleeWeapon), TargetKind.Item, WorldObjectKind.None },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ClassifiesEachKind(ClientObject obj, TargetKind kind, WorldObjectKind sub)
    {
        TargetClassification c = TargetKindClassifier.Classify(obj, Me);
        Assert.Equal(kind, c.Kind);
        Assert.Equal(sub, c.WorldObject);
    }

    [Fact]
    public void NullIsNone() => Assert.Equal(TargetKind.None, TargetKindClassifier.Classify(null, Me).Kind);

    [Fact]
    public void YourOwnSummonIsMarkedOwn() =>
        Assert.True(TargetKindClassifier.Classify(Obj(3, ItemType.Creature, petOwner: Me), Me).OwnedByPlayer);
}
