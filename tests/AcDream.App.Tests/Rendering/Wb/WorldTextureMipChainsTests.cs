using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using AcDream.App.Rendering.Wb;
using AcDream.Content;
using AcDream.Core.Rendering.Wb;

namespace AcDream.App.Tests.Rendering.Wb;

public sealed class WorldTextureMipChainsTests
{
    private static byte[] Rgba(int width, int height, int seed = 7)
    {
        var pixels = new byte[width * height * 4];
        var random = new Random(seed);
        random.NextBytes(pixels);
        return pixels;
    }

    [Fact]
    public void ABlockCompressedChainIsTheCodecsChain()
    {
        byte[] level0 = BlockCompressionCodec.EncodeLevel(
            GpuTextureFormat.Bc1Unorm,
            Rgba(64, 64),
            64,
            64);

        byte[][] chain = WorldTextureMipChains.Build(TextureFormat.DXT1, level0, 64, 64);

        IReadOnlyList<BlockCompressionMipChain.Level> expected =
            BlockCompressionMipChain.BuildCompressed(
                GpuTextureFormat.Bc1Unorm,
                level0,
                64,
                64,
                7);
        Assert.Equal(6, chain.Length);
        for (int i = 0; i < chain.Length; i++)
            Assert.Equal(expected[i].Data, chain[i]);
    }

    [Fact]
    public void AnRgbaChainHalvesEachLevelWithTheBoxFilter()
    {
        byte[] level0 = Rgba(8, 4);

        byte[][] chain = WorldTextureMipChains.Build(TextureFormat.RGBA8, level0, 8, 4);

        // 8x4 is four levels: 4x2, 2x1, 1x1 below level 0.
        Assert.Equal([4 * 2 * 4, 2 * 1 * 4, 1 * 1 * 4], chain.Select(level => level.Length));
        (byte[] level1, _, _) = BlockCompressionMipChain.Downsample(level0, 8, 4);
        Assert.Equal(level1, chain[0]);
        Assert.Equal(level0, Rgba(8, 4));
    }

    [Fact]
    public void PrepareBuildsOneChainPerPayloadIncludingCellGeometry()
    {
        byte[] shared = Rgba(16, 16);
        byte[] cellPayload = Rgba(16, 16, seed: 9);
        var cell = Mesh((16, 16, TextureFormat.RGBA8), Batch(cellPayload));
        var mesh = Mesh(
            (16, 16, TextureFormat.RGBA8),
            Batch(shared),
            Batch(shared));
        mesh.EnvCellGeometry = cell;
        var other = Mesh((16, 16, TextureFormat.RGBA8), Batch(shared));
        var chains = new WorldTextureMipChains();

        chains.Prepare(mesh);
        byte[][]? first = chains.Find(shared);
        chains.Prepare(other);

        Assert.NotNull(first);
        Assert.Same(first, chains.Find(shared));
        Assert.Equal(4, first!.Length);
        Assert.NotNull(chains.Find(cellPayload));
    }

    [Fact]
    public void PrepareSkipsWhatTheUploadMustJudge()
    {
        byte[] misSized = new byte[10];
        byte[] unused = Rgba(16, 16);
        byte[] unsupported = new byte[16 * 16];
        var unusedBatch = Batch(unused);
        unusedBatch.Indices.Clear();
        var mesh = Mesh((16, 16, TextureFormat.RGBA8), Batch(misSized), unusedBatch);
        mesh.TextureBatches[(16, 16, TextureFormat.A8)] = [Batch(unsupported)];
        var chains = new WorldTextureMipChains();

        chains.Prepare(mesh);

        Assert.Null(chains.Find(misSized));
        Assert.Null(chains.Find(unused));
        Assert.Null(chains.Find(unsupported));
    }

    private static TextureBatchData Batch(byte[] payload) => new()
    {
        TextureData = payload,
        Indices = [0, 1, 2],
    };

    private static ObjectMeshData Mesh(
        (int Width, int Height, TextureFormat Format) format,
        params TextureBatchData[] batches)
    {
        var mesh = new ObjectMeshData
        {
            Vertices = new VertexPositionNormalTexture[3],
        };
        mesh.TextureBatches[format] = [.. batches];
        return mesh;
    }
}
