namespace AcDream.Rynth.Host;

/// <summary>
/// What the RynthCore table reads of one object: the client's typed fields
/// (from the object's public description) and its property tables.
/// </summary>
public sealed record RynthObjectInfo
{
    public required uint ContainerId { get; init; }
    public required uint WielderId { get; init; }
    public required uint EquipLocation { get; init; }
    public required uint ItemType { get; init; }
    public required uint Wcid { get; init; }
    public required uint Bitfield { get; init; }
    public required uint IconId { get; init; }
    public required uint IconUnderlayId { get; init; }
    public required uint IconOverlayId { get; init; }
    public required uint? SpellId { get; init; }
    public required uint? HouseOwnerId { get; init; }
    public required int StackSize { get; init; }
    public required int StackSizeMax { get; init; }
    public required int Value { get; init; }
    public required int Burden { get; init; }
    public required int ItemsCapacity { get; init; }
    public required int Structure { get; init; }
    public required int MaxStructure { get; init; }
    public required float Workmanship { get; init; }
    public required uint? MaterialType { get; init; }

    /// <summary>An identify of this object came back this session.</summary>
    public required bool Identified { get; init; }

    public required IReadOnlyList<uint> AppraisedSpellIds { get; init; }
    public required IReadOnlyDictionary<uint, int> Ints { get; init; }
    public required IReadOnlyDictionary<uint, uint> DataIds { get; init; }
    public required IReadOnlyDictionary<uint, uint> InstanceIds { get; init; }

    public IReadOnlyDictionary<uint, bool> Bools { get; init; } = new Dictionary<uint, bool>();
    public IReadOnlyDictionary<uint, double> Floats { get; init; } = new Dictionary<uint, double>();
    public IReadOnlyDictionary<uint, string> Strings { get; init; } = new Dictionary<uint, string>();
}
