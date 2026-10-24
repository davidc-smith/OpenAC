using System.Collections.Immutable;
using System.Numerics;
using AcDream.Content;
using AcDream.Core.Physics;
using AcDream.Core.World;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Types;

namespace AcDream.Runtime.Tests.World;

/// <summary>
/// How a landblock's static objects get their collision, now that both clients
/// build it the one way the session's collision neighborhood does. These pin
/// the rules the graphical client's own static publication used to carry.
/// </summary>
public sealed class StaticCollisionPublicationTests
{
    private const uint LandblockId = 0xA9B4FFFFu;
    private const uint SetupId = 0x02000042u;
    private const uint SecondSetupId = 0x02000043u;

    [Fact]
    public void APhysicsBspSuppressesTheSetupCylinderFallback()
    {
        const uint gfxObjId = 0x01000077u;
        var cache = new PhysicsDataCache();
        CacheCylinderSetup(cache);
        cache.RegisterGfxObjForTest(gfxObjId, BspGfx(radius: 1.25f));
        var entity = new WorldEntity
        {
            Id = 0x80A9B401u,
            SourceGfxObjOrSetupId = SetupId,
            Position = new Vector3(12f, 12f, 0f),
            Rotation = Quaternion.Identity,
            MeshRefs = [new MeshRef(gfxObjId, Matrix4x4.Identity)],
        };
        var engine = new PhysicsEngine();

        LandblockPhysicsContentBuilder.StaticCollisionPublication publication =
            Publish(engine, cache, entity);

        ShadowEntry entry = Assert.Single(engine.ShadowObjects.AllEntriesForDebug());
        Assert.Equal(ShadowCollisionType.BSP, entry.CollisionType);
        Assert.Equal(1, publication.BspOwnerCount);
        Assert.Equal(0, publication.SetupOwnerCount);
    }

    [Fact]
    public void ABuildingShellNeverRegistersSetupCollision()
    {
        var cache = new PhysicsDataCache();
        CacheCylinderSetup(cache);
        var shell = new WorldEntity
        {
            Id = 0xC0A9B401u,
            SourceGfxObjOrSetupId = SetupId,
            Position = new Vector3(12f, 12f, 0f),
            Rotation = Quaternion.Identity,
            MeshRefs = Array.Empty<MeshRef>(),
            IsBuildingShell = true,
        };
        var engine = new PhysicsEngine();

        _ = Publish(engine, cache, shell);

        Assert.Equal(0, engine.ShadowObjects.RetainedRegistrationCount);
        Assert.Empty(engine.ShadowObjects.AllEntriesForDebug());
    }

    [Fact]
    public void EverySetupShapeOfOneObjectSharesOneOwner()
    {
        var cache = new PhysicsDataCache();
        CacheCylinderSetup(cache, SetupId, shapeCount: 5);
        CacheCylinderSetup(cache, SecondSetupId);
        const uint firstId = 0x80A9B401u;
        const uint secondId = 0xC0A9B401u;
        var engine = new PhysicsEngine();

        _ = Publish(
            engine,
            cache,
            CylinderEntity(firstId, new Vector3(12f, 12f, 0f), SetupId),
            CylinderEntity(secondId, new Vector3(36f, 12f, 0f), SecondSetupId));

        Assert.Equal(2, engine.ShadowObjects.RetainedRegistrationCount);
        ShadowEntry[] entries = engine.ShadowObjects.AllEntriesForDebug().ToArray();
        Assert.Equal(6, entries.Length);
        Assert.Equal(
            [firstId, secondId],
            entries.Select(entry => entry.EntityId).Distinct().Order().ToArray());
    }

    private static LandblockPhysicsContentBuilder.StaticCollisionPublication Publish(
        PhysicsEngine engine,
        PhysicsDataCache cache,
        params WorldEntity[] entities)
    {
        var landblock = new LoadedLandblock(
            LandblockId,
            new LandBlock { Terrain = new TerrainInfo[81], Height = new byte[81] },
            entities);
        return LandblockPhysicsContentBuilder.PublishStaticCollision(
            engine,
            cache,
            landblock,
            LandblockCollisionBuild.Empty,
            origin: Vector3.Zero);
    }

    private static WorldEntity CylinderEntity(uint id, Vector3 position, uint setupId) => new()
    {
        Id = id,
        SourceGfxObjOrSetupId = setupId,
        Position = position,
        Rotation = Quaternion.Identity,
        MeshRefs = Array.Empty<MeshRef>(),
    };

    private static void CacheCylinderSetup(
        PhysicsDataCache cache,
        uint setupId = SetupId,
        int shapeCount = 1)
    {
        var setup = new Setup();
        for (int index = 0; index < shapeCount; index++)
        {
            setup.CylSpheres.Add(new CylSphere
            {
                Radius = 0.4f + index * 0.01f,
                Height = 1.2f,
                Origin = new Vector3(index * 0.1f, 0f, 0.6f),
            });
        }
        cache.CacheSetup(setupId, FlatCollisionAssetBuilder.FlattenSetup(setup));
    }

    private static GfxObjPhysics BspGfx(float radius) => new()
    {
        BSP = new PhysicsBSPTree
        {
            Root = new PhysicsBSPNode
            {
                Type = DatReaderWriter.Enums.BSPNodeType.Leaf,
            },
        },
        BoundingSphere = new Sphere
        {
            Origin = Vector3.Zero,
            Radius = radius,
        },
        PhysicsPolygons = new Dictionary<ushort, Polygon>(),
        Vertices = new VertexArray(),
        Resolved = new Dictionary<ushort, ResolvedPolygon>(),
    };
}
