using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Core.Items;
using AcDream.Plugin.Abstractions;

namespace AcDream.Rynth.Host;

/// <summary>One entity's pose and state, as the live view reads them.</summary>
public readonly record struct RynthEntityPose(
    uint CellId, Vector3 Origin, Quaternion Orientation, uint RawState, bool? MotionOn,
    IReadOnlyList<(uint SubPaletteId, uint Offset)>? Palettes);

/// <summary>The player's character: refreshed when the character revision moves.</summary>
public sealed record RynthCharacterPart(
    bool HasVitals, RynthVitals Vitals,
    bool HasBaseVitals, uint BaseHealth, uint BaseStamina, uint BaseMana,
    FrozenDictionary<uint, (int Buffed, int Base, int Training)> Skills,
    FrozenDictionary<uint, (uint Buffed, uint Base)> Attributes,
    FrozenDictionary<uint, uint> VitalBases,
    RynthPlayerProgress? Progress);

/// <summary>The player's spellbook: refreshed when its revision moves, or each second.</summary>
public sealed record RynthSpellbookPart(
    IReadOnlyList<RynthEnchantment>? Enchantments,
    IReadOnlyList<RynthStatMod>? StatMods,
    float VitaeMultiplier,
    IReadOnlyList<uint>? KnownSpells,
    FrozenSet<uint> KnownSet,
    bool SpellsAvailable);

/// <summary>Everything the pump reads in one pass. Immutable; published by the frame thread.</summary>
public sealed record RynthWorldSnapshot
{
    public long Sequence { get; init; }
    /// <summary><see cref="Stopwatch.GetTimestamp"/> at publish.</summary>
    public long PublishedAt { get; init; }

    public uint PlayerId { get; init; }
    public uint SelectedObjectId { get; init; }
    public string AccountName { get; init; } = "";
    public string WorldName { get; init; } = "";
    public bool IsPortaling { get; init; }
    public bool Dead { get; init; }
    public bool HasHeading { get; init; }
    public float Heading { get; init; }
    public int BusyCount { get; init; }
    public bool HasPendingItemRequest { get; init; }
    public uint PendingAcType { get; init; }
    public uint PendingObjectId { get; init; }
    public int CombatMode { get; init; }
    public bool IsAttackInProgress { get; init; }
    public bool IsCastGestureActive { get; init; }
    public bool AutoRunActive { get; init; }
    public PluginMovementIntent? MovementIntent { get; init; }
    public uint OpenContainerId { get; init; }
    public ServerReplySnapshot ServerReplies { get; init; }
    public double ServerTime { get; init; }

    public FrozenDictionary<uint, RynthEntityPose> Entities { get; init; } = FrozenDictionary<uint, RynthEntityPose>.Empty;
    public RynthCharacterPart? Character { get; init; }
    public RynthSpellbookPart? Spellbook { get; init; }
    public RynthObjectsPart Objects { get; init; } = RynthObjectsPart.Empty;   // Task 4
    public RynthVendor? Vendor { get; init; }
    public RynthTradeState? Trade { get; init; }

    public ClientScreenProjection? Projection { get; init; }
    public bool HasViewport { get; init; }
    public uint ViewportWidth { get; init; }
    public uint ViewportHeight { get; init; }
    public bool HasRadar { get; init; }
    public (int X0, int Y0, int X1, int Y1) Radar { get; init; }

    /// <summary>Server time at <paramref name="timestamp"/>: the published time plus the time since.</summary>
    public double ServerTimeAt(long timestamp) =>
        ServerTime + (timestamp - PublishedAt) / (double)Stopwatch.Frequency;
}
