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
    Action<uint, uint, int>? sendPutItemInContainer) : IGearItemActions
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

    public bool Merge(uint from, uint to) => items.TryMergeItemsForAutomation(from, to);
    public bool WieldFromPaperdoll(uint item, EquipMask mask) => items.WieldFromPaperdoll(item, mask);
    public bool WieldSecondary(uint item) => items.TryWieldItemSecondary(item);
    public bool Activate(uint item) => items.ActivateItem(item);
    public bool OpenPack(uint pack) => items.ActivateItem(pack);
}
