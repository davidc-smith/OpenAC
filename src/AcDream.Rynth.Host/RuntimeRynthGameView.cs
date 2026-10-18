using System.Numerics;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime;
using AcDream.Runtime.Entities;

namespace AcDream.Rynth.Host;

/// <summary>The RynthCore table's game data, read from the client's runtime.</summary>
public sealed class RuntimeRynthGameView : IRynthGameView
{
    private readonly GameRuntime _runtime;
    private readonly IAutomationSurface _automation;

    private readonly Func<string, bool>? _submitChatCommand;

    public RuntimeRynthGameView(GameRuntime runtime, Func<string, bool>? submitChatCommand = null,
        IAutomationSurface? automation = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _submitChatCommand = submitChatCommand;
        _automation = automation ?? NoOpAutomationSurface.Instance;
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
            Bools = properties.Bools,
            Floats = properties.Floats,
            Strings = properties.Strings,
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

    public bool TryGetPlayerPose(out uint cellId, out Vector3 origin, out Quaternion orientation)
    {
        uint player = PlayerId;
        if (player != 0
            && _runtime.EntityObjects.Entities.TryGetActive(player, out RuntimeEntityRecord record)
            && record.PhysicsBody is { } body
            && body.CellPosition.ObjCellId != 0)
        {
            cellId = body.CellPosition.ObjCellId;
            origin = body.CellPosition.Frame.Origin;
            orientation = body.CellPosition.Frame.Orientation;
            return true;
        }

        cellId = 0;
        origin = default;
        orientation = Quaternion.Identity;
        return false;
    }

    public bool TryGetPlayerHeading(out float headingDegrees)
    {
        PluginNavigationSnapshot snapshot = _automation.Navigation.Snapshot;
        headingDegrees = snapshot.IsAvailable ? snapshot.Position.HeadingDegrees : 0;
        return snapshot.IsAvailable;
    }

    public bool TryGetObjectHeading(uint objectId, out float headingDegrees)
    {
        if (_automation.Objects.TryGet(objectId, out PluginWorldObject found) && found.HasPosition)
        {
            headingDegrees = found.Position.HeadingDegrees;
            return true;
        }

        headingDegrees = 0;
        return false;
    }

    public bool IsPortaling => _automation.Navigation.Snapshot.IsPortalSpace;

    public int BusyCount => _automation.Recovery.CaptureBusyState().BusyCount;

    public int CombatMode => _automation.IsAvailable
        ? RynthTranslate.ToRynthCombatMode(_automation.Combat.Snapshot.Mode)
        : RynthTranslate.NonCombat;

    public bool TryGetPlayerVitals(out RynthVitals vitals)
    {
        ICharacterInfo character = _automation.Character;
        vitals = new RynthVitals(
            character.CurrentHealth, character.MaxHealth,
            character.CurrentStamina, character.MaxStamina,
            character.CurrentMana, character.MaxMana);
        return _automation.IsAvailable && character.MaxHealth > 0;
    }

    public bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana)
    {
        ICharacterInfo character = _automation.Character;
        health = character.BaseHealth;
        stamina = character.BaseStamina;
        mana = character.BaseMana;
        return _automation.IsAvailable && health > 0;
    }

    public float VitaeMultiplier => _automation.IsAvailable ? _automation.Character.VitaeMultiplier : 1f;

    // The server clock as the client last synced it; 0 before login, as RynthCore reports it.
    public double ServerTime => PlayerId == 0 ? 0 : _runtime.EnvironmentOwner.WorldTime.NowTicks;

    public IReadOnlyList<RynthEnchantment>? PlayerEnchantments
    {
        get
        {
            if (!_automation.IsAvailable || PlayerId == 0)
                return null;
            double now = ServerTime;
            return
            [
                .. _automation.Character.ActiveEnchantments.Select(enchantment => new RynthEnchantment(
                    enchantment.SpellId, RynthTranslate.ToServerExpiry(now, enchantment.SecondsRemaining))),
            ];
        }
    }

    public bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training)
    {
        if (IsPlayer(objectId) && _automation.Character.TryGetSkill(skillId, out PluginSkillInfo skill))
        {
            buffed = (int)skill.Current;
            baseLevel = (int)skill.Base;
            training = (int)skill.Training;
            return true;
        }

        buffed = baseLevel = training = 0;
        return false;
    }

    public bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel)
    {
        if (IsPlayer(objectId))
        {
            foreach (PluginAttributeInfo attribute in _automation.Character.Attributes)
            {
                if (attribute.StatId != attributeId)
                    continue;
                buffed = attribute.Current;
                baseLevel = attribute.Base;
                return true;
            }
        }

        buffed = baseLevel = 0;
        return false;
    }

    // RynthCore's secondary-attribute ids: 1 max health, 3 max stamina, 5 max mana.
    public bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum)
    {
        if (IsPlayer(objectId) && (vitalStatId is 1 or 3 or 5)
            && _automation.Character.TryGetVital((int)(vitalStatId - 1) / 2, out PluginVitalInfo vital))
        {
            baseMaximum = vital.Base;
            return true;
        }

        baseMaximum = 0;
        return false;
    }

    public int IsSpellKnown(uint objectId, uint spellId) =>
        !IsPlayer(objectId) || !_automation.IsAvailable ? -1 : _automation.Spells.IsKnown(spellId) ? 1 : 0;

    public IReadOnlyList<uint>? KnownSpells =>
        PlayerId == 0 ? null : [.. _runtime.CharacterOwner.Spellbook.LearnedSpells.Order()];

    public bool IsAttackable(uint objectId)
    {
        uint player = PlayerId;
        if (player == 0)
            return false;
        ClientObjectTable objects = _runtime.InventoryOwner.Objects;
        return SelectedObjectHealthPolicy.ObjectIsAttackable(player, objects.Get(player), objectId, objects.Get(objectId));
    }

    public uint OpenContainerId => _automation.Objects.OpenContainerObjectId;

    public ServerReplySnapshot ServerReplies => _runtime.InventoryOwner.ServerReplies.Snapshot;

    private bool IsPlayer(uint objectId) => objectId != 0 && objectId == PlayerId;

    private static PropertyBundle Merge(PropertyBundle objectProperties, PropertyBundle description)
    {
        PropertyBundle merged = objectProperties.Clone();
        foreach (KeyValuePair<uint, int> pair in description.Ints)
            merged.Ints[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, uint> pair in description.DataIds)
            merged.DataIds[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, uint> pair in description.InstanceIds)
            merged.InstanceIds[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, bool> pair in description.Bools)
            merged.Bools[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, double> pair in description.Floats)
            merged.Floats[pair.Key] = pair.Value;
        foreach (KeyValuePair<uint, string> pair in description.Strings)
            merged.Strings[pair.Key] = pair.Value;
        return merged;
    }

    public bool SelectObject(uint objectId)
    {
        SelectionState selection = _runtime.ActionOwner.Selection;
        selection.Select(objectId, SelectionChangeSource.Plugin);
        // RynthCore answers 1 for a repeat select too; OpenAC's Select is false when nothing changed.
        return (selection.SelectedObjectId ?? 0u) == objectId;
    }

    public bool UseObject(uint objectId) => _automation.Items.Use(objectId).Accepted;

    public bool RequestIdentify(uint objectId) => _automation.Objects.Identify(objectId).Accepted;

    public bool QueryHealth(uint objectId)
    {
        if (_runtime.Session.CurrentSession is not { } session)
            return false;
        session.SendQueryHealth(objectId);
        return true;
    }

    public bool ChangeCombatMode(int rynthMode) =>
        RynthTranslate.FromRynthCombatMode(rynthMode) is { } mode && _automation.Combat.EnterMode(mode).Accepted;

    public bool MoveItem(uint objectId, uint containerId, int slot, int amount) =>
        amount > 0 && _automation.Items.MoveToContainer(objectId, containerId, (uint)amount, slot).Accepted;

    public bool MergeStack(uint sourceObjectId, uint targetObjectId) =>
        _automation.Items.Merge(sourceObjectId, targetObjectId).Accepted;

    // RynthCore zeroes the busy count at once; OpenAC releases one reference per call.
    public void ResetBusyCount()
    {
        for (int i = 0; i < 64; i++)
        {
            PluginRecoveryResult result = _automation.Recovery.ClearOneBusyReference();
            if (!result.Accepted || result.CurrentCount <= 0)
                return;
        }
    }

    public bool WieldItem(uint objectId, uint equipMask) => _automation.Equipment.Equip(objectId, equipMask).Accepted;
}
