using System;
using System.Collections.Generic;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;

namespace AcDream.App.Tests.Rendering.Gpu.Vk;

/// <summary>
/// A block-compressed chain decodes level 0 and filters every level below it
/// through working images that are dropped once the level is encoded again.
/// Those images are reused between textures; the chain must come out exactly
/// as if each had been a fresh array.
/// </summary>
public sealed class BlockCompressionMipChainScratchTests
{
    private static byte[] Rgba(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 4];
        new Random(seed).NextBytes(pixels);
        return pixels;
    }

    private static List<byte[]> ReferenceChain(GpuTextureFormat format, byte[] level0, int width, int height, int levels)
    {
        var chain = new List<byte[]>();
        byte[] current = BlockCompressionCodec.DecodeLevel(format, level0, width, height);
        for (int level = 1; level < levels; level++)
        {
            (byte[] next, int nextWidth, int nextHeight) = BlockCompressionMipChain.Downsample(current, width, height);
            chain.Add(BlockCompressionCodec.EncodeLevel(format, next, nextWidth, nextHeight));
            current = next;
            width = nextWidth;
            height = nextHeight;
        }
        return chain;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AChainIsUnchangedByTheTextureBuiltBeforeIt(bool withAlpha)
    {
        GpuTextureFormat format = withAlpha ? GpuTextureFormat.Bc3Unorm : GpuTextureFormat.Bc1Unorm;
        // A larger texture first leaves its pixels in the reused images.
        byte[] large = BlockCompressionCodec.EncodeLevel(format, Rgba(128, 128, seed: 3), 128, 128);
        _ = BlockCompressionMipChain.BuildCompressed(format, large, 128, 128, 8);

        byte[] level0 = BlockCompressionCodec.EncodeLevel(format, Rgba(64, 32, seed: 5), 64, 32);
        IReadOnlyList<BlockCompressionMipChain.Level> chain =
            BlockCompressionMipChain.BuildCompressed(format, level0, 64, 32, 7);

        List<byte[]> expected = ReferenceChain(format, level0, 64, 32, 7);
        Assert.Equal(expected.Count, chain.Count);
        for (int i = 0; i < chain.Count; i++)
            Assert.Equal(expected[i], chain[i].Data);
    }

    [Fact]
    public void ACompressedChainAllocatesOnlyTheLevelsItReturns()
    {
        byte[] level0 = BlockCompressionCodec.EncodeLevel(
            GpuTextureFormat.Bc1Unorm, Rgba(256, 256, seed: 11), 256, 256);
        _ = BlockCompressionMipChain.BuildCompressed(GpuTextureFormat.Bc1Unorm, level0, 256, 256, 9);

        long before = GC.GetAllocatedBytesForCurrentThread();
        IReadOnlyList<BlockCompressionMipChain.Level> chain =
            BlockCompressionMipChain.BuildCompressed(GpuTextureFormat.Bc1Unorm, level0, 256, 256, 9);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        long kept = 0;
        foreach (BlockCompressionMipChain.Level level in chain)
            kept += level.Data.Length;
        // The decoded 256 x 256 image alone is 256 KB; the encoded levels are
        // about 11 KB. The list and the array headers fit in the margin.
        Assert.True(
            allocated <= kept + 4096,
            $"building the chain allocated {allocated} bytes for {kept} bytes of levels");
    }
}
