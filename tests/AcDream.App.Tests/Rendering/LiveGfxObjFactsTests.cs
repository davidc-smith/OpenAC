using System.Numerics;
using AcDream.App.Rendering;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.Rendering;

public sealed class LiveGfxObjFactsTests
{
    private const uint Body = 0x01F5_0B01u;
    private const uint BodyClose = 0x01F5_0B02u;
    private const uint Degrade = 0x1100_0001u;
    private const uint Missing = 0x01F5_0B99u;
    private const uint SurfaceId = 0x0800_0001u;

    [Fact]
    public void OnlyTheFirstArrivalReadsItsPartModels()
    {
        var files = new Files();
        LiveGfxObjFacts facts = files.Facts();
        int readsAfterFirst = 0, degradesAfterFirst = 0, surfacesAfterFirst = 0;

        for (int arrival = 0; arrival < 3; arrival++)
        {
            Assert.True(facts.TryGetPart(Body, out (Vector3 Min, Vector3 Max)? bounds));
            Assert.Equal((new Vector3(-1, 0, 0), new Vector3(2, 3, 4)), bounds);
            Assert.True(facts.TryResolveClose(Body, out uint close));
            Assert.Equal(BodyClose, close);
            Assert.Equal([SurfaceId], facts.Surfaces(Body));
            Assert.Equal(0x0500_0007u, facts.OriginalTexture(SurfaceId));
            Assert.False(facts.TryGetPart(Missing, out _));
            Assert.False(facts.TryResolveClose(Missing, out uint missingClose));
            Assert.Equal(Missing, missingClose);
            if (arrival == 0)
            {
                readsAfterFirst = files.Reads.Values.Sum();
                degradesAfterFirst = files.DegradeReads;
                surfacesAfterFirst = files.SurfaceReads;
            }
        }

        Assert.Equal(readsAfterFirst, files.Reads.Values.Sum());
        Assert.Equal(degradesAfterFirst, files.DegradeReads);
        Assert.Equal(surfacesAfterFirst, files.SurfaceReads);
        Assert.Equal(1, files.DegradeReads);
        Assert.Equal(1, files.SurfaceReads);
    }

    [Fact]
    public void CollisionIsPublishedForEveryModelThePhysicsCacheDoesNotHold()
    {
        var files = new Files();
        LiveGfxObjFacts facts = files.Facts();

        facts.TryGetPart(Body, out _);
        facts.TryResolveClose(Body, out _);
        Assert.Equal([Body, BodyClose], files.Published);

        // The physics cache lost them: the next arrival publishes them again.
        files.Cached.Clear();
        facts.TryGetPart(Body, out _);
        facts.TryResolveClose(Body, out _);
        Assert.Equal([Body, BodyClose, Body, BodyClose], files.Published);

        // Held: nothing is read or published.
        int reads = files.Reads.Values.Sum();
        facts.TryGetPart(Body, out _);
        facts.Surfaces(Body);
        facts.TryResolveClose(Body, out _);
        Assert.Equal([Body, BodyClose, Body, BodyClose], files.Published);
        Assert.Equal(reads, files.Reads.Values.Sum());
    }

    private sealed class Files
    {
        public Dictionary<uint, int> Reads { get; } = [];
        public int DegradeReads { get; private set; }
        public int SurfaceReads { get; private set; }
        public HashSet<uint> Cached { get; } = [];
        public List<uint> Published { get; } = [];

        public LiveGfxObjFacts Facts() => new(
            Read,
            id =>
            {
                DegradeReads++;
                return id == Degrade
                    ? new GfxObjDegradeInfo
                    {
                        Id = Degrade,
                        Degrades = { new GfxObjInfo { Id = BodyClose } },
                    }
                    : null;
            },
            id =>
            {
                SurfaceReads++;
                return id == SurfaceId
                    ? new Surface { OrigTextureId = 0x0500_0007u }
                    : null;
            },
            Cached.Contains,
            (id, model) =>
            {
                Assert.Equal(id, model.Id);
                Published.Add(id);
                Cached.Add(id);
            });

        private GfxObj? Read(uint id)
        {
            Reads[id] = Reads.GetValueOrDefault(id) + 1;
            return id switch
            {
                Body => new GfxObj
                {
                    Id = Body,
                    Flags = GfxObjFlags.HasDIDDegrade,
                    DIDDegrade = Degrade,
                    Surfaces = { SurfaceId },
                    VertexArray = new VertexArray
                    {
                        Vertices =
                        {
                            [0] = new SWVertex { Origin = new Vector3(-1, 0, 4) },
                            [1] = new SWVertex { Origin = new Vector3(2, 3, 0) },
                        },
                    },
                },
                BodyClose => new GfxObj { Id = BodyClose, VertexArray = new VertexArray() },
                _ => null,
            };
        }
    }
}
