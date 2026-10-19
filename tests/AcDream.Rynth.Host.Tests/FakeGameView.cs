using AcDream.Rynth.Host;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AcDream.Rynth.Host.Tests;

internal sealed class FakeGameView : IRynthGameView
{
    public uint PlayerId
    {
        get => Throw ? throw new InvalidOperationException("view failed") : _playerId;
        set => _playerId = value;
    }

    private uint _playerId;
    public Dictionary<uint, string> Names { get; } = new();
    public Dictionary<(uint Object, uint Property), long> Int64s { get; } = new();
    public List<(string Text, int ChatType)> ChatLines { get; } = new();
    public bool Throw { get; set; }
    public uint SelectedObjectId { get; set; }
    public List<string> SubmittedCommands { get; } = new();

    public bool SubmitChatCommand(string text)
    {
        SubmittedCommands.Add(text);
        return true;
    }

    public bool TryGetObjectName(uint objectId, out string name)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Names.TryGetValue(objectId, out name!);
    }

    public bool TryGetInt64Property(uint objectId, uint propertyId, out long value)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Int64s.TryGetValue((objectId, propertyId), out value);
    }

    public void WriteChatLine(string text, int chatType)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        ChatLines.Add((text, chatType));
    }

    public Dictionary<uint, RynthObjectInfo> Objects { get; } = new();
    public Dictionary<uint, List<uint>> Contents { get; } = new();
    public Dictionary<uint, (uint Cell, System.Numerics.Vector3 Origin)> Positions { get; } = new();
    public string AccountName { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;

    public bool TryGetObject(uint objectId, out RynthObjectInfo info)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Objects.TryGetValue(objectId, out info!);
    }

    public IReadOnlyList<uint> GetContainerContents(uint containerId)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return Contents.TryGetValue(containerId, out List<uint>? ids) ? ids : [];
    }

    public bool TryGetObjectPosition(uint objectId, out uint cellId, out System.Numerics.Vector3 origin)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        bool found = Positions.TryGetValue(objectId, out var position);
        (cellId, origin) = position;
        return found;
    }

    private T Read<T>(T value) => Throw ? throw new InvalidOperationException("view failed") : value;

    public (uint Cell, System.Numerics.Vector3 Origin, System.Numerics.Quaternion Orientation)? Pose { get; set; }
    public float? PlayerHeading { get; set; }
    public Dictionary<uint, float> Headings { get; } = new();
    public bool Portaling { get; set; }
    public int Busy { get; set; }
    public int Mode { get; set; } = 1;
    public RynthVitals? Vitals { get; set; }
    public (uint Health, uint Stamina, uint Mana)? BaseVitals { get; set; }
    public float Vitae { get; set; } = 1f;
    public double Time { get; set; }
    public List<RynthEnchantment>? Enchantments { get; set; }
    public Dictionary<(uint Object, uint Skill), (int Buffed, int Base, int Training)> Skills { get; } = new();
    public Dictionary<(uint Object, uint Attribute), (uint Buffed, uint Base)> Attributes { get; } = new();
    public Dictionary<(uint Object, uint Vital), uint> VitalBases { get; } = new();
    public List<uint>? Known { get; set; }
    public HashSet<uint> Attackable { get; } = new();
    public uint OpenContainer { get; set; }
    public AcDream.Core.Items.ServerReplySnapshot Replies { get; set; }

    public bool TryGetPlayerPose(out uint cellId, out System.Numerics.Vector3 origin, out System.Numerics.Quaternion orientation)
    {
        var pose = Read(Pose);
        (cellId, origin, orientation) = pose ?? default;
        return pose is not null;
    }

    public bool TryGetPlayerHeading(out float headingDegrees)
    {
        float? heading = Read(PlayerHeading);
        headingDegrees = heading ?? 0;
        return heading is not null;
    }

    public bool TryGetObjectHeading(uint objectId, out float headingDegrees) =>
        Read(Headings).TryGetValue(objectId, out headingDegrees);

    public bool IsPortaling => Read(Portaling);
    public int BusyCount => Read(Busy);
    public int CombatMode => Read(Mode);

    public bool TryGetPlayerVitals(out RynthVitals vitals)
    {
        RynthVitals? found = Read(Vitals);
        vitals = found ?? default;
        return found is not null;
    }

    public bool TryGetPlayerBaseVitals(out uint health, out uint stamina, out uint mana)
    {
        var found = Read(BaseVitals);
        (health, stamina, mana) = found ?? default;
        return found is not null;
    }

    public float VitaeMultiplier => Read(Vitae);
    public double ServerTime => Read(Time);
    public IReadOnlyList<RynthEnchantment>? PlayerEnchantments => Read(Enchantments);

    public bool TryGetSkill(uint objectId, uint skillId, out int buffed, out int baseLevel, out int training)
    {
        bool found = Read(Skills).TryGetValue((objectId, skillId), out var skill);
        (buffed, baseLevel, training) = skill;
        return found;
    }

    public bool TryGetAttribute(uint objectId, uint attributeId, out uint buffed, out uint baseLevel)
    {
        bool found = Read(Attributes).TryGetValue((objectId, attributeId), out var attribute);
        (buffed, baseLevel) = attribute;
        return found;
    }

    public bool TryGetVitalBase(uint objectId, uint vitalStatId, out uint baseMaximum) =>
        Read(VitalBases).TryGetValue((objectId, vitalStatId), out baseMaximum);

    public int IsSpellKnown(uint objectId, uint spellId) =>
        Read(Known) is { } known && objectId == _playerId ? (known.Contains(spellId) ? 1 : 0) : -1;

    public IReadOnlyList<uint>? KnownSpells => Read(Known);
    public bool IsAttackable(uint objectId) => Read(Attackable).Contains(objectId);
    public uint OpenContainerId => Read(OpenContainer);
    public AcDream.Core.Items.ServerReplySnapshot ServerReplies => Read(Replies);

    public bool CastGesture { get; set; }
    public Dictionary<uint, RynthVitals> TargetVitals { get; } = new();

    public bool IsCastGestureActive => Read(CastGesture);

    public bool TryGetTargetVitals(uint objectId, out RynthVitals vitals) =>
        Read(TargetVitals).TryGetValue(objectId, out vitals);

    public IReadOnlyList<RynthEnchantment>? GetObjectEnchantments(uint objectId) =>
        objectId == _playerId ? Read(Enchantments) : Read((List<RynthEnchantment>?)null);

    public List<string> Actions { get; } = new();
    public bool ActionAnswer { get; set; } = true;

    private bool Act(string call)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        Actions.Add(call);
        return ActionAnswer;
    }

    public bool SelectObject(uint objectId) => Act($"select {objectId:X8}");
    public bool UseObject(uint objectId) => Act($"use {objectId:X8}");
    public bool RequestIdentify(uint objectId) => Act($"id {objectId:X8}");
    public bool QueryHealth(uint objectId) => Act($"health {objectId:X8}");
    public bool ChangeCombatMode(int rynthMode) => Act($"mode {rynthMode}");
    public bool MoveItem(uint objectId, uint containerId, int slot, int amount) =>
        Act($"move {objectId:X8} {containerId:X8} {slot} {amount}");
    public bool MergeStack(uint sourceObjectId, uint targetObjectId) => Act($"merge {sourceObjectId:X8} {targetObjectId:X8}");
    public void ResetBusyCount() => Act("reset busy");
    public bool WieldItem(uint objectId, uint equipMask) => Act($"wield {objectId:X8} {equipMask:X8}");
    public bool CastSpell(uint targetId, uint spellId) => Act($"cast {targetId:X8} {spellId}");
    public bool MeleeAttack(uint targetId, int height, float power) =>
        Act($"melee {targetId:X8} {height} {power.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
    public bool MissileAttack(uint targetId, int height, float accuracy) =>
        Act($"missile {targetId:X8} {height} {accuracy.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}");
    public bool CancelAttack() => Act("cancel");
    public bool UseObjectOn(uint sourceObjectId, uint targetObjectId) =>
        Act($"useon {sourceObjectId:X8} {targetObjectId:X8}");

    public bool AutoRun { get; set; }
    public List<AcDream.Plugin.Abstractions.PluginMovementIntent?> Intents { get; } = new();
    public Dictionary<uint, uint> ObjectStates { get; } = new();
    public Dictionary<uint, bool> MotionOn { get; } = new();

    public bool AutoRunActive => Read(AutoRun);
    public bool SetAutoRun(bool enabled) => Act($"autorun {(enabled ? 1 : 0)}");
    public bool SnapHeading(float headingDegrees) => Act(FormattableString.Invariant($"heading {headingDegrees:0.##}"));
    public bool SetMovementIntent(in AcDream.Plugin.Abstractions.PluginMovementIntent intent)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        Intents.Add(intent);
        if (ActionAnswer)
            HeldIntent = intent;
        return ActionAnswer;
    }
    public bool ClearMovementIntent()
    {
        if (Throw) throw new InvalidOperationException("view failed");
        Intents.Add(null);
        if (ActionAnswer)
            HeldIntent = null;
        return ActionAnswer;
    }
    /// <summary>What the client holds now; tests clear or replace it to act as the client or another plugin.</summary>
    public AcDream.Plugin.Abstractions.PluginMovementIntent? HeldIntent { get; set; }
    public AcDream.Plugin.Abstractions.PluginMovementIntent? CurrentMovementIntent => Read(HeldIntent);
    public bool StopCompletely() => Act("stop");
    public bool BeginJump(float power) => Act(FormattableString.Invariant($"jump {power:0.###}"));
    public bool TryGetObjectState(uint objectId, out uint state)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return ObjectStates.TryGetValue(objectId, out state);
    }
    public bool TryGetObjectMotionOn(uint objectId, out bool isOn)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        return MotionOn.TryGetValue(objectId, out isOn);
    }

    public Dictionary<uint, List<(uint SubPaletteId, uint Offset)>> Palettes { get; } = new();
    public Dictionary<uint, (int Items, int Containers)> ContentCounts { get; } = new();

    public bool MoveItemExternal(uint objectId, uint targetId, uint amount) =>
        Act($"moveext {objectId:X8} {targetId:X8} {amount}");
    public bool GiveItem(uint objectId, uint targetId, uint amount) => Act($"give {objectId:X8} {targetId:X8} {amount}");
    public bool CloseContainer(uint containerId) => Act($"close {containerId:X8}");

    public HashSet<uint> Tools { get; } = new();
    public HashSet<uint> Owned { get; } = new();
    public bool IsOwnedTinkeringTool(uint objectId) => Read(Tools).Contains(objectId);
    public bool IsOwnedItem(uint objectId) => Read(Owned).Contains(objectId);
    public bool Salvage(uint toolId, IReadOnlyList<uint> itemIds) =>
        Act($"salvage {toolId:X8} {string.Join(",", itemIds.Select(id => id.ToString("X8")))}");
    public IReadOnlyList<(uint SubPaletteId, uint Offset)>? GetPalettes(uint objectId) =>
        Read(Palettes).TryGetValue(objectId, out var list) ? list : null;
    public bool TryCountContents(uint containerId, out int items, out int containers)
    {
        if (Throw) throw new InvalidOperationException("view failed");
        bool known = ContentCounts.TryGetValue(containerId, out var counts);
        (items, containers) = counts;
        return known;
    }

    public RynthVendor? OpenVendor { get; set; }
    public string? VendorNotice { get; set; }
    public RynthVendor? Vendor => Read(OpenVendor);
    public bool VendorBuy(IReadOnlyList<(uint ObjectId, int Amount)> items, out string? notice)
    {
        notice = VendorNotice;
        return Act($"buy {string.Join(",", items.Select(i => $"{i.ObjectId:X8}x{i.Amount}"))}");
    }
    public bool VendorSell(IReadOnlyList<uint> itemIds, out string? notice)
    {
        notice = VendorNotice;
        return Act($"sell {string.Join(",", itemIds.Select(id => id.ToString("X8")))}");
    }
    public RynthTradeState? TradeState { get; set; }
    public RynthTradeState? Trade => Read(TradeState);
    public bool TradeOpen(uint partnerId) => Act($"trade open {partnerId:X8}");
    public bool TradeAdd(uint itemId) => Act($"trade add {itemId:X8}");
    public bool TradeAccept() => Act("trade accept");
    public bool TradeDecline() => Act("trade decline");
    public bool TradeReset() => Act("trade reset");
    public bool TradeClose() => Act("trade close");
    public event Action<RynthVendorOutcome>? VendorTransactionCompleted;
    public void CompleteVendorTransaction(bool success, string? notice = null) =>
        VendorTransactionCompleted?.Invoke(new RynthVendorOutcome(success, notice));
}
