using System.Collections.Frozen;
using System.Diagnostics;
using System.Numerics;
using AcDream.ClientExtensions;
using AcDream.Core.Physics.Motion;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;

namespace AcDream.Rynth.Host;

/// <summary>
/// Builds <see cref="RynthWorldSnapshot"/>s on the frame thread from the live view: small parts
/// every time, big parts only when their revision moved, the rest shared with the last one.
/// Dispose unhooks the object mirror from the object table.
/// </summary>
public sealed class RynthSnapshotBuilder(IRynthGameView live, GameRuntime runtime, IClientScreen? screen) : IDisposable
{
    // RynthCore's id ranges: skills, primary attributes, max-vital stat ids.
    private const uint MaxSkillId = 64;
    private static readonly uint[] AttributeIds = [1, 2, 3, 4, 5, 6];
    private static readonly uint[] VitalStatIds = [1, 3, 5];
    private static readonly long SpellbookRefreshTicks = Stopwatch.Frequency;

    private readonly RynthObjectMirror _objects = new(live, runtime);
    private RynthWorldSnapshot? _last;
    private long _sequence;
    private (long Character, long Description, uint Player) _characterKey = (-1, -1, 0);
    private (long Revision, uint Player) _spellbookKey = (-1, 0);
    private long _spellbookAt;
    private (uint Generation, ulong Mutation) _vendorKey = (uint.MaxValue, ulong.MaxValue);

    /// <summary>Set by the host: the vendor list generation (RynthVendorTrades.Generation).</summary>
    public Func<uint> RynthVendorGeneration { get; set; } = () => 0;

    public RynthWorldSnapshot Build()
    {
        long now = Stopwatch.GetTimestamp();
        uint player = live.PlayerId;
        bool hasVitals = live.TryGetPlayerVitals(out RynthVitals vitals);
        bool hasHeading = live.TryGetPlayerHeading(out float heading);
        bool pending = live.TryGetPendingItemRequest(out uint acType, out uint pendingId);
        var snapshot = new RynthWorldSnapshot
        {
            Sequence = ++_sequence,
            PublishedAt = now,
            PlayerId = player,
            SelectedObjectId = live.SelectedObjectId,
            AccountName = live.AccountName,
            WorldName = live.WorldName,
            IsPortaling = live.IsPortaling,
            Dead = hasVitals && vitals.Health == 0,
            HasHeading = hasHeading,
            Heading = heading,
            BusyCount = live.BusyCount,
            HasPendingItemRequest = pending,
            PendingAcType = acType,
            PendingObjectId = pendingId,
            CombatMode = live.CombatMode,
            IsAttackInProgress = live.IsAttackInProgress,
            IsCastGestureActive = live.IsCastGestureActive,
            AutoRunActive = live.AutoRunActive,
            MovementIntent = live.CurrentMovementIntent,
            OpenContainerId = live.OpenContainerId,
            ServerReplies = live.ServerReplies,
            ServerTime = live.ServerTime,
            Entities = BuildEntities(),
            Character = BuildCharacter(player),
            Spellbook = BuildSpellbook(player, now),
            Objects = _objects.Build(player),
            Vendor = BuildVendor(),
            // Small, and its items are copies: rebuilt every publish (spec 2.8 allows it).
            Trade = live.Trade,
        };
        snapshot = WithScreen(snapshot);
        _last = snapshot;
        return snapshot;
    }

    public void Dispose() => _objects.Dispose();

    // Each active entity, read as TryGetPlayerPose / TryGetObjectPosition / TryGetObjectState read it:
    // only one with a body in a cell is placed.
    private FrozenDictionary<uint, RynthEntityPose> BuildEntities()
    {
        // TryGetObjectHeading's gate, probed once: the surface answers object reads only while available.
        bool headings = live is RuntimeRynthGameView { AutomationAvailable: true };
        var poses = new Dictionary<uint, RynthEntityPose>(runtime.EntityObjects.Entities.Count);
        foreach (RuntimeEntityRecord record in runtime.EntityObjects.Entities.ActiveRecords)
        {
            uint guid = record.ServerGuid;
            live.TryGetObjectState(guid, out uint state);
            bool? motion = live.TryGetObjectMotionOn(guid, out bool on) ? on : null;
            bool hasHeading = TryGetHeading(record, headings, out float heading);
            poses[guid] = record.PhysicsBody is { } body && body.CellPosition.ObjCellId != 0
                ? new RynthEntityPose(
                    body.CellPosition.ObjCellId, body.CellPosition.Frame.Origin, body.Orientation,
                    state, motion, live.GetPalettes(guid), true, hasHeading, heading)
                : new RynthEntityPose(0, default, Quaternion.Identity, state, motion, live.GetPalettes(guid),
                    false, hasHeading, heading);
        }
        return poses.ToFrozenDictionary();
    }

    // The heading RuntimeWorldObjectProjection gives an entity, without projecting the rest of it: the
    // body's orientation (LivePosition) when there is a body, else the server position's rotation, else none.
    private static bool TryGetHeading(RuntimeEntityRecord record, bool available, out float heading)
    {
        heading = 0;
        if (!available || record.ServerGuid == 0)
            return false;
        if (record.PhysicsBody is { } body)
        {
            heading = MoveToMath.GetHeading(body.Orientation);
            return true;
        }
        if (RuntimeWorldObjectProjection.ConvertPosition(record.Snapshot.Position) is not { } position)
            return false;
        heading = MoveToMath.GetHeading(position.Frame.Orientation);
        return true;
    }

    private RynthCharacterPart? BuildCharacter(uint player)
    {
        var key = (runtime.CharacterOwner.CharacterRevision,
                   runtime.CharacterOwner.LocalPlayer.DescriptionRevision, player);
        if (_last?.Character is { } shared && key == _characterKey)
            return shared;
        _characterKey = key;
        var skills = new Dictionary<uint, (int, int, int)>();
        for (uint id = 0; id <= MaxSkillId; id++)
            if (live.TryGetSkill(player, id, out int buffed, out int baseLevel, out int training))
                skills[id] = (buffed, baseLevel, training);
        var attributes = new Dictionary<uint, (uint, uint)>();
        foreach (uint id in AttributeIds)
            if (live.TryGetAttribute(player, id, out uint buffed, out uint baseLevel))
                attributes[id] = (buffed, baseLevel);
        var vitalBases = new Dictionary<uint, uint>();
        foreach (uint id in VitalStatIds)
            if (live.TryGetVitalBase(player, id, out uint baseMaximum))
                vitalBases[id] = baseMaximum;
        bool hasVitals = live.TryGetPlayerVitals(out RynthVitals vitals);
        bool hasBase = live.TryGetPlayerBaseVitals(out uint h, out uint st, out uint m);
        return new RynthCharacterPart(hasVitals, vitals, hasBase, h, st, m,
            skills.ToFrozenDictionary(), attributes.ToFrozenDictionary(), vitalBases.ToFrozenDictionary(),
            live.PlayerProgress);
    }

    private RynthSpellbookPart? BuildSpellbook(uint player, long now)
    {
        var key = (runtime.CharacterOwner.SpellbookRevision, player);
        if (_last?.Spellbook is { } shared && key == _spellbookKey && now - _spellbookAt < SpellbookRefreshTicks)
            return shared;
        _spellbookKey = key;
        _spellbookAt = now;
        // The live view hands out fresh lists each read; copied to arrays all the same, so no caller can cast back.
        IReadOnlyList<uint>? known = live.KnownSpells?.ToArray();
        return new RynthSpellbookPart(
            live.PlayerEnchantments?.ToArray(), live.PlayerStatMods?.ToArray(), live.VitaeMultiplier, known,
            (known ?? []).ToFrozenSet(), live.IsSpellKnown(player, 0) != -1,
            // Here, not in the object part: that part is shared on idle frames, the spellbook is not.
            live.GetObjectEnchantments(player)?.ToArray());
    }

    private RynthVendor? BuildVendor()
    {
        var key = (RynthVendorGeneration(), runtime.InventoryOwner.Objects.MutationRevision);
        if (_last is { } last && key == _vendorKey)
            return last.Vendor;
        _vendorKey = key;
        return live.Vendor;
    }

    private RynthWorldSnapshot WithScreen(RynthWorldSnapshot s)
    {
        bool hasViewport = live.TryGetViewportSize(out uint w, out uint h);
        bool hasRadar = live.TryGetRadarRect(out int x0, out int y0, out int x1, out int y1);
        ClientScreenProjection? projection = null;
        if (screen is not null && screen.TryCaptureProjection(out ClientScreenProjection captured))
            projection = captured;
        return s with
        {
            Projection = projection,
            HasViewport = hasViewport, ViewportWidth = w, ViewportHeight = h,
            HasRadar = hasRadar, Radar = (x0, y0, x1, y1),
        };
    }
}
