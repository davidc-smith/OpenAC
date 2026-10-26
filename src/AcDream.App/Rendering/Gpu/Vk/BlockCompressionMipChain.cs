using System.Buffers;

namespace AcDream.App.Rendering.Gpu.Vk;

internal static class BlockCompressionMipChain
{
    internal readonly record struct Level(int MipLevel, int Width, int Height, byte[] Data);

    internal static IReadOnlyList<Level> BuildCompressed(
        GpuTextureFormat format,
        ReadOnlySpan<byte> level0,
        int width,
        int height,
        int mipLevelCount)
    {
        if (!BlockCompressionCodec.IsBlockCompressed(format))
            throw new ArgumentOutOfRangeException(nameof(format), format, "This chain builder is for BC formats.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mipLevelCount);
        if (mipLevelCount == 1)
            return [];

        // The decoded level 0 and the filtered levels are working images,
        // dropped once each level is encoded again: they are rented, so a
        // chain allocates only the levels it returns.
        int rgbaBytes = checked(width * height * 4);
        byte[] rgba = ArrayPool<byte>.Shared.Rent(rgbaBytes);
        try
        {
            BlockCompressionCodec.DecodeLevel(format, level0, width, height, rgba.AsSpan(0, rgbaBytes));
            return BuildFromRgba(format, rgba.AsSpan(0, rgbaBytes), width, height, mipLevelCount);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rgba);
        }
    }

    internal static IReadOnlyList<Level> BuildFromRgba(
        GpuTextureFormat format,
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        int mipLevelCount)
    {
        var levels = new List<Level>(Math.Max(0, mipLevelCount - 1));
        if (mipLevelCount <= 1)
            return levels;

        // Level 1 is filtered straight from the caller's level 0; no copy of
        // it is made.
        ReadOnlySpan<byte> current = rgba;
        int currentWidth = width;
        int currentHeight = height;

        if (!BlockCompressionCodec.IsBlockCompressed(format))
        {
            // An uncompressed level is itself the level returned.
            for (int level = 1; level < mipLevelCount; level++)
            {
                (byte[] next, int nextWidth, int nextHeight) = Downsample(current, currentWidth, currentHeight);
                levels.Add(new Level(level, nextWidth, nextHeight, next));
                current = next;
                currentWidth = nextWidth;
                currentHeight = nextHeight;
            }
            return levels;
        }

        // A compressed level is encoded from a filtered working image. Two
        // rented images take turns: one is filtered into the other.
        int firstBytes = checked(Math.Max(1, width / 2) * Math.Max(1, height / 2) * 4);
        byte[] scratchA = ArrayPool<byte>.Shared.Rent(firstBytes);
        byte[] scratchB = ArrayPool<byte>.Shared.Rent(firstBytes);
        try
        {
            byte[] target = scratchA;
            for (int level = 1; level < mipLevelCount; level++)
            {
                (int nextWidth, int nextHeight) = Downsample(current, currentWidth, currentHeight, target);
                ReadOnlySpan<byte> next = target.AsSpan(0, nextWidth * nextHeight * 4);
                levels.Add(new Level(
                    level,
                    nextWidth,
                    nextHeight,
                    BlockCompressionCodec.EncodeLevel(format, next, nextWidth, nextHeight)));
                current = next;
                currentWidth = nextWidth;
                currentHeight = nextHeight;
                target = ReferenceEquals(target, scratchA) ? scratchB : scratchA;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratchB);
            ArrayPool<byte>.Shared.Return(scratchA);
        }

        return levels;
    }

    internal static (byte[] Rgba, int Width, int Height) Downsample(
        ReadOnlySpan<byte> rgba,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var output = new byte[Math.Max(1, width / 2) * Math.Max(1, height / 2) * 4];
        (int nextWidth, int nextHeight) = Downsample(rgba, width, height, output);
        return (output, nextWidth, nextHeight);
    }

    /// <summary>
    /// The 2 x 2 box filter of a level into <paramref name="output"/>, which
    /// must hold the next level; every texel of the next level is written.
    /// </summary>
    internal static (int Width, int Height) Downsample(
        ReadOnlySpan<byte> rgba,
        int width,
        int height,
        Span<byte> output)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int nextWidth = Math.Max(1, width / 2);
        int nextHeight = Math.Max(1, height / 2);
        if (output.Length < nextWidth * nextHeight * 4)
        {
            throw new ArgumentException(
                $"A {nextWidth}x{nextHeight} RGBA8 level needs {nextWidth * nextHeight * 4} bytes.",
                nameof(output));
        }

        for (int y = 0; y < nextHeight; y++)
        {
            int y0 = Math.Min((y * 2) + 0, height - 1);
            int y1 = Math.Min((y * 2) + 1, height - 1);
            for (int x = 0; x < nextWidth; x++)
            {
                int x0 = Math.Min((x * 2) + 0, width - 1);
                int x1 = Math.Min((x * 2) + 1, width - 1);

                int a = ((y0 * width) + x0) * 4;
                int b = ((y0 * width) + x1) * 4;
                int c = ((y1 * width) + x0) * 4;
                int d = ((y1 * width) + x1) * 4;
                int destination = ((y * nextWidth) + x) * 4;

                for (int channel = 0; channel < 4; channel++)
                {
                    int sum = rgba[a + channel] + rgba[b + channel] + rgba[c + channel] + rgba[d + channel];
                    output[destination + channel] = (byte)((sum + 2) / 4);
                }
            }
        }

        return (nextWidth, nextHeight);
    }
}
