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
}
