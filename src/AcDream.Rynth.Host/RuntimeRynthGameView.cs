using AcDream.Runtime.Gameplay;
using System.Numerics;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
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
            double serverNow = ServerTime;
            double clockNow = _runtime.EffectClock.NowSeconds;
            // As RynthCore reads the player's registry: the Mult, Add and Cooldown lists, covered layers
            // included. Vitae (bucket 4) is not in those lists.
            return
            [
                .. _runtime.CharacterOwner.Spellbook.ActiveEnchantmentSnapshot
                    .Where(record => record.Bucket is 1u or 2u or Spellbook.CooldownBucket
                        && (record.SpellId & 0xFFFFu) != 0)
                    .Select(record => new RynthEnchantment(
                        record.SpellId & 0xFFFFu,
                        record.Duration > 0
                            ? serverNow + (record.StartTime + record.Duration - clockNow)
                            : double.MaxValue)),
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
        !_automation.IsAvailable || PlayerId == 0 ? null : [.. _runtime.CharacterOwner.Spellbook.LearnedSpells.Order()];

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
        if (!_automation.IsAvailable || _runtime.Session.CurrentSession is not { } session)
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

    public bool IsCastGestureActive => _runtime.MovementOwner.HasPendingMotions;

    public bool TryGetTargetVitals(uint objectId, out RynthVitals vitals)
    {
        if (objectId != 0
            && _runtime.ActionOwner.Combat.TryGetCreatureVitals(objectId, out CombatState.CreatureVitals found))
        {
            vitals = new RynthVitals(
                found.Health, found.HealthMax,
                found.Stamina ?? 0, found.StaminaMax ?? 0,
                found.Mana ?? 0, found.ManaMax ?? 0);
            return true;
        }

        vitals = default;
        return false;
    }

    // OpenAC keeps the player's enchantment registry only.
    public IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId) =>
        IsPlayer(objectId) ? PlayerEnchantments : null;

    // RynthCore holds casts while the player is in portal space. It answers 1 once the client's cast
    // call ran, and a local refusal (unknown spell, no components) reaches the plugin as chat, so
    // only an unavailable surface is false; a false here would stop RynthAi seeing that line.
    public bool CastSpell(uint targetId, uint spellId) =>
        _automation.IsAvailable && !IsPortaling
        && _runtime.ActionOwner.SpellCast.Cast(spellId, targetId) != CastRequestResult.Unavailable;

    // RynthCore sends the attack itself, past the power bar. The body stops first, as OpenAC's own attack does.
    public bool MeleeAttack(uint targetId, int height, float power)
    {
        if (RynthTranslate.ToAttackHeight(height) is not { } attackHeight
            || !_automation.IsAvailable
            || _runtime.Session.CurrentSession is not { } session)
            return false;
        _runtime.PrepareLocalPlayerForAttackRequest();
        session.SendMeleeAttack(targetId, attackHeight, power);
        return true;
    }

    public bool MissileAttack(uint targetId, int height, float accuracy)
    {
        if (RynthTranslate.ToAttackHeight(height) is not { } attackHeight
            || !_automation.IsAvailable
            || _runtime.Session.CurrentSession is not { } session)
            return false;
        _runtime.PrepareLocalPlayerForAttackRequest();
        session.SendMissileAttack(targetId, attackHeight, accuracy);
        return true;
    }

    // OpenAC's own attack state sends the cancel itself when it holds anything (a bot swing the
    // server commenced included), and clears what it holds; otherwise the cancel goes out bare.
    public bool CancelAttack()
    {
        if (!_automation.IsAvailable || _runtime.Session.CurrentSession is not { } session)
            return false;
        RuntimeCombatAttackState attack = _runtime.ActionOwner.CombatAttack;
        if (attack.AttackServerResponsePending || attack.AttackRequestInProgress || attack.RepeatAttackInProgress)
            attack.AbortAutomaticAttack();
        else
            session.SendCancelAttack();
        return true;
    }

    public bool UseObjectOn(uint sourceObjectId, uint targetObjectId) =>
        _automation.Items.Apply(sourceObjectId, targetObjectId).Accepted;

    public bool AutoRunActive => _runtime.MovementOwner.AutoRunActive;

    public bool SetAutoRun(bool enabled) => _automation.IsAvailable && _runtime.MovementOwner.SetAutoRun(enabled);

    public bool SnapHeading(float headingDegrees) => _automation.IsAvailable && _runtime.MovementOwner.SnapHeading(headingDegrees);

    public bool SetMovementIntent(in PluginMovementIntent intent) =>
        _automation.Navigation.SetMovementIntent(intent) == PluginNavigationCommandStatus.Accepted;

    public bool ClearMovementIntent() =>
        _automation.Navigation.ClearMovementIntent() == PluginNavigationCommandStatus.Accepted;

    public PluginMovementIntent? CurrentMovementIntent
    {
        get
        {
            RuntimeLocalPlayerMovementState movement = _runtime.MovementOwner;
            if (!movement.HasCommandInput)
                return null;
            MovementInput input = movement.CommandInput;
            return new PluginMovementIntent(
                input.Forward, input.Backward, input.StrafeLeft, input.StrafeRight,
                input.TurnLeft, input.TurnRight, input.Run, input.Jump);
        }
    }

    public bool StopCompletely() =>
        _automation.IsAvailable && _runtime.MovementOwner.Execute(RuntimeMovementCommand.StopCompletely);

    public bool BeginJump(float power) => _automation.IsAvailable && _runtime.MovementOwner.BeginJump(power);

    public bool TryGetObjectState(uint objectId, out uint state)
    {
        if (_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record))
        {
            state = record.RawPhysicsState;
            return true;
        }

        state = 0;
        return false;
    }

    // Doors play On (0x4000000B) to open and Off (0x4000000C) to close; the record keeps the low 16 bits.
    public bool TryGetObjectMotionOn(uint objectId, out bool isOn)
    {
        isOn = false;
        if (!_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record)
            || record.Snapshot.MotionState?.ForwardCommand is not { } command)
            return false;
        if (command is not (0x000B or 0x000C))
            return false;
        isOn = command == 0x000B;
        return true;
    }


    public bool PickupItem(uint objectId, uint preferredContainerId) =>
        (preferredContainerId == 0u
            ? _automation.Loot.Pickup(objectId)
            : _automation.Loot.Pickup(objectId, preferredContainerId)).Accepted;

    public bool GiveItem(uint objectId, uint targetId, uint amount) =>
        _automation.Items.Give(objectId, targetId, amount).Accepted;

    public bool CloseContainer(uint containerId) => _automation.Loot.Close(containerId).Accepted;

    // The raw ObjDesc swaps, as RynthCore captures them at CreateObject: id and offset only.
    public IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId)
    {
        if (!_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record))
            return _runtime.InventoryOwner.Objects.Get(objectId) is null ? null : [];
        return [.. record.Snapshot.SubPalettes
            .Select(p => ((uint)p.SubPaletteId, (uint)p.Offset))
            .OrderBy(p => p.Item2)];
    }

    public bool TryCountContents(uint containerId, out int items, out int containers)
    {
        items = containers = 0;
        ClientObjectTable objects = _runtime.InventoryOwner.Objects;
        if (objects.Get(containerId) is null)
            return false;
        foreach (uint id in objects.GetContents(containerId))
        {
            if (objects.Get(id) is { ItemsCapacity: > 0 })
                containers++;
            else
                items++;
        }
        return true;
    }
}
