using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
using AcDream.Core.World;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.World;

namespace AcDream.App.Hosting;

/// <summary>
/// The graphical session's collision. It is the neighborhood the windowless
/// client keeps, one implementation for both: the 3x3 landblocks around the
/// character, each with its own static collision, measured in the runtime's
/// frame. The session host owns it and keeps it around the character
/// whether or not a window is open; what is drawn reads it and never builds
/// collision of its own.
/// </summary>
internal sealed class GraphicalSessionCollision
{
    /// <summary>
    /// The share of one update the collision work may take. Loading a
    /// landblock runs on a worker thread; publishing what was loaded is cut
    /// into steps, and the steps stop for the update once this is spent, so
    /// a window keeps drawing smoothly while the character crosses into new
    /// landblocks.
    /// </summary>
    internal static readonly TimeSpan FrameShare = TimeSpan.FromMilliseconds(3);

    private readonly GameRuntime _runtime;
    private readonly ContentSlot _content = new();
    private readonly Func<long> _timestamp;
    private readonly long _frameShareTicks;
    private long _frameDeadline;

    internal GraphicalSessionCollision(GameRuntime runtime)
        : this(runtime, System.Diagnostics.Stopwatch.GetTimestamp, FrameShare)
    {
    }

    internal GraphicalSessionCollision(
        GameRuntime runtime,
        Func<long> timestamp,
        TimeSpan frameShare)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));
        _frameShareTicks = checked((long)(frameShare.TotalSeconds
            * System.Diagnostics.Stopwatch.Frequency));
        Neighborhood = new RuntimeCollisionNeighborhood(
            runtime,
            _content,
            new RuntimeCollisionPacing
            {
                LoadInBackground = true,
                IsFrameSpent = () => _timestamp() > _frameDeadline,
            });
    }

    internal RuntimeCollisionNeighborhood Neighborhood { get; }

    /// <summary>Whether the game files to build collision from are bound.</summary>
    internal bool HasContent => _content.Target is not null;

    /// <summary>
    /// Lends the game files the collision is built from. Disposing the
    /// returned lease takes them back; nothing more is loaded until the next
    /// binding, and what is published stays.
    /// </summary>
    internal IDisposable BindContent(IRuntimeCollisionContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_content.Target is not null)
        {
            throw new InvalidOperationException(
                "The session's collision content is already bound.");
        }

        _content.Target = content;
        return new ContentLease(_content, content);
    }

    /// <summary>
    /// One step of the session's update: keeps the collision around the
    /// character. An arrival the server decided (login, a portal) centres
    /// it; the character walking moves it; a session with no character in
    /// the world withdraws it.
    /// </summary>
    internal void Tick()
    {
        if (!HasContent)
            return;

        _frameDeadline = _timestamp() + _frameShareTicks;

        if (_runtime.EntityObjects.Physics.WorldFrameCenterLandblockId == 0u)
        {
            if (Neighborhood.HasPublishedCollision || !Neighborhood.IsQuiescent)
                Neighborhood.Release();
            return;
        }

        if (_runtime.MovementOwner.Controller is
            {
                State: not PlayerState.PortalSpace,
                CellId: not 0u,
            } controller)
        {
            // Walking moves the window inside the frame. A frame the
            // neighborhood has not been centred in yet is an arrival.
            if (Neighborhood.FrameLandblock
                == _runtime.EntityObjects.Physics.WorldFrameCenterLandblockId)
            {
                Neighborhood.Follow(controller.CellId);
            }
            else
            {
                Neighborhood.CenterOn(controller.CellId);
            }
            return;
        }

        uint arrival = ArrivalCell();
        if (arrival != 0u)
            Neighborhood.CenterOn(arrival);
        else
            Neighborhood.Pump();
    }

    /// <summary>
    /// Whether the collision at a destination is in place: its landblock is
    /// the centre and published, and an indoor cell's structure is there.
    /// </summary>
    internal bool IsReady(uint fullCellId) =>
        HasContent && Neighborhood.IsReady(fullCellId);

    /// <summary>Whether this cell's collision is published right now.</summary>
    internal bool IsCollisionPublished(uint fullCellId) =>
        Neighborhood.IsCollisionPublished(fullCellId);

    /// <summary>
    /// Where the server last put the character and the client has not yet
    /// placed it: an accepted portal destination, or the character's own
    /// last accepted position.
    /// </summary>
    private uint ArrivalCell()
    {
        if (_runtime.TransitOwner.TryGetAcceptedTeleportDestination(
                out RuntimeTeleportDestination destination)
            && destination.CellId != 0u)
        {
            return destination.CellId;
        }

        uint player = _runtime.PlayerIdentity.ServerGuid;
        if (player != 0u
            && _runtime.EntityObjects.Entities.TryGetActive(
                player,
                out RuntimeEntityRecord record)
            && record.Snapshot.Position is { LandblockId: not 0u } position)
        {
            return position.LandblockId;
        }

        return 0u;
    }

    private sealed class ContentSlot : IRuntimeCollisionContent
    {
        internal IRuntimeCollisionContent? Target { get; set; }

        private IRuntimeCollisionContent Bound =>
            Target ?? throw new InvalidOperationException(
                "The session's collision has no content bound.");

        public IDatReaderWriter Dats => Bound.Dats;

        public object DatLock => Bound.DatLock;

        public IPreparedCollisionSource PreparedCollision =>
            Bound.PreparedCollision;

        public ImmutableArray<float> HeightTable => Bound.HeightTable;

        public bool TryLoadCollisionLandblock(
            uint landblockId,
            Vector3 origin,
            out LoadedLandblock landblock,
            out LandblockCollisionBuild collisions) =>
            Bound.TryLoadCollisionLandblock(
                landblockId,
                origin,
                out landblock,
                out collisions);
    }

    private sealed class ContentLease(
        ContentSlot slot,
        IRuntimeCollisionContent content) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (ReferenceEquals(slot.Target, content))
                slot.Target = null;
        }
    }
}

/// <summary>
/// The game files a window's composition opened, lent to the session's
/// collision. A landblock is loaded on a worker thread, so every read takes
/// the files' lock one record at a time, as the landblocks a window streams
/// to draw are read: the update thread never waits for a whole landblock.
/// </summary>
internal sealed class GraphicalCollisionContent : IRuntimeCollisionContent
{
    private readonly AcDream.App.Streaming.LockedContentReads _reads;

    internal GraphicalCollisionContent(
        IDatReaderWriter dats,
        object datLock,
        IPreparedCollisionSource preparedCollision,
        ImmutableArray<float> heightTable)
    {
        Dats = dats ?? throw new ArgumentNullException(nameof(dats));
        DatLock = datLock ?? throw new ArgumentNullException(nameof(datLock));
        PreparedCollision = preparedCollision
            ?? throw new ArgumentNullException(nameof(preparedCollision));
        HeightTable = heightTable;
        _reads = new AcDream.App.Streaming.LockedContentReads(dats, datLock);
    }

    public IDatReaderWriter Dats { get; }

    public object DatLock { get; }

    public IPreparedCollisionSource PreparedCollision { get; }

    public ImmutableArray<float> HeightTable { get; }

    public bool TryLoadCollisionLandblock(
        uint landblockId,
        Vector3 origin,
        out LoadedLandblock landblock,
        out LandblockCollisionBuild collisions) =>
        LandblockPhysicsContentBuilder.TryLoadCollisionLandblock(
            _reads,
            PreparedCollision,
            HeightTable.AsSpan(),
            landblockId,
            origin,
            out landblock,
            out collisions);
}
