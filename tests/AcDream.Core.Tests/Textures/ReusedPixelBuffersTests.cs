using AcDream.Core.Textures;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace AcDream.Core.Tests.Textures;

public sealed class ReusedPixelBuffersTests
{
    [Fact]
    public void ALengthIsHandedTheSameArrayAgain()
    {
        var buffers = new ReusedPixelBuffers(1024);

        byte[] first = buffers.Get(64);

        Assert.Equal(64, first.Length);
        Assert.Same(first, buffers.Get(64));
        Assert.NotSame(first, buffers.Get(128));
        Assert.Equal(192, buffers.RetainedBytes);
    }

    [Fact]
    public void ANewLengthLetsGoOfTheLeastRecentlyUsedOnes()
    {
        var buffers = new ReusedPixelBuffers(100);
        byte[] a = buffers.Get(40);
        byte[] b = buffers.Get(30);
        Assert.Same(a, buffers.Get(40));

        byte[] c = buffers.Get(50);

        // 40 + 30 + 50 is over the budget: 30 went, as the least recently used.
        Assert.Equal(90, buffers.RetainedBytes);
        Assert.Same(a, buffers.Get(40));
        Assert.Same(c, buffers.Get(50));
        Assert.NotSame(b, buffers.Get(30));
    }

    [Fact]
    public void ALengthLargerThanTheBudgetIsNeverKept()
    {
        var buffers = new ReusedPixelBuffers(100);
        byte[] small = buffers.Get(64);

        byte[] over = buffers.Get(101);

        Assert.Equal(101, over.Length);
        Assert.NotSame(over, buffers.Get(101));
        Assert.Same(small, buffers.Get(64));
        Assert.Equal(64, buffers.RetainedBytes);
    }

    [Fact]
    public void AnotherThreadIsNeverHandedTheOwnersArray()
    {
        var buffers = new ReusedPixelBuffers(1024);
        byte[] owners = buffers.Get(16);

        byte[]? first = null, second = null;
        var thread = new Thread(() =>
        {
            first = buffers.Get(16);
            second = buffers.Get(16);
        });
        thread.Start();
        thread.Join();

        Assert.NotSame(owners, first);
        Assert.NotSame(first, second);
        Assert.Same(owners, buffers.Get(16));
        Assert.Equal(16, buffers.RetainedBytes);
    }

    private static RenderSurface Index16(int width, int height, int seed)
    {
        var source = new byte[width * height * 2];
        var random = new Random(seed);
        for (int i = 0; i < source.Length; i += 2)
            source[i] = (byte)random.Next(32);
        return new RenderSurface
        {
            Width = width,
            Height = height,
            Format = PixelFormat.PFID_INDEX16,
            SourceData = source,
        };
    }

    private static Palette Palette()
    {
        var palette = new Palette();
        for (int i = 0; i < 32; i++)
            palette.Colors.Add(new ColorARGB { Alpha = (byte)(255 - i), Red = (byte)i, Green = (byte)(2 * i), Blue = (byte)(3 * i) });
        return palette;
    }

    [Fact]
    public void ADecodeIntoAReusedArrayIsTheFreshDecode()
    {
        var buffers = new ReusedPixelBuffers(1 << 20);
        Palette palette = Palette();
        // The first texture leaves its pixels, clip-map zeros included, in the array.
        _ = SurfaceDecoder.DecodeRenderSurface(Index16(32, 32, seed: 1), palette, isClipMap: true, isAdditive: false, buffers);

        RenderSurface second = Index16(32, 32, seed: 2);
        DecodedTexture reused = SurfaceDecoder.DecodeRenderSurface(second, palette, isClipMap: false, isAdditive: false, buffers);
        DecodedTexture fresh = SurfaceDecoder.DecodeRenderSurface(second, palette);

        Assert.Equal(fresh.Rgba8, reused.Rgba8);
        Assert.Equal(32 * 32 * 4, reused.Rgba8.Length);
    }

    [Fact]
    public void ARepeatedSizeDecodesWithoutANewImage()
    {
        var buffers = new ReusedPixelBuffers(1 << 20);
        Palette palette = Palette();
        RenderSurface surface = Index16(128, 128, seed: 3);
        _ = SurfaceDecoder.DecodeRenderSurface(surface, palette, isClipMap: false, isAdditive: false, buffers);

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = SurfaceDecoder.DecodeRenderSurface(surface, palette, isClipMap: false, isAdditive: false, buffers);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // The image is 64 KB; only the small result record is new.
        Assert.True(allocated < 1024, $"decoding allocated {allocated} bytes");
    }
}
