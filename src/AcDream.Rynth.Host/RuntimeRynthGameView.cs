using System.Numerics;
using AcDream.Core.Items;
using AcDream.Runtime;
using AcDream.Runtime.Entities;

namespace AcDream.Rynth.Host;

/// <summary>The RynthCore table's game data, read from the client's runtime.</summary>
public sealed class RuntimeRynthGameView : IRynthGameView
{
    private readonly GameRuntime _runtime;

    private readonly Func<string, bool>? _submitChatCommand;

    public RuntimeRynthGameView(GameRuntime runtime, Func<string, bool>? submitChatCommand = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _submitChatCommand = submitChatCommand;
    }

    public uint SelectedObjectId => _runtime.ActionOwner.Selection.SelectedObjectId ?? 0u;

    public bool SubmitChatCommand(string text) => _submitChatCommand?.Invoke(text) ?? false;

    public uint PlayerId => _runtime.PlayerIdentity.ServerGuid;

    public bool TryGetObjectName(uint objectId, out string name)
    {
        name = _runtime.InventoryOwner.Objects.Get(objectId)?.Name ?? string.Empty;
        return name.Length > 0;
    }

    // The player's own properties come from the player description first (what
    // the plugin API's TotalExperience reads), then from the player object.
    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value)
    {
        if (objectId != 0 && objectId == PlayerId
            && _runtime.CharacterOwner.LocalPlayer is { HasDescription: true } player
            && player.Properties.Int64s.TryGetValue(propertyId, out value))
        {
            return true;
        }

        ClientObject? item = _runtime.InventoryOwner.Objects.Get(objectId);
        if (item is not null && item.Properties.Int64s.TryGetValue(propertyId, out value))
            return true;

        value = 0;
        return false;
    }

    public void WriteChatLine(string text, int chatType) =>
        _runtime.CommunicationOwner.Chat.OnSystemMessage(text, unchecked((uint)chatType));

    public string AccountName => _runtime.CharacterSelection.Snapshot.AccountName;

    public string WorldName => _runtime.CharacterSelection.Snapshot.WorldName;

    public bool TryGetObject(uint objectId, out RynthObjectInfo info)
    {
        ClientObject? item = _runtime.InventoryOwner.Objects.Get(objectId);
        if (item is null)
        {
            info = null!;
            return false;
        }

        // These tables are the client's live ones, read by plugins on the game thread only.
        PropertyBundle properties = item.Properties;
        // The player's own tables come from the player description first, as
        // TryGetInt64Property reads them.
        if (objectId != 0 && objectId == PlayerId
            && _runtime.CharacterOwner.LocalPlayer is { HasDescription: true } player)
        {
            properties = Merge(item.Properties, player.Properties);
        }

        info = new RynthObjectInfo
        {
            ContainerId = item.ContainerId,
            WielderId = item.WielderId,
            EquipLocation = (uint)item.CurrentlyEquippedLocation,
            ItemType = (uint)item.Type,
            Wcid = item.WeenieClassId,
            Bitfield = item.PublicWeenieBitfield ?? 0,
            IconId = item.IconId,
            IconUnderlayId = item.IconUnderlayId,
            IconOverlayId = item.IconOverlayId,
            SpellId = item.SpellId,
            HouseOwnerId = item.HouseOwnerId,
            StackSize = item.StackSize,
            StackSizeMax = item.StackSizeMax,
            Value = item.Value,
            Burden = item.Burden,
            ItemsCapacity = item.ItemsCapacity,
            Structure = item.Structure,
            MaxStructure = item.MaxStructure,
            Workmanship = item.Workmanship,
            MaterialType = item.MaterialType,
            Identified = item.AppraisalAnswered && !item.LastAppraisalUnsuccessful,
            AppraisedSpellIds = item.AppraisedSpellIds,
            Ints = properties.Ints,
            DataIds = properties.DataIds,
            InstanceIds = properties.InstanceIds,
        };
        return true;
    }

    public IReadOnlyList<uint> GetContainerContents(uint containerId)
    {
        var ids = new SortedSet<uint>(_runtime.InventoryOwner.Objects.GetContents(containerId));
        foreach (ClientObject worn in _runtime.InventoryOwner.Objects.GetEquippedBy(containerId))
            ids.Add(worn.ObjectId);
        return [.. ids];
    }

    public bool TryGetObjectPosition(uint objectId, out uint cellId, out Vector3 origin)
    {
        if (_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record)
            && record.PhysicsBody is { } body
            && body.CellPosition.ObjCellId != 0)
        {
            cellId = body.CellPosition.ObjCellId;
            origin = body.CellPosition.Frame.Origin;
            return true;
        }

        cellId = 0;
        origin = default;
        return false;
    }

    private static PropertyBundle Merge(PropertyBundle objectProperties, PropertyBundle description)
    {
        PropertyBundle merged = objectProperties.Clone();
        foreach (KeyValuePair<uint, int> pair in description.Ints)
            merged.Ints[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, uint> pair in description.DataIds)
            merged.DataIds[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, uint> pair in description.InstanceIds)
            merged.InstanceIds[pair.Key] = pair.Value;
        return merged;
    }
}
