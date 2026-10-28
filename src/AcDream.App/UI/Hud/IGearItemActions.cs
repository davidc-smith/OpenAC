using AcDream.Core.Items;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

/// <summary>What the Gear Deck asks of the game: the retail inventory's own request paths.</summary>
internal interface IGearItemActions
{
    bool Ready();
    void Report(string text);
    bool PutInContainer(uint item, uint container, int placement, bool fromGround);
    bool Merge(uint from, uint to);
    bool WieldFromPaperdoll(uint item, EquipMask mask);
    bool WieldSecondary(uint item);
    bool Activate(uint item);
    bool OpenPack(uint pack);
}

internal sealed class RuntimeGearItemActions(
    RuntimeItemInteraction items,
    ClientObjectTable objects,
    Action<uint, uint, int>? sendPutItemInContainer,
    Action<uint, uint, uint>? sendStackableMerge) : IGearItemActions
{
    public bool Ready() => items.EnsureInventoryRequestReady();
    public void Report(string text) => items.ReportClientLocal(text);

    public bool PutInContainer(uint item, uint container, int placement, bool fromGround) =>
        items.TryDispatchPendingBackpackPlacement(
            item, container, placement,
            fromGround ? InventoryRequestKind.Pickup : InventoryRequestKind.PutInContainer,
            () =>
            {
                if (sendPutItemInContainer is null) return false;
                sendPutItemInContainer(item, container, placement);
                return true;
            });

    // Owned stacks go through the runtime's merge. A ground source is not
    // owned, so the runtime refuses it; retail's TryMergeStacks sends it
    // anyway, so the same plan and request are sent here.
    public bool Merge(uint from, uint to)
    {
        if (items.TryMergeItemsForAutomation(from, to)) return true;
        if (sendStackableMerge is null
            || objects.Get(from) is not { } source
            || objects.Get(to) is not { } target)
            return false;
        StackMergePlan? plan = StackMergePlanner.Plan(
            new StackMergeItem(source.ObjectId, source.WeenieClassId, source.StackSize, source.StackSizeMax, source.TradeState),
            new StackMergeItem(target.ObjectId, target.WeenieClassId, target.StackSize, target.StackSizeMax, target.TradeState),
            items.CanMakeInventoryRequest,
            Math.Max(1, source.StackSize));
        if (plan is not { } merge) return false;
        return items.TryDispatchInventoryRequest(
            InventoryRequestKind.Merge,
            merge.SourceObjectId,
            () =>
            {
                sendStackableMerge(merge.SourceObjectId, merge.TargetObjectId, merge.Amount);
                return true;
            });
    }
    public bool WieldFromPaperdoll(uint item, EquipMask mask) => items.WieldFromPaperdoll(item, mask);
    public bool WieldSecondary(uint item) => items.TryWieldItemSecondary(item);
    public bool Activate(uint item) => items.ActivateItem(item);
    public bool OpenPack(uint pack) => items.ActivateItem(pack);
}
