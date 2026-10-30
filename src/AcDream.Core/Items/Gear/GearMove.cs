namespace AcDream.Core.Items.Gear;

public enum GearMoveKind { Merge, Split, Put }

/// <summary>
/// One organising move. Merge: <see cref="Item"/> into stack <see cref="Target"/>, <see cref="Amount"/> of it.
/// Split: <see cref="Amount"/> off stack <see cref="Item"/> into container <see cref="Target"/> at <see cref="Slot"/>.
/// Put: <see cref="Item"/> into container <see cref="Target"/> at <see cref="Slot"/>; -1 is the end of the pack.
/// </summary>
public sealed record GearMove(GearMoveKind Kind, uint Item, uint Target, int Amount, int Slot)
{
    public static GearMove Merge(uint from, uint to, int amount) => new(GearMoveKind.Merge, from, to, amount, -1);
    public static GearMove Split(uint stack, int amount, uint container, int slot) => new(GearMoveKind.Split, stack, container, amount, slot);
    public static GearMove Put(uint item, uint container, int slot) => new(GearMoveKind.Put, item, container, 0, slot);
}
