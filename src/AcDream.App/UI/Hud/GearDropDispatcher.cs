using AcDream.Core.Items;
using AcDream.Runtime.Gameplay;

namespace AcDream.App.UI.Hud;

/// <summary>
/// Decides and sends every move the Gear Deck makes. It applies the retail
/// inventory's rules in the retail order (InventoryController.HandleDropRelease,
/// PaperdollController.HandleDropRelease); it adds none of its own.
/// </summary>
internal sealed class GearDropDispatcher(
    ClientObjectTable objects, Func<uint> playerGuid, IGearItemActions actions)
{
    /// <summary>A drop on a pack icon: the item goes to the pack's top. A bag goes to the main pack:
    /// at its top on the main pack's own icon, else to the hovered side pack's place in the pack column.</summary>
    public void DropOnPack(uint item, uint packId, bool fromGround)
    {
        if (item == 0u) return;
        uint player = playerGuid();
        bool sourceIsBag = objects.Get(item) is { } src && IsBag(src);
        uint dest = sourceIsBag ? player : packId;
        if (dest == 0u || dest == item) return;

        InventoryContainerPlacementRejection legality =
            InventoryContainerPlacementPolicy.Evaluate(objects, item, dest, player);
        if (!ResolveLegality(item, ref dest, ref legality, out _))
            return;
        if (!actions.Ready()) return;
        int placement = 0;
        if (sourceIsBag && packId != player)
        {
            if (BagListPlacement(player, item, packId) is not int bagPlacement) return;   // already there
            placement = bagPlacement;
        }
        actions.PutInContainer(item, dest, placement, fromGround);
    }

    /// <summary>A drop on a cell of a pack's grid: merge onto a same-kind stack, else place left of the cell.</summary>
    public void DropOnCell(uint item, uint packId, uint targetItemId, int targetIndex, bool fromGround)
    {
        if (item == 0u) return;
        uint player = playerGuid();
        bool sourceIsBag = objects.Get(item) is { } src && IsBag(src);
        uint dest = packId;
        InventoryContainerPlacementRejection legality = sourceIsBag
            ? InventoryContainerPlacementRejection.ContainerCapacityFull
            : InventoryContainerPlacementPolicy.Evaluate(objects, item, dest, player);
        if (!ResolveLegality(item, ref dest, ref legality, out bool fellThrough))
            return;
        if (!actions.Ready()) return;

        if (targetItemId != 0u && TryMerge(item, targetItemId))
            return;

        int? requested = fellThrough
            ? 0
            : ListDropPlacement(dest, item, targetItemId != 0u, targetIndex);
        if (requested is not int placement) return;      // already there
        if (dest == item) return;
        actions.PutInContainer(item, dest, placement, fromGround);
    }

    /// <summary>A drop on a paperdoll slot.</summary>
    public void DropOnSlot(uint item, EquipMask slotMask)
    {
        if (slotMask == EquipMask.None || objects.Get(item) is not { } obj) return;
        if (!actions.Ready()) return;
        if (ItemEquipRules.IsOffHandWeaponDrop(obj, slotMask))
        {
            actions.WieldSecondary(item);
            return;
        }
        EquipMask wield = ItemEquipRules.ResolvePaperdollDropWieldMask(obj, slotMask);
        if (wield == EquipMask.None) return;
        actions.WieldFromPaperdoll(item, wield);
    }

    /// <summary>Whether a drop on the figure would wear <paramref name="item"/>: at <paramref name="region"/>
    /// when it fits there, else anywhere for an item retail's doll wears whole (IsAutoWearItem).</summary>
    public static bool FigureAccepts(ClientObject item, EquipMask region) =>
        RegionFits(item, region) || ItemEquipRules.IsAutoWearItem(item);

    private static bool RegionFits(ClientObject item, EquipMask region) =>
        region != EquipMask.None
        && (ItemEquipRules.IsOffHandWeaponDrop(item, region)
            || ItemEquipRules.ResolvePaperdollDropWieldMask(item, region) != EquipMask.None);

    /// <summary>A drop on the figure: the body region under the pointer when it fits the item (as a drop on that
    /// slot), else retail's doll drop (PaperdollController.HandleDollDrop): an auto-wear item at all its valid
    /// locations, and nothing for anything else.</summary>
    public void DropOnFigure(uint item, EquipMask region)
    {
        if (objects.Get(item) is not { } obj) return;
        if (RegionFits(obj, region))
        {
            DropOnSlot(item, region);
            return;
        }
        if (!ItemEquipRules.IsAutoWearItem(obj)) return;
        if (!actions.Ready()) return;
        actions.WieldFromPaperdoll(item, obj.ValidLocations);
    }

    /// <summary>Takes a worn item off into the first pack with room.</summary>
    public void TakeOff(uint item)
    {
        uint player = playerGuid();
        uint container = InventoryPlacementSearch.ChooseContainer(
            objects, item, player, 0u, player, out InventoryContainerPlacementRejection refusal);
        if (container == 0u)
        {
            ReportRefusal(refusal, item, player);
            return;
        }
        if (!actions.Ready()) return;
        actions.PutInContainer(item, container, 0, false);
    }

    // Retail order: a capacity refusal falls through to the pack that has room
    // (or reports against the whole inventory); any other refusal is reported.
    private bool ResolveLegality(
        uint item,
        ref uint dest,
        ref InventoryContainerPlacementRejection legality,
        out bool fellThrough)
    {
        fellThrough = false;
        uint player = playerGuid();
        if (IsCapacityRejection(legality))
        {
            uint fallthrough = InventoryPlacementSearch.ChooseContainer(
                objects, item, player, dest, player, out InventoryContainerPlacementRejection noRoom);
            if (fallthrough == 0u)
            {
                ReportRefusal(noRoom, item, player);
                return false;
            }
            legality = InventoryContainerPlacementPolicy.Evaluate(objects, item, fallthrough, player);
            dest = fallthrough;
            fellThrough = true;
        }
        if (legality != InventoryContainerPlacementRejection.None)
        {
            ReportRefusal(legality, item, dest);
            return false;
        }
        return true;
    }

    private void ReportRefusal(InventoryContainerPlacementRejection rejection, uint item, uint dest)
    {
        if (InventoryContainerPlacementPolicy.ComposeClientLocal(
                rejection, objects.Get(item), objects.Get(dest), playerGuid()) is { } text)
        {
            actions.Report(text);
        }
    }

    private bool TryMerge(uint sourceId, uint targetId)
    {
        if (objects.Get(sourceId) is not { } source || objects.Get(targetId) is not { } target)
            return false;
        StackMergePlan? plan = StackMergePlanner.Plan(
            new StackMergeItem(source.ObjectId, source.WeenieClassId, source.StackSize, source.StackSizeMax, source.TradeState),
            new StackMergeItem(target.ObjectId, target.WeenieClassId, target.StackSize, target.StackSizeMax, target.TradeState),
            true,
            Math.Max(1, source.StackSize));
        return plan is not null && actions.Merge(sourceId, targetId);
    }

    private int? ListDropPlacement(uint containerId, uint itemId, bool overItem, int targetIndex)
    {
        int count = 0, source = -1;
        foreach (uint guid in objects.GetContents(containerId))
        {
            if (objects.Get(guid) is not { } listed
                || listed.CurrentlyEquippedLocation != EquipMask.None
                || IsBag(listed))
            {
                continue;
            }
            if (guid == itemId) source = count;
            count++;
        }
        int dropIndex = overItem ? targetIndex : int.MaxValue;   // an empty cell is past the last item
        return ItemListDropPlacement.Resolve(dropIndex, count, source, wholeStack: true);
    }

    // Retail's ListDropPlacement(bags: true): the hovered cell's index among the player's bags.
    private int? BagListPlacement(uint player, uint itemId, uint targetBag)
    {
        int count = 0, source = -1, target = int.MaxValue;
        foreach (uint guid in objects.GetContents(player))
        {
            if (objects.Get(guid) is not { } listed
                || listed.CurrentlyEquippedLocation != EquipMask.None
                || !IsBag(listed))
            {
                continue;
            }
            if (guid == itemId) source = count;
            if (guid == targetBag) target = count;
            count++;
        }
        return ItemListDropPlacement.Resolve(target, count, source, wholeStack: true);
    }

    private static bool IsCapacityRejection(InventoryContainerPlacementRejection r) =>
        r is InventoryContainerPlacementRejection.ItemCapacityFull
            or InventoryContainerPlacementRejection.ContainerCapacityFull;

    private static bool IsBag(ClientObject item) =>
        item.ContainerTypeHint != 0u
        || item.Type.HasFlag(ItemType.Container)
        || item.ItemsCapacity > 0;
}
