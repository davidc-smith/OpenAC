using AcDream.Core.Items;

namespace AcDream.Core.Targeting;

public static class TargetKindClassifier
{
    public static TargetClassification Classify(ClientObject? obj, uint playerId)
    {
        if (obj is null)
            return default;
        if (obj.ObjectId == playerId)
            return new(TargetKind.Self, WorldObjectKind.None, OwnedByPlayer: true);

        PublicWeenieFlags flags = PublicWeenieFlagsExtensions.FlagsOf(obj);
        bool creature = (obj.Type & ItemType.Creature) != 0;

        if ((flags & PublicWeenieFlags.Player) != 0)
            return new(TargetKind.Player, WorldObjectKind.None, false);
        if (obj.PetOwnerId != 0)
            return new(TargetKind.Summon, WorldObjectKind.None, obj.PetOwnerId == playerId);
        if (creature)
            return new((flags & PublicWeenieFlags.Attackable) != 0 ? TargetKind.Monster : TargetKind.Npc,
                WorldObjectKind.None, false);
        if ((flags & PublicWeenieFlags.Corpse) != 0)
            return new(TargetKind.Corpse, WorldObjectKind.None, false);
        if ((flags & PublicWeenieFlags.Portal) != 0 || (obj.Type & ItemType.Portal) != 0)
            return new(TargetKind.Portal, WorldObjectKind.None, false);

        // Levers and switches are stuck in place; a stuck container stays a Container.
        WorldObjectKind fixture =
            (flags & PublicWeenieFlags.Door) != 0 ? WorldObjectKind.Door
            : (flags & PublicWeenieFlags.Lifestone) != 0 || (obj.Type & ItemType.LifeStone) != 0 ? WorldObjectKind.LifeStone
            : (flags & PublicWeenieFlags.Bindstone) != 0 ? WorldObjectKind.BindStone
            : obj.IsHook ? WorldObjectKind.Hook
            : (obj.Type & ItemType.Container) == 0 && (flags & PublicWeenieFlags.Stuck) != 0 ? WorldObjectKind.Fixture
            : WorldObjectKind.None;
        if (fixture != WorldObjectKind.None)
            return new(TargetKind.WorldObject, fixture, false);

        if ((obj.Type & ItemType.Container) != 0)
            return new(TargetKind.Container, WorldObjectKind.None, false);
        return new(TargetKind.Item, WorldObjectKind.None, false);
    }
}
