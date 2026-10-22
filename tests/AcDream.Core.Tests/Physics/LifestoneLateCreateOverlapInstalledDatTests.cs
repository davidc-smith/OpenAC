using System.Collections.Generic;
using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Core.Tests.Conformance;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Options;
using Xunit.Abstractions;

namespace AcDream.Core.Tests.Physics;

// A recall into a landblock the client has not loaded places the player before
// the server's CreateObject for the lifestone it arrives on. The lifestone then
// lands on the player, who can no longer move. The lifestone keeps the position
// the server gave it; a fresh placement of the player is what gets it out.
[Trait("Lane", "InstalledDat")]
public sealed class LifestoneLateCreateOverlapInstalledDatTests
{
    private const uint Landblock = 0xA9B40000u;
    private const uint Cell = Landblock | 0x0001u;
    private const uint PlayerId = 0x50000001u;
    private const uint LifestoneId = 0x7AA0C34Fu;
    private const uint LifestoneSetup = 0x020002EEu;
    private const uint HumanSetup = 0x02000001u;
    private const uint LifestoneState = 0x410u; // ACE lifestone: Gravity | IgnoreCollisions

    private static readonly Vector3 Spot = new(20f, 20f, 0f);
    private static readonly Quaternion Heading = Quaternion.Identity;

    private readonly ITestOutputHelper _out;

    public LifestoneLateCreateOverlapInstalledDatTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void LifestoneArrivingOnThePlacedPlayer_TrapsIt()
    {
        using Scene scene = Scene.Load();
        (PhysicsEngine engine, PhysicsDataCache cache) = NewEngine(scene);

        PhysicsSetPositionResult placed = PlacePlayer(engine, scene, Spot);
        Assert.True(placed.IsCommitted);
        Assert.True(Vector3.Distance(placed.Position, Spot) < 0.05f);

        RegisterLifestone(engine, cache, scene, Spot);

        foreach (Vector3 step in new[] { new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0f, -0.5f, 0f) })
        {
            ResolveResult move = TryWalk(engine, scene, placed.Position, step);
            _out.WriteLine($"walk {step}: ok={move.Ok} pos={move.Position} obj=0x{move.LastCollidedObjectId:X8}");
            Assert.True(Vector3.Distance(move.Position, placed.Position) < 0.01f);
        }
    }

    [Fact]
    public void PlacingThePlayerAgain_MovesItOffTheLifestone()
    {
        using Scene scene = Scene.Load();
        (PhysicsEngine engine, PhysicsDataCache cache) = NewEngine(scene);
        PhysicsSetPositionResult placed = PlacePlayer(engine, scene, Spot);
        RegisterLifestone(engine, cache, scene, Spot);

        PhysicsSetPositionResult replaced = engine.SetPosition(new PhysicsSetPositionRequest(
            placed.Position, Heading, Cell, placed.Position, scene.Human.Spheres,
            Scale: 1f,
            StepUpHeight: scene.Human.StepUpHeight,
            StepDownHeight: scene.Human.StepDownHeight,
            MoverFlags: ObjectInfoState.IsPlayer | ObjectInfoState.EdgeSlide,
            MovingEntityId: PlayerId,
            Flags: PhysicsSetPositionFlags.Placement | PhysicsSetPositionFlags.Slide));
        _out.WriteLine($"replaced: err={replaced.Error} pos={replaced.Position} moved={Vector3.Distance(replaced.Position, Spot):F3}");

        Assert.True(replaced.IsCommitted);
        Assert.True(Vector3.Distance(replaced.Position, Spot) > 2f);
        ResolveResult away = TryWalk(engine, scene, replaced.Position,
            Vector3.Normalize(replaced.Position - Spot) * 0.5f);
        Assert.True(away.Ok && Vector3.Distance(away.Position, replaced.Position) > 0.4f);
    }

    [Fact]
    public void LifestoneArrivingOnAPlayersShadow_StaysWhereTheServerPutIt()
    {
        using Scene scene = Scene.Load();
        (PhysicsEngine engine, PhysicsDataCache cache) = NewEngine(scene);
        engine.ShadowObjects.RegisterMultiPart(
            PlayerId, Spot, Heading,
            ShadowShapeBuilder.FromSetup(scene.HumanSetup, 1f, _ => false),
            state: (uint)PhysicsStateFlags.Gravity,
            flags: EntityCollisionFlags.HasWeenie | EntityCollisionFlags.IsPlayer,
            0f, 0f, Landblock, seedCellId: Cell, isStatic: false);
        RegisterLifestone(engine, cache, scene, Spot);

        PhysicsSetPositionResult stone = engine.SetPosition(new PhysicsSetPositionRequest(
            Spot, Heading, Cell, Spot, scene.Lifestone.Spheres, 1f, 0f, 0f,
            MoverPhysicsState: (PhysicsStateFlags)LifestoneState,
            MovingEntityId: LifestoneId,
            Flags: PhysicsSetPositionFlags.Placement | PhysicsSetPositionFlags.Slide,
            PassThroughPlayers: true));
        _out.WriteLine($"stone: err={stone.Error} pos={stone.Position}");

        Assert.True(stone.IsCommitted);
        Assert.True(Vector3.Distance(stone.Position, Spot) < 0.01f);
    }

    [Fact]
    public void LifestoneKnownBeforeThePlacement_PlacesThePlayerBesideIt()
    {
        using Scene scene = Scene.Load();
        (PhysicsEngine engine, PhysicsDataCache cache) = NewEngine(scene);
        RegisterLifestone(engine, cache, scene, Spot);

        PhysicsSetPositionResult placed = PlacePlayer(engine, scene, Spot);

        Assert.True(placed.IsCommitted);
        Assert.True(Vector3.Distance(placed.Position, Spot) > 2f);
    }

    private sealed class Scene : IDisposable
    {
        private readonly DatCollection _dats;

        private Scene(DatCollection dats)
        {
            _dats = dats;
            LifestoneSetup = dats.Get<Setup>(LifestoneSetupId)!;
            HumanSetup = dats.Get<Setup>(HumanSetupId)!;
            Lifestone = FlatCollisionAssetBuilder.FlattenSetup(LifestoneSetup);
            Human = FlatCollisionAssetBuilder.FlattenSetup(HumanSetup);
        }

        private const uint LifestoneSetupId = LifestoneLateCreateOverlapInstalledDatTests.LifestoneSetup;
        private const uint HumanSetupId = LifestoneLateCreateOverlapInstalledDatTests.HumanSetup;

        public Setup LifestoneSetup { get; }
        public Setup HumanSetup { get; }
        public FlatSetupCollision Lifestone { get; }
        public FlatSetupCollision Human { get; }

        public GfxObj GfxObj(uint id) => _dats.Get<GfxObj>(id)!;

        public static Scene Load()
        {
            string? datDir = ConformanceDats.ResolveDatDir();
            Assert.NotNull(datDir);
            return new Scene(new DatCollection(new DatCollectionOptions
            {
                DatDirectory = datDir!,
                AccessType = DatAccessType.Read,
            }));
        }

        public void Dispose() => _dats.Dispose();
    }

    private static (PhysicsEngine, PhysicsDataCache) NewEngine(Scene scene)
    {
        var cache = PhysicsDataCache.CreateProduction();
        var engine = new PhysicsEngine { DataCache = cache };
        engine.AddLandblock(
            Landblock,
            new TerrainSurface(new byte[81], new float[256]),
            Array.Empty<CellSurface>(),
            Array.Empty<PortalPlane>(),
            0f,
            0f);
        foreach (uint part in scene.LifestoneSetup.Parts)
        {
            GfxObj gfx = scene.GfxObj(part);
            cache.CacheGfxObj(part, gfx, FlatCollisionAssetBuilder.FlattenGfxObj(gfx));
        }
        return (engine, cache);
    }

    private static void RegisterLifestone(
        PhysicsEngine engine, PhysicsDataCache cache, Scene scene, Vector3 at)
    {
        ShadowPartGeometry? Bounds(uint id)
        {
            FlatGfxObjCollisionAsset? asset = cache.GetFlatGfxObj(id);
            FlatPhysicsBsp? flat = asset?.PhysicsBsp;
            return flat is { RootIndex: >= 0 }
                ? ShadowPartGeometry.Create(flat.Nodes[flat.RootIndex].BoundingSphere, asset!.VisualBounds)
                : null;
        }
        IReadOnlyList<ShadowShape> shapes = ShadowShapeBuilder.FromSetup(
            scene.LifestoneSetup, 1f, id => Bounds(id) is not null, physicsBspBounds: Bounds);
        engine.ShadowObjects.RegisterMultiPart(
            LifestoneId, at, Heading, shapes,
            state: LifestoneState,
            flags: EntityCollisionFlags.HasWeenie,
            0f, 0f, Landblock,
            seedCellId: Cell,
            isStatic: false);
    }

    private static PhysicsSetPositionResult PlacePlayer(
        PhysicsEngine engine, Scene scene, Vector3 at)
        => engine.SetPosition(new PhysicsSetPositionRequest(
            at, Heading, Cell, at, scene.Human.Spheres,
            Scale: 1f,
            StepUpHeight: scene.Human.StepUpHeight,
            StepDownHeight: scene.Human.StepDownHeight,
            MoverFlags: ObjectInfoState.IsPlayer | ObjectInfoState.EdgeSlide,
            MovingEntityId: PlayerId,
            Flags: PhysicsSetPositionFlags.Teleport | PhysicsSetPositionFlags.Slide
                | PhysicsSetPositionFlags.SendPositionEvent));

    private static ResolveResult TryWalk(
        PhysicsEngine engine, Scene scene, Vector3 from, Vector3 delta)
    {
        var body = new PhysicsBody
        {
            State = PhysicsStateFlags.Gravity,
        };
        body.ContactPlaneValid = true;
        body.ContactPlane = new Plane(Vector3.UnitZ, -from.Z);
        body.ContactPlaneCellId = Cell;
        body.TransientState |= TransientStateFlags.Contact | TransientStateFlags.OnWalkable;
        return engine.ResolveWithTransition(
            from, from + delta, Cell,
            sphereRadius: 0.48f, sphereHeight: 1.835f,
            stepUpHeight: scene.Human.StepUpHeight,
            stepDownHeight: scene.Human.StepDownHeight,
            isOnGround: true,
            body: body,
            moverFlags: ObjectInfoState.IsPlayer | ObjectInfoState.EdgeSlide,
            movingEntityId: PlayerId,
            sphereList: scene.Human.Spheres);
    }
}
