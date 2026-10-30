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

    /// <summary>Splits <paramref name="amount"/> off a stack into a container slot. Not every caller can; false by default.</summary>
    bool Split(uint stack, uint container, int placement, int amount) => false;
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
    public bool Split(uint stack, uint container, int placement, int amount) =>
        items.TrySplitToContainer(stack, container, (uint)Math.Max(0, placement), (uint)Math.Max(0, amount));
}

/// <summary>Forwards to the real actions and keeps a short note of the last one, for the Gear Deck's drop log line.</summary>
internal sealed class LoggedGearItemActions(IGearItemActions inner) : IGearItemActions
{
    /// <summary>What the last call sent or why nothing was sent; the deck clears it before each drop.</summary>
    public string? Last { get; set; }

    private bool Note(bool ok, string call)
    {
        Last = $"{call} {(ok ? "ok" : "failed")}";
        return ok;
    }

    public bool Ready()
    {
        bool ready = inner.Ready();
        if (!ready) Last = "not ready";
        return ready;
    }

    public void Report(string text)
    {
        Last = $"refused: {text}";
        inner.Report(text);
    }

    public bool PutInContainer(uint item, uint container, int placement, bool fromGround) =>
        Note(inner.PutInContainer(item, container, placement, fromGround), $"put 0x{item:X8} 0x{container:X8} {placement}");
    public bool Merge(uint from, uint to) => Note(inner.Merge(from, to), $"merge 0x{from:X8} 0x{to:X8}");
    public bool WieldFromPaperdoll(uint item, EquipMask mask) =>
        Note(inner.WieldFromPaperdoll(item, mask), $"wield 0x{item:X8} {mask}");
    public bool WieldSecondary(uint item) => Note(inner.WieldSecondary(item), $"secondary 0x{item:X8}");
    public bool Activate(uint item) => inner.Activate(item);
    public bool OpenPack(uint pack) => inner.OpenPack(pack);
    public bool Split(uint stack, uint container, int placement, int amount) =>
        Note(inner.Split(stack, container, placement, amount), $"split 0x{stack:X8} 0x{container:X8} {placement} {amount}");
}
