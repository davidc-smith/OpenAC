using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Core.Combat;
using AcDream.Core.Items;
using AcDream.Core.Player;
using AcDream.Core.Selection;
using AcDream.Core.Spells;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Navigation;

namespace AcDream.Rynth.Host;

/// <summary>The RynthCore table's game data, read from the client's runtime.</summary>
public sealed class RuntimeRynthGameView : IRynthGameView
{
    private readonly GameRuntime _runtime;
    private readonly IAutomationSurface _automation;

    private readonly Func<string, bool>? _submitChatCommand;
    private readonly IClientScreen? _screen;

    public RuntimeRynthGameView(GameRuntime runtime, Func<string, bool>? submitChatCommand = null,
        IAutomationSurface? automation = null, IClientScreen? screen = null)
    {
        _screen = screen;
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _submitChatCommand = submitChatCommand;
        _automation = automation ?? NoOpAutomationSurface.Instance;
        // The surface polls its vendor automation every tick, so this arrives on the game thread.
        _automation.Vendor.TransactionCompleted +=
            transaction => VendorTransactionCompleted?.Invoke(new RynthVendorOutcome(transaction.Success, transaction.Notice));
    }

    public bool TryWorldToScreen(float x, float y, float z, out float screenX, out float screenY)
    {
        screenX = screenY = 0f;
        if (_screen is null || !TryGetPlayerPose(out uint cell, out _, out _))
            return false;
        if (!_screen.TryProject(ToNavigationPosition(cell, x, y, z), out Vector2 point))
            return false;
        (screenX, screenY) = (point.X, point.Y);
        return true;
    }

    // RynthCore's frame is Y up; the client's cell frame is Z up (as RynthNav3D converts).
    internal static PluginNavigationPosition ToNavigationPosition(uint cell, float x, float y, float z) =>
        RuntimeNavigationPositions.FromCellLocal(cell, new Vector3(x, z, y));

    // Beyond this many points a size or position is not a screen's; casting it to an integer is unspecified.
    private const float MaxPoints = 1 << 20;

    private static bool IsPoints(float value) => float.IsFinite(value) && MathF.Abs(value) <= MaxPoints;

    public bool TryGetViewportSize(out uint width, out uint height)
    {
        width = height = 0;
        if (_screen is null || !_screen.TryGetViewportSize(out Vector2 size)
            || !IsPoints(size.X) || !IsPoints(size.Y) || size.X < 0.5f || size.Y < 0.5f)
            return false;
        (uint w, uint h) = ((uint)MathF.Round(size.X), (uint)MathF.Round(size.Y));
        if (w == 0 || h == 0)
            return false; // 0.5 rounds to even, 0
        (width, height) = (w, h);
        return true;
    }

    public bool TryGetRadarRect(out int x0, out int y0, out int x1, out int y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (_screen is null || !_screen.TryGetRadarDisc(out Vector2 centre, out float radius)
            || !IsPoints(centre.X) || !IsPoints(centre.Y) || !IsPoints(radius) || radius < 0f)
            return false;
        (x0, y0, x1, y1) = RadarRect(centre, radius);
        return true;
    }

    // RynthAi draws at min(w, h) / 2 - 2, so the box is the disc plus 2 on every side. The
    // centre and the half-size are rounded once, so the box is always square.
    internal static (int X0, int Y0, int X1, int Y1) RadarRect(Vector2 centre, float radius)
    {
        int cx = (int)MathF.Round(centre.X), cy = (int)MathF.Round(centre.Y), half = (int)MathF.Round(radius + 2f);
        return (cx - half, cy - half, cx + half, cy + half);
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
            ValidLocations = (uint)item.ValidLocations,
            Useability = item.Useability ?? 0,
            ContainerSlot = item.ContainerSlot,
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
            // The body's own orientation: the cell frame's copy only refreshes when the
            // position changes, so a turn in place (RynthAi's TurnToHeading) wouldn't show.
            orientation = body.Orientation;
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
                        record.SpellId & 0xFFFFu, ExpiryServerTime(record, serverNow, clockNow))),
            ];
        }
    }

    // A record's end in server time: double.MaxValue when it has none.
    private static double ExpiryServerTime(ActiveEnchantmentRecord record, double serverNow, double clockNow) =>
        record.Duration > 0 ? serverNow + (record.StartTime + record.Duration - clockNow) : double.MaxValue;

    // Whole seconds: the two clocks are read at different instants, so the raw expiry jitters each read and
    // RynthCore's exact snapshot compare would republish every poll while a timed buff is up.
    private static double WholeSeconds(double expiry) => expiry == double.MaxValue ? expiry : Math.Round(expiry);

    // As RynthCore's ReadPlayerProgressLive: the description's skill table, attribute cache and
    // vitals (ranks, innate, XP spent) with the buffed and base values the surface reports.
    public RynthPlayerProgress? PlayerProgress
    {
        get
        {
            if (!_automation.IsAvailable || PlayerId == 0
                || _runtime.CharacterOwner.LocalPlayer is not { HasDescription: true } local)
                return null;
            ICharacterInfo character = _automation.Character;
            var skills = new List<RynthSkillProgress>(local.Skills.Count);
            foreach (LocalPlayerState.SkillSnapshot s in local.Skills.Values)
            {
                bool known = character.TryGetSkill(s.SkillId, out PluginSkillInfo info);
                skills.Add(new RynthSkillProgress(
                    s.SkillId, s.Status, s.Ranks, s.Xp, s.Init, known,
                    known ? (int)info.Current : 0, known ? (int)info.Base : 0));
            }

            var attributes = new List<RynthStatProgress>(6);
            foreach (PluginAttributeInfo a in character.Attributes)
            {
                uint init = LocalPlayerState.AttributeIdToKind(a.StatId) is { } kind && local.GetAttribute(kind) is { } snap ? snap.Start : 0;
                attributes.Add(new RynthStatProgress(a.StatId, a.Ranks, init, ClampXp(a.ExperienceSpent), a.Current, a.Base));
            }

            var vitals = new List<RynthStatProgress>(3);
            foreach (PluginVitalInfo v in character.Vitals)
            {
                uint init = local.Get((LocalPlayerState.VitalKind)v.Kind) is { } snap ? snap.Start : 0;
                vitals.Add(new RynthStatProgress((uint)v.Kind + 1, v.Ranks, init, ClampXp(v.ExperienceSpent), v.Maximum, v.Base));
            }

            return new RynthPlayerProgress(
                skills, attributes, vitals, character.Level,
                local.Properties.GetInt(AvailableSkillCreditsProperty),
                character.TotalExperience, character.UnassignedExperience,
                character.AvailableLuminance, character.MaximumLuminance, character.VitaeMultiplier);
        }
    }

    private const uint AvailableSkillCreditsProperty = 24;

    private static uint ClampXp(ulong xp) => xp > uint.MaxValue ? uint.MaxValue : (uint)xp;

    // As RynthCore's EnchantmentHooks.CopyPlayerStatMods: the Mult and Add lists' records that change a stat.
    public IReadOnlyList<RynthStatMod>? PlayerStatMods
    {
        get
        {
            if (!_automation.IsAvailable || PlayerId == 0)
                return null;
            double serverNow = ServerTime;
            double clockNow = _runtime.EffectClock.NowSeconds;
            return
            [
                .. _runtime.CharacterOwner.Spellbook.ActiveEnchantmentSnapshot
                    .Where(record => record.Bucket is 1u or 2u && record.StatModType is not null && (record.SpellId & 0xFFFFu) != 0)
                    .Select(record => new RynthStatMod(
                        record.SpellId & 0xFFFFu, WholeSeconds(ExpiryServerTime(record, serverNow, clockNow)),
                        record.StatModType!.Value, record.StatModKey ?? 0, record.StatModValue ?? 0f, record.SpellCategory)),
            ];
        }
    }

    public bool RequestAdvancement(int kind, uint statId, uint amount)
    {
        PluginAdvancementKind? mapped = kind switch
        {
            1 => PluginAdvancementKind.Attribute,
            2 => PluginAdvancementKind.Vital,
            3 => PluginAdvancementKind.Skill,
            4 => PluginAdvancementKind.TrainSkill,
            _ => null,
        };
        return mapped is { } k && _automation.Character.RequestAdvancement(k, statId, amount).Accepted;
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

    public bool TryGetPendingItemRequest(out uint acType, out uint objectId)
    {
        var transactions = _runtime.InventoryOwner.Transactions;
        return PendingFor(transactions.PendingRequest, transactions.ItemBusyCount, out acType, out objectId);
    }

    /// <summary>
    /// OpenAC refuses item actions unless the busy count is 0 and nothing is pending, so a busy
    /// count with no request reports OpenAC's own type 0x100 (see the ClientActionGates stand-in).
    /// </summary>
    internal static bool PendingFor(PendingInventoryRequest? pending, int itemBusyCount, out uint acType, out uint objectId)
    {
        if (pending is { } p)
        {
            (acType, objectId) = (AcRequestType(p.Kind), p.ItemId);
            return true;
        }
        if (itemBusyCount > 0)
        {
            (acType, objectId) = (0x100, 0);
            return true;
        }
        (acType, objectId) = (0, 0);
        return false;
    }

    /// <summary>acclient's request-slot numbers (RynthCore's ClientActionGates.RequestTypeName).</summary>
    internal static uint AcRequestType(InventoryRequestKind kind) => kind switch
    {
        InventoryRequestKind.Merge => 1,
        InventoryRequestKind.SplitToContainer or InventoryRequestKind.SplitToWorld => 2,
        InventoryRequestKind.Move => 3,
        InventoryRequestKind.Pickup => 4,
        InventoryRequestKind.PutInContainer => 5,
        InventoryRequestKind.DropToWorld => 6,
        InventoryRequestKind.Wield => 7,
        InventoryRequestKind.Give => 9,
        InventoryRequestKind.Shop => 10,
        _ => 0,
    };

    public bool IsAttackInProgress
    {
        get
        {
            RuntimeCombatAttackState attack = _runtime.ActionOwner.CombatAttack;
            return attack.AttackRequestInProgress || attack.AttackServerResponsePending || attack.RepeatAttackInProgress;
        }
    }

    public ulong InventoryRevision => _runtime.InventoryOwner.Objects.MutationRevision;

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

    // RynthCore's UseObject is the retail client's use, a double-click: a weapon or caster
    // in the pack is wielded (RynthAi wields its buffing wand this way). The automation's
    // Use is the plain use, so those go through Equip, which wields the way a double-click does.
    public bool UseObject(uint objectId) =>
        _runtime.ItemInteractionOwner.ClassifyPrimaryUse(objectId)
            is AcDream.Core.Items.ItemPrimaryUseResult.WieldRight or AcDream.Core.Items.ItemPrimaryUseResult.WieldLeft
            ? _automation.Equipment.Equip(objectId).Accepted
            : _automation.Items.Use(objectId).Accepted;

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

    public bool MoveItemExternal(uint objectId, uint targetId, uint amount)
    {
        if (objectId == 0)
            return false;
        uint player = PlayerId;
        if (!IsOwnedItem(objectId))
        {
            // Loot: the pickup takes the whole stack and refuses anything not in the open container.
            return (targetId == 0u || targetId == player
                ? _automation.Loot.Pickup(objectId)
                : _automation.Loot.Pickup(objectId, targetId)).Accepted;
        }
        if (targetId == 0u)
            return _automation.Items.Drop(objectId, amount).Accepted;
        ClientObject? target = _runtime.InventoryOwner.Objects.Get(targetId);
        if (targetId == player || (target is { ItemsCapacity: > 0 } && IsOwnedItem(targetId)))
            return _automation.Items.MoveToContainer(objectId, targetId, amount).Accepted;
        // Any other known target that is not the player's own: a give (an NPC or another player).
        return target is not null && !IsOwnedItem(targetId)
            && _automation.Items.Give(objectId, targetId, amount).Accepted;
    }

    public bool GiveItem(uint objectId, uint targetId, uint amount) =>
        _automation.Items.Give(objectId, targetId, amount).Accepted;

    public bool CloseContainer(uint containerId) => _automation.Loot.Close(containerId).Accepted;

    public bool IsOwnedTinkeringTool(uint objectId) =>
        IsOwnedItem(objectId)
        && _runtime.InventoryOwner.Objects.Get(objectId) is { } tool
        && (tool.Type & ItemType.TinkeringTool) != 0;

    public bool IsOwnedItem(uint objectId) =>
        objectId != 0
        && _runtime.InventoryOwner.Objects.Get(objectId) is { } item
        && RuntimeWorldObjectProjection.IsPlayerOwned(item, PlayerId, _runtime.InventoryOwner.Objects);

    public bool Salvage(uint toolId, IReadOnlyList<uint> itemIds) =>
        _automation.Items.Salvage(toolId, itemIds).Accepted;

    // The ObjDesc swaps, as RynthCore captures them at CreateObject: id and offset only. RynthCore
    // reads the client's unpacked Subpalette.offset, a colour index; the wire sends blocks of eight.
    public IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId)
    {
        if (!_runtime.EntityObjects.Entities.TryGetActive(objectId, out RuntimeEntityRecord record))
            return _runtime.InventoryOwner.Objects.Get(objectId) is null ? null : [];
        return [.. record.Snapshot.SubPalettes
            .Select(p => ((uint)p.SubPaletteId, (uint)p.Offset * 8u))
            .OrderBy(p => p.Item2)];
    }

    // A container slot as the client's placement search counts one (packs and spell foci alike).
    public bool TryCountContents(uint containerId, out int items, out int containers)
    {
        items = containers = 0;
        ClientObjectTable objects = _runtime.InventoryOwner.Objects;
        if (objects.Get(containerId) is null)
            return false;
        foreach (uint id in objects.GetContents(containerId))
        {
            if (objects.Get(id) is { } held && InventoryContainerPlacementPolicy.IsContainer(held))
                containers++;
            else
                items++;
        }
        return true;
    }

    public event Action<RynthVendorOutcome>? VendorTransactionCompleted;

    public RynthVendor? Vendor
    {
        get
        {
            VendorState state = _runtime.InventoryOwner.Vendor;
            if (state.VendorId == 0u)
                return null;
            VendorShopProfile profile = state.Profile;
            IReadOnlyList<PluginVendorItem> priced = _automation.Vendor.Items;
            var items = new RynthVendorItem[state.Items.Count];
            for (int i = 0; i < items.Length; i++)
            {
                VendorShopItem listing = state.Items[i];
                int value = listing.Value ?? 0;
                int stack = Math.Max(1, listing.DescStackSize ?? 1);
                items[i] = new RynthVendorItem(
                    listing.ItemGuid, listing.WeenieClassId, listing.Name ?? string.Empty, listing.ItemType ?? 0u,
                    listing.IconId,
                    // The ApproachVendor wiring records ACE's unlimited stock (0 or less) as -1,
                    // and the vendor window reads a negative stock as unlimited.
                    Amount: listing.StackSize > 0 ? listing.StackSize : -1,
                    StackSize: stack,
                    MaxStackSize: listing.MaxStackSize ?? 0,
                    Value: value,
                    UnitValue: stack > 1 ? value / stack : value,
                    UnitPrice: UnitPrice(priced, i, listing.ItemGuid),
                    Burden: listing.Burden ?? 0,
                    NeedsContainerSlot: (listing.ItemsCapacity ?? 0) > 0 || (listing.ContainersCapacity ?? 0) > 0);
            }

            return new RynthVendor(
                state.VendorId,
                _runtime.InventoryOwner.Objects.Get(state.VendorId)?.Name ?? string.Empty,
                profile.MerchandiseItemTypes,
                ClampToInt(profile.MerchandiseMinValue),
                ClampToInt(profile.MerchandiseMaxValue),
                profile.DealMagicalItems,
                profile.BuyPrice,
                profile.SellPrice,
                profile.AlternateCurrencyWcid,
                profile.AlternateCurrencyPluralName ?? string.Empty,
                ClampToInt(profile.AlternateCurrencyAmount),
                profile.AlternateCurrencyWcid == 0u ? -1 : CountOwned(profile.AlternateCurrencyWcid),
                PlayerCoins(),
                items);
        }
    }

    public bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice)
    {
        IVendorAutomation vendor = _automation.Vendor;
        // The surface's buy list is shared, so a Rynth request replaces anything staged there.
        vendor.ClearBuyList();
        foreach ((uint id, int amount) in items)
        {
            PluginVendorCommandResult added = vendor.AddToBuyList(id, amount);
            if (!added.Accepted)
            {
                vendor.ClearBuyList();
                notice = added.Notice ?? $"0x{id:X8} could not be bought ({added.Status})";
                return false;
            }
        }

        PluginVendorCommandResult sent = vendor.BuyAll();
        if (!sent.Accepted)
            vendor.ClearBuyList();
        notice = sent.Notice ?? (sent.Accepted ? null : sent.Status.ToString());
        return sent.Accepted;
    }

    public bool VendorSell(IReadOnlyList<uint> itemIds, out string? notice)
    {
        IVendorAutomation vendor = _automation.Vendor;
        // The surface's sell list is shared, so a Rynth request replaces anything staged there.
        vendor.ClearSellList();
        foreach (uint id in itemIds)
        {
            PluginVendorCommandResult added = vendor.AddToSellList(id);
            if (!added.Accepted)
            {
                vendor.ClearSellList();
                notice = added.Notice ?? $"0x{id:X8} could not be sold ({added.Status})";
                return false;
            }
        }

        PluginVendorCommandResult sent = vendor.SellAll();
        if (!sent.Accepted)
            vendor.ClearSellList();
        notice = sent.Notice ?? (sent.Accepted ? null : sent.Status.ToString());
        return sent.Accepted;
    }

    public RynthTradeState? Trade
    {
        get
        {
            ITradeAutomation trade = _automation.Trade;
            if (!trade.IsAvailable)
                return null;
            PluginTradeEventCounters c = trade.Events;
            return new RynthTradeState(
                trade.IsOpen, trade.MyAccepted, trade.PartnerAccepted, trade.PartnerObjectId,
                trade.MyItems, trade.PartnerItems,
                c.Generation, c.Sequence, c.InitiatorId, c.LastEventType, c.FailureCount, c.LastFailureItemId,
                c.LastFailureReason, c.LastCloseReason, c.LastAcceptedBy, c.LastDeclinedBy, c.LastResetBy,
                c.CompletedCount, c.PartnerAcceptCount);
        }
    }

    public bool TradeOpen(uint partnerId) => _automation.Trade.Open(partnerId).Accepted;
    public bool TradeAdd(uint itemId) => _automation.Trade.Add(itemId).Accepted;
    public bool TradeAccept() => _automation.Trade.Accept().Accepted;
    public bool TradeDecline() => _automation.Trade.Decline().Accepted;
    public bool TradeReset() => _automation.Trade.Reset().Accepted;
    public bool TradeClose() => _automation.Trade.End().Accepted;

    // The automation lists the shop in the vendor state's order; a search covers a list that differs.
    private static int UnitPrice(IReadOnlyList<PluginVendorItem> priced, int index, uint listingId)
    {
        if (index < priced.Count && priced[index].TemplateObjectId == listingId)
            return priced[index].UnitPrice;
        foreach (PluginVendorItem item in priced)
        {
            if (item.TemplateObjectId == listingId)
                return item.UnitPrice;
        }
        return 0;
    }

    // Min/max use uint.MaxValue as "no limit", which becomes int.MaxValue.
    private static int ClampToInt(uint value) => value > int.MaxValue ? int.MaxValue : (int)value;

    // CoinValue (PropertyInt 20), from the player description first as TryGetInt64Property reads; -1 unknown.
    private int PlayerCoins()
    {
        const uint coinValue = 20u;
        uint player = PlayerId;
        if (player == 0)
            return -1;
        if (_runtime.CharacterOwner.LocalPlayer is { HasDescription: true } local
            && local.Properties.Ints.TryGetValue(coinValue, out int described))
            return described;
        return _runtime.InventoryOwner.Objects.Get(player) is { } self
            && self.Properties.Ints.TryGetValue(coinValue, out int coins)
            ? coins
            : -1;
    }

    // RynthCore's CountAltCurrency: the player's and their packs' direct contents, not worn items.
    private int CountOwned(uint wcid)
    {
        uint player = PlayerId;
        if (player == 0)
            return -1;
        ClientObjectTable objects = _runtime.InventoryOwner.Objects;
        long total = 0;
        foreach (uint id in objects.GetContents(player))
        {
            if (objects.Get(id) is not { } item)
                continue;
            if (item.WeenieClassId == wcid)
                total += Math.Max(1, item.StackSize);
            if (item.ItemsCapacity > 0)
            {
                foreach (uint inner in objects.GetContents(id))
                {
                    if (objects.Get(inner) is { } packed && packed.WeenieClassId == wcid)
                        total += Math.Max(1, packed.StackSize);
                }
            }
        }
        return (int)Math.Min(total, int.MaxValue);
    }
}
