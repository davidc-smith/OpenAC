using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.Content;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using Xunit;

namespace AcDream.App.Tests.Rendering;

public sealed class TextureCacheRenderSurfaceSamplerKeyTests
{
    private const uint RenderSurfaceId = 0x06006D59u;

    [Fact]
    public void SameId_DifferentNearest_ReturnsDistinctHandles_AndBothDisposeCleanly()
    {
        var device = new RecordingGpuDevice();
        device.Clear();
        var dats = new FakeRenderSurfaceDats();
        dats.Register(new RenderSurface
        {
            Width = 4,
            Height = 4,
            Format = PixelFormat.PFID_A8R8G8B8,
            SourceData = new byte[4 * 4 * 4],
        }, RenderSurfaceId);

        var cache = new TextureCache(device, dats);

        uint linear = cache.GetOrUploadRenderSurface(
            RenderSurfaceId, out _, out _, nearest: false);
        uint nearest = cache.GetOrUploadRenderSurface(
            RenderSurfaceId, out _, out _, nearest: true);

        Assert.NotEqual(0u, linear);
        Assert.NotEqual(0u, nearest);
        Assert.NotEqual(linear, nearest);

        Assert.Equal(
            linear,
            cache.GetOrUploadRenderSurface(RenderSurfaceId, out _, out _, nearest: false));
        Assert.Equal(
            nearest,
            cache.GetOrUploadRenderSurface(RenderSurfaceId, out _, out _, nearest: true));

        Assert.Equal(2, device.CreatedTextures.Count);

        cache.Dispose();

        Assert.All(device.CreatedTextures, texture => Assert.True(texture.IsDisposed));
    }

    [Fact]
    public void AnAlphaImageIsUploadedAsStoredAndShownAsWhiteWithThatAlpha()
    {
        var device = new RecordingGpuDevice();
        device.Clear();
        var dats = new FakeRenderSurfaceDats();
        byte[] alpha = [0, 64, 128, 255, 1, 2, 3, 4];
        dats.Register(new RenderSurface
        {
            Width = 4,
            Height = 2,
            Format = PixelFormat.PFID_A8,
            SourceData = alpha,
        }, RenderSurfaceId);
        var cache = new TextureCache(device, dats);

        uint handle = cache.GetOrUploadRenderSurface(RenderSurfaceId, out int width, out int height, nearest: true);

        Assert.NotEqual(0u, handle);
        Assert.Equal((4, 2), (width, height));
        RecordingGpuTexture texture = Assert.Single(device.CreatedTextures);
        Assert.Equal(GpuTextureFormat.R8Unorm, texture.Format);
        Assert.Equal(GpuTextureSwizzle.CoverageAsAlpha, texture.Swizzle);
        Assert.Equal(alpha, Assert.Single(texture.UploadedBytes));
        cache.Dispose();
    }

    [Fact]
    public void AColourImageIsStillDecodedToFourChannels()
    {
        var device = new RecordingGpuDevice();
        device.Clear();
        var dats = new FakeRenderSurfaceDats();
        dats.Register(new RenderSurface
        {
            Width = 2,
            Height = 1,
            Format = PixelFormat.PFID_A8R8G8B8,
            SourceData = [1, 2, 3, 4, 5, 6, 7, 8],
        }, RenderSurfaceId);
        var cache = new TextureCache(device, dats);

        cache.GetOrUploadRenderSurface(RenderSurfaceId, out _, out _, nearest: false);

        RecordingGpuTexture texture = Assert.Single(device.CreatedTextures);
        Assert.Equal(GpuTextureFormat.Rgba8Unorm, texture.Format);
        Assert.Equal(GpuTextureSwizzle.Identity, texture.Swizzle);
        Assert.Equal(new byte[] { 3, 2, 1, 4, 7, 6, 5, 8 }, Assert.Single(texture.UploadedBytes));
        cache.Dispose();
    }

    private sealed class FakeRenderSurfaceDats : IDatReaderWriter
    {
        private readonly Dictionary<uint, IDBObj> _objects = new();

        public FakeRenderSurfaceDats()
        {
            Portal = new FakeDatDatabase(_objects);
            HighRes = new FakeDatDatabase(new Dictionary<uint, IDBObj>());
        }

        public void Register<T>(T obj, uint id) where T : IDBObj => _objects[id] = obj;

        public string SourceDirectory => string.Empty;
        public IDatDatabase Portal { get; }
        public IDatDatabase Cell => throw new NotSupportedException();
        public ReadOnlyDictionary<uint, IDatDatabase> CellRegions { get; } =
            new(new Dictionary<uint, IDatDatabase>());
        public IDatDatabase HighRes { get; }
        public IDatDatabase Language => throw new NotSupportedException();
        public IDatDatabase Local => throw new NotSupportedException();
        public ReadOnlyDictionary<uint, uint> RegionFileMap { get; } =
            new(new Dictionary<uint, uint>());
        public int PortalIteration => 0;
        public int CellIteration => 0;
        public int HighResIteration => 0;
        public int LanguageIteration => 0;

        public bool TryGetFileBytes(
            uint regionId, uint fileId, ref byte[] bytes, out int bytesRead)
        {
            bytesRead = 0;
            return false;
        }

        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj =>
            Array.Empty<uint>();

        public IEnumerable<IDatReaderWriter.IdResolution> ResolveId(uint id) =>
            Array.Empty<IDatReaderWriter.IdResolution>();

        public bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj =>
            throw new NotSupportedException();

        public bool TrySave<T>(uint regionId, T obj, int iteration = 0) where T : IDBObj =>
            throw new NotSupportedException();

        [return: MaybeNull]
        public T Get<T>(uint fileId) where T : IDBObj =>
            _objects.TryGetValue(fileId, out IDBObj? obj) && obj is T typed ? typed : default;

        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj
        {
            if (_objects.TryGetValue(fileId, out IDBObj? obj) && obj is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeDatDatabase : IDatDatabase
    {
        private readonly Dictionary<uint, IDBObj> _objects;

        public FakeDatDatabase(Dictionary<uint, IDBObj> objects) => _objects = objects;

        public DatDatabase Db => throw new NotSupportedException();
        public int Iteration => 0;

        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj =>
            Array.Empty<uint>();

        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj
        {
            if (_objects.TryGetValue(fileId, out IDBObj? obj) && obj is T typed)
            {
                value = typed;
                return true;
            }
            value = default;
            return false;
        }

        public bool TryGetFileBytes(uint fileId, [MaybeNullWhen(false)] out byte[] value)
        {
            value = null;
            return false;
        }

        public bool TryGetFileBytes(uint fileId, ref byte[] bytes, out int bytesRead)
        {
            bytesRead = 0;
            return false;
        }

        public bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
