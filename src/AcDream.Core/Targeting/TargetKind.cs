namespace AcDream.Core.Targeting;

/// <summary>What the selected object is, for the Target Deck.</summary>
public enum TargetKind { None, Monster, Summon, Player, Npc, Item, Corpse, Container, Portal, WorldObject, Self }

/// <summary>Which fixture a <see cref="TargetKind.WorldObject"/> is.</summary>
public enum WorldObjectKind { None, Door, LifeStone, BindStone, Hook, Fixture }

/// <param name="OwnedByPlayer">True for the player and for the player's own summon.</param>
public readonly record struct TargetClassification(TargetKind Kind, WorldObjectKind WorldObject, bool OwnedByPlayer);
