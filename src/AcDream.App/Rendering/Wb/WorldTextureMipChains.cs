using System.Runtime.CompilerServices;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Rendering.Gpu.Vk;
using AcDream.Content;
using AcDream.Core.Rendering.Wb;

namespace AcDream.App.Rendering.Wb;

/// <summary>
/// World texture mip chains (every level below level 0), built on the mesh
/// preparation workers so that the drawing thread only uploads them. A chain
/// is kept with its level-0 payload: payloads are shared by every mesh that
/// uses the texture, so a texture's chain is built once while its payload is
/// alive, and goes with it.
/// </summary>
internal sealed class WorldTextureMipChains
{
    private readonly ConditionalWeakTable<byte[], byte[][]> _chains = new();

    /// <summary>
    /// Builds the chain of every texture a mesh (and the cell geometry it
    /// carries) draws with that has none yet. Runs on a preparation worker.
    /// </summary>
    internal void Prepare(ObjectMeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.EnvCellGeometry is { } nested)
            Prepare(nested);

        foreach (var (format, batches) in mesh.TextureBatches)
        {
            if (!Supports(format.Format))
                continue;
            int expectedBytes;
            try
            {
                expectedBytes = RhiWorldTextureArray.CalculateExpectedDataSize(
                    format.Format,
                    format.Width,
                    format.Height);
            }
            catch (OverflowException)
            {
                continue;
            }

            foreach (TextureBatchData batch in batches)
            {
                byte[] level0 = batch.TextureData;
                // A mis-sized layer is left to the upload, which rejects it.
                if (batch.Indices.Count == 0
                    || level0.Length != expectedBytes
                    || _chains.TryGetValue(level0, out _))
                {
                    continue;
                }

                _chains.TryAdd(
                    level0,
                    Build(format.Format, level0, format.Width, format.Height));
            }
        }
    }

    /// <summary>The chain built for <paramref name="level0"/>, if any.</summary>
    internal byte[][]? Find(byte[] level0) =>
        _chains.TryGetValue(level0, out byte[][]? chain) ? chain : null;

    internal static bool Supports(TextureFormat format) =>
        format is TextureFormat.RGBA8
            or TextureFormat.DXT1
            or TextureFormat.DXT3
            or TextureFormat.DXT5;

    /// <summary>
    /// Levels 1 and down of a layer, in the layer's own format: each level is
    /// the 2 x 2 box filter of the one above, and a block-compressed layer is
    /// decoded once and each level encoded again.
    /// </summary>
    internal static byte[][] Build(
        TextureFormat format,
        byte[] level0,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(level0);
        if (!Supports(format))
        {
            throw new NotSupportedException(
                $"World texture format {format} has no prepared mip chain.");
        }

        int levelCount = RhiWorldTextureArray.MipLevelsFor(width, height);
        if (levelCount <= 1)
            return [];

        GpuTextureFormat gpuFormat = RhiWorldTextureArray.MapFormat(format);
        IReadOnlyList<BlockCompressionMipChain.Level> levels =
            BlockCompressionCodec.IsBlockCompressed(gpuFormat)
                ? BlockCompressionMipChain.BuildCompressed(
                    gpuFormat,
                    level0,
                    width,
                    height,
                    levelCount)
                : BlockCompressionMipChain.BuildFromRgba(
                    gpuFormat,
                    level0,
                    width,
                    height,
                    levelCount);
        var chain = new byte[levels.Count][];
        for (int i = 0; i < levels.Count; i++)
            chain[i] = levels[i].Data;
        return chain;
    }

    /// <summary>The bytes of a chain.</summary>
    internal static long Bytes(byte[][] chain)
    {
        long total = 0;
        foreach (byte[] level in chain)
            total = checked(total + level.Length);
        return total;
    }
}
