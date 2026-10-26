using AcDream.App.Rendering.Gpu;
using AcDream.Core.Textures;
using DatReaderWriter;
using AcDream.Content;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatPixelFormat = DatReaderWriter.Enums.PixelFormat;

namespace AcDream.App.Rendering;

public sealed class TerrainAtlas : IDisposable
{
    internal static readonly GpuSamplerDescription DetailSamplerDescription =
        GpuSamplerDescription.WorldRepeat;

    internal readonly record struct RetailDetailTextureBinding(
        GpuTextureSlot TextureSlot,
        float Tiling,
        uint SurfaceTextureId,
        uint RenderSurfaceId,
        int Width,
        int Height)
    {
        public bool IsAvailable =>
            TextureSlot.IsAssigned
            && SurfaceTextureId != 0
            && RenderSurfaceId != 0;
    }

    private sealed record DetailTextureResource(
        IGpuTexture Texture,
        RetailDetailTextureBinding Binding);

    public IReadOnlyDictionary<uint, uint> TerrainTypeToLayer { get; }
    public int LayerCount { get; }
    public IReadOnlyList<float> TilingByLayer { get; }

    public int AlphaLayerCount { get; }
    public IReadOnlyList<byte> CornerAlphaLayers { get; }
    /// <summary>Layer indices in the alpha atlas for SideTerrainMaps (typically 4 entries).</summary>
    public IReadOnlyList<byte> SideAlphaLayers { get; }
    public IReadOnlyList<byte> RoadAlphaLayers { get; }

    public IReadOnlyList<uint> CornerAlphaTCodes { get; }
    public IReadOnlyList<uint> SideAlphaTCodes { get; }
    public IReadOnlyList<uint> RoadAlphaRCodes { get; }

    internal RetailDetailTextureBinding BuildingDetailTexture { get; }

    internal RetailDetailTextureBinding EnvironmentDetailTexture { get; }

    private sealed class RhiArrays(
        IGpuDevice device,
        IGpuTexture terrain,
        IGpuTexture alpha,
        IGpuSampler alphaSampler)
    {
        public IGpuDevice Device { get; } = device;
        public IGpuTexture Terrain { get; } = terrain;
        public IGpuTexture Alpha { get; } = alpha;
        public IGpuSampler AlphaSampler { get; } = alphaSampler;
        public IGpuSampler? TerrainSampler { get; set; }
        public IGpuTexture? BuildingDetailTexture { get; set; }
        public IGpuTexture? EnvironmentDetailTexture { get; set; }
        public GpuTextureSlot TerrainSlot { get; set; } = GpuTextureSlot.Unassigned;
        public GpuTextureSlot AlphaSlot { get; set; } = GpuTextureSlot.Unassigned;
    }

    private readonly RhiArrays _rhi;

    internal (GpuTextureSlot Terrain, GpuTextureSlot Alpha) TextureSlots =>
        (_rhi.TerrainSlot, _rhi.AlphaSlot);

    private const float RetailMaxAnisotropy = 16f;

    private TerrainAtlas(
        IGpuDevice device,
        IGpuTexture terrain,
        IGpuTexture alpha,
        IGpuSampler alphaSampler,
        IReadOnlyDictionary<uint, uint> map,
        int layerCount,
        IReadOnlyList<float> tilingByLayer,
        int alphaLayerCount,
        IReadOnlyList<byte> cornerLayers,
        IReadOnlyList<byte> sideLayers,
        IReadOnlyList<byte> roadLayers,
        IReadOnlyList<uint> cornerTCodes,
        IReadOnlyList<uint> sideTCodes,
        IReadOnlyList<uint> roadRCodes,
        DetailTextureResource? buildingDetail,
        DetailTextureResource? environmentDetail)
    {
        _rhi = new RhiArrays(device, terrain, alpha, alphaSampler);
        TerrainTypeToLayer = map;
        LayerCount = layerCount;
        TilingByLayer = tilingByLayer;
        AlphaLayerCount = alphaLayerCount;
        CornerAlphaLayers = cornerLayers;
        SideAlphaLayers = sideLayers;
        RoadAlphaLayers = roadLayers;
        CornerAlphaTCodes = cornerTCodes;
        SideAlphaTCodes = sideTCodes;
        RoadAlphaRCodes = roadRCodes;
        _rhi.BuildingDetailTexture = buildingDetail?.Texture;
        _rhi.EnvironmentDetailTexture = environmentDetail?.Texture;
        BuildingDetailTexture = buildingDetail?.Binding ?? default;
        EnvironmentDetailTexture = environmentDetail?.Binding ?? default;
        _rhi.AlphaSlot = device.RegisterTexture(alpha, alphaSampler);
        ApplyAnisotropic(RetailMaxAnisotropy);
    }

    /// <summary>
    /// One source layer as it is uploaded: a terrain layer is four bytes per
    /// texel in blue, green, red, alpha order (the game files' own order for
    /// 32-bit colour, so those images are uploaded as stored); an alpha map
    /// is one byte per texel, the blend weight.
    /// </summary>
    internal sealed record LayerTexels(byte[] Texels, int Width, int Height);

    internal readonly record struct TerrainLayerDecode(
        Dictionary<uint, LayerTexels> DecodedByType,
        Dictionary<uint, uint> TilingByType,
        int MaxWidth,
        int MaxHeight);

    private static TerrainLayerDecode DecodeTerrainLayers(
        IDatReaderWriter dats,
        IReadOnlyList<DatReaderWriter.Types.TMTerrainDesc> terrainDesc,
        object? datLock)
    {
        var decodedByType = new Dictionary<uint, LayerTexels>();
        var tilingByType = new Dictionary<uint, uint>();
        int maxW = 1, maxH = 1;
        foreach (var tmtd in terrainDesc)
        {
            uint typeKey = (uint)tmtd.TerrainType;
            if (decodedByType.ContainsKey(typeKey))
                continue;

            tilingByType[typeKey] = tmtd.TerrainTex.TexTiling;

            var surfaceTextureId = (uint)tmtd.TerrainTex.TextureId;
            var st = Read(datLock, () => dats.Get<SurfaceTexture>(surfaceTextureId));
            if (st is null || st.Textures.Count == 0)
            {
                Console.WriteLine($"WARN: TerrainType {tmtd.TerrainType} SurfaceTexture 0x{surfaceTextureId:X8} missing");
                decodedByType[typeKey] = Bgra(DecodedTexture.Magenta);
                continue;
            }

            var rs = Read(datLock, () => dats.Get<RenderSurface>((uint)st.Textures[0]));
            if (rs is null)
            {
                decodedByType[typeKey] = Bgra(DecodedTexture.Magenta);
                continue;
            }

            LayerTexels decoded;
            if (IsStoredBgra(rs))
            {
                // Already blue, green, red, alpha: uploaded as stored, with
                // no decoded copy.
                decoded = new LayerTexels(Exactly(rs.SourceData, rs.Width * rs.Height * 4), rs.Width, rs.Height);
            }
            else
            {
                Palette? palette = rs.DefaultPaletteId != 0
                    ? Read(datLock, () => dats.Get<Palette>(rs.DefaultPaletteId))
                    : null;
                decoded = Bgra(SurfaceDecoder.DecodeRenderSurface(rs, palette));
            }
            decodedByType[typeKey] = decoded;
            if (decoded.Width > maxW) maxW = decoded.Width;
            if (decoded.Height > maxH) maxH = decoded.Height;
        }

        return new TerrainLayerDecode(decodedByType, tilingByType, maxW, maxH);
    }

    internal sealed record AlphaLayerDecode(
        List<LayerTexels> Decoded,
        List<byte> CornerLayers,
        List<byte> SideLayers,
        List<byte> RoadLayers,
        List<uint> CornerTCodes,
        List<uint> SideTCodes,
        List<uint> RoadRCodes,
        int MaxWidth,
        int MaxHeight);

    private static AlphaLayerDecode DecodeAlphaLayers(
        IDatReaderWriter dats,
        DatReaderWriter.Types.TexMerge texMerge,
        object? datLock)
    {
        var decoded = new List<LayerTexels>();
        var cornerLayers = new List<byte>();
        var sideLayers = new List<byte>();
        var roadLayers = new List<byte>();
        var cornerTCodes = new List<uint>();
        var sideTCodes = new List<uint>();
        var roadRCodes = new List<uint>();

        foreach (var entry in texMerge.CornerTerrainMaps)
        {
            if (TryDecodeAlphaMap(dats, (uint)entry.TextureId, datLock, out var dtex))
            {
                cornerLayers.Add((byte)decoded.Count);
                cornerTCodes.Add(entry.TCode);
                decoded.Add(dtex);
            }
            else
            {
                Console.WriteLine($"WARN: CornerTerrainMap TextureId 0x{(uint)entry.TextureId:X8} failed to decode");
            }
        }
        foreach (var entry in texMerge.SideTerrainMaps)
        {
            if (TryDecodeAlphaMap(dats, (uint)entry.TextureId, datLock, out var dtex))
            {
                sideLayers.Add((byte)decoded.Count);
                sideTCodes.Add(entry.TCode);
                decoded.Add(dtex);
            }
            else
            {
                Console.WriteLine($"WARN: SideTerrainMap TextureId 0x{(uint)entry.TextureId:X8} failed to decode");
            }
        }
        foreach (var entry in texMerge.RoadMaps)
        {
            if (TryDecodeAlphaMap(dats, (uint)entry.TextureId, datLock, out var dtex))
            {
                roadLayers.Add((byte)decoded.Count);
                roadRCodes.Add(entry.RCode);
                decoded.Add(dtex);
            }
            else
            {
                Console.WriteLine($"WARN: RoadMap TextureId 0x{(uint)entry.TextureId:X8} failed to decode");
            }
        }

        int decodedMaxW = 1, decodedMaxH = 1;
        foreach (var d in decoded)
        {
            if (d.Width > decodedMaxW) decodedMaxW = d.Width;
            if (d.Height > decodedMaxH) decodedMaxH = d.Height;
        }

        return new AlphaLayerDecode(
            decoded,
            cornerLayers,
            sideLayers,
            roadLayers,
            cornerTCodes,
            sideTCodes,
            roadRCodes,
            decodedMaxW,
            decodedMaxH);
    }

    /// <summary>
    /// The atlas's source layers, read from the game files and decoded to
    /// pixels: everything the atlas needs before the GPU is involved.
    /// </summary>
    internal sealed record DecodedSources(
        IDatReaderWriter Dats,
        IReadOnlyList<DatReaderWriter.Types.TMTerrainDesc>? TerrainDesc,
        TerrainLayerDecode? Terrain,
        AlphaLayerDecode Alpha);

    /// <summary>
    /// Starts reading and decoding the atlas's source layers on a worker
    /// thread, taking <paramref name="datLock"/> for each read, so the work
    /// overlaps window and GPU setup on the main thread.
    /// </summary>
    internal static Task<DecodedSources> BeginDecodeSources(IDatReaderWriter dats, object datLock)
    {
        ArgumentNullException.ThrowIfNull(dats);
        ArgumentNullException.ThrowIfNull(datLock);
        return Task.Run(() => DecodeSources(dats, datLock));
    }

    /// <summary>Reads and decodes the atlas's source layers, under <paramref name="datLock"/> when one is given.</summary>
    internal static DecodedSources DecodeSources(IDatReaderWriter dats, object? datLock)
    {
        ArgumentNullException.ThrowIfNull(dats);
        var region = Read(datLock, () => dats.Get<Region>(0x13000000u))
            ?? throw new InvalidOperationException("Region dat id 0x13000000 missing");
        var texMerge = region.TerrainInfo?.LandSurfaces?.TexMerge;
        var terrainDesc = texMerge?.TerrainDesc;
        TerrainLayerDecode? terrain = terrainDesc is null || terrainDesc.Count == 0
            ? null
            : DecodeTerrainLayers(dats, terrainDesc, datLock);
        AlphaLayerDecode alpha = texMerge is null
            ? new AlphaLayerDecode([], [], [], [], [], [], [], 1, 1)
            : DecodeAlphaLayers(dats, texMerge, datLock);
        return new DecodedSources(dats, terrainDesc, terrain, alpha);
    }

    private static T Read<T>(object? datLock, Func<T> read)
    {
        if (datLock is null)
            return read();
        lock (datLock)
            return read();
    }

    /// <param name="sources">
    /// The source layers decoded ahead of time from <paramref name="dats"/>;
    /// when absent, or read from other files, they are decoded here.
    /// </param>
    internal static TerrainAtlas BuildBackendNeutral(
        IGpuDevice device,
        IDatReaderWriter dats,
        Wb.WorldTextureDetail? textureDetail = null,
        DecodedSources? sources = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(dats);
        textureDetail ??= Wb.WorldTextureDetail.Full;

        if (sources is null || !ReferenceEquals(sources.Dats, dats))
            sources = DecodeSources(dats, datLock: null);
        var terrainDesc = sources.TerrainDesc;

        Dictionary<uint, LayerTexels> decodedByType;
        Dictionary<uint, uint> tilingByType;
        int maxW, maxH;
        if (sources.Terrain is not { } decode)
        {
            Console.WriteLine("WARN: TerrainDesc missing, using single white fallback layer");
            decodedByType = new Dictionary<uint, LayerTexels> { [0u] = new LayerTexels([0xFF, 0xFF, 0xFF, 0xFF], 1, 1) };
            tilingByType = [];
            maxW = 1;
            maxH = 1;
        }
        else
        {
            decodedByType = decode.DecodedByType;
            tilingByType = decode.TilingByType;
            // The landscape texture-detail choice: every layer is resampled to
            // the reduced size of the largest source, so one array still fits
            // them all.
            maxW = textureDetail.LandscapeSize(decode.MaxWidth);
            maxH = textureDetail.LandscapeSize(decode.MaxHeight);
        }

        AlphaLayerDecode alpha = sources.Alpha;
        List<LayerTexels> alphaDecoded = alpha.Decoded;
        int alphaLayerCount = Math.Max(1, alphaDecoded.Count);
        int alphaW = alphaDecoded.Count == 0 ? 1 : alpha.MaxWidth;
        int alphaH = alphaDecoded.Count == 0 ? 1 : alpha.MaxHeight;

        int layerCount = decodedByType.Count;
        int mipLevels = Wb.RhiWorldTextureArray.MipLevelsFor(maxW, maxH);
        IGpuTexture? terrainTexture = null;
        IGpuTexture? alphaTexture = null;
        IGpuSampler? detailSampler = null;
        DetailTextureResource? buildingDetail = null;
        DetailTextureResource? environmentDetail = null;
        try
        {
            terrainTexture = device.CreateTexture(new GpuTextureDescription(
                "terrain-atlas",
                GpuTextureKind.Texture2DArray,
                GpuTextureFormat.Bgra8Unorm,
                maxW,
                maxH,
                layerCount,
                mipLevels));

            var map = new Dictionary<uint, uint>(layerCount);
            int layerIdx = 0;
            foreach (var kvp in decodedByType)
            {
                byte[] buffer = Resample(kvp.Value, maxW, maxH);
                terrainTexture.Upload(0, layerIdx, buffer);
                map[kvp.Key] = (uint)layerIdx;
                layerIdx++;
            }

            terrainTexture.GenerateMipChain();

            var tilingByLayer = TerrainTextureTilingTable.Build(
                map.Select(entry =>
                    (entry.Value, tilingByType.TryGetValue(entry.Key, out uint repeatCount)
                        ? repeatCount
                        : 1u)));

            alphaTexture = device.CreateTexture(new GpuTextureDescription(
                "terrain-alpha-atlas",
                GpuTextureKind.Texture2DArray,
                GpuTextureFormat.R8Unorm,
                alphaW,
                alphaH,
                alphaLayerCount,
                MipLevelCount: 1,
                GpuTextureSwizzle.Replicate));
            if (alphaDecoded.Count == 0)
            {
                Console.WriteLine("WARN: no alpha maps loaded; alpha atlas will be a 1x1 white fallback");
                alphaTexture.Upload(0, 0, [0xFF]);
            }
            else
            {
                for (int i = 0; i < alphaDecoded.Count; i++)
                    alphaTexture.Upload(0, i, ResizeNearest(alphaDecoded[i], bytesPerTexel: 1, alphaW, alphaH));
            }

            IGpuSampler alphaSampler = device.CreateSampler(GpuSamplerDescription.WorldClamp with
            {
                MipFilter = GpuMipFilter.None,
            });

            detailSampler = device.CreateSampler(DetailSamplerDescription);
            if (terrainDesc is { Count: > 1 })
            {
                buildingDetail = TryCreateDetailTexture(
                    device,
                    dats,
                    detailSampler,
                    terrainDesc[1],
                    "building");
            }
            if (terrainDesc is { Count: > 2 })
            {
                environmentDetail = TryCreateDetailTexture(
                    device,
                    dats,
                    detailSampler,
                    terrainDesc[2],
                    "environment");
            }

            Console.WriteLine(
                $"TerrainAtlas: {layerCount} terrain layers at {maxW}x{maxH} ({mipLevels} mip levels, "
                + $"landscape detail {textureDetail.Landscape})");
            Console.WriteLine(
                $"AlphaAtlas: {alphaLayerCount} layers at {alphaW}x{alphaH}  "
                + $"(corners={alpha.CornerLayers.Count}, sides={alpha.SideLayers.Count}, "
                + $"roads={alpha.RoadLayers.Count})");

            return new TerrainAtlas(
                device,
                terrainTexture,
                alphaTexture,
                alphaSampler,
                map,
                layerCount,
                tilingByLayer,
                alphaLayerCount,
                alpha.CornerLayers,
                alpha.SideLayers,
                alpha.RoadLayers,
                alpha.CornerTCodes,
                alpha.SideTCodes,
                alpha.RoadRCodes,
                buildingDetail,
                environmentDetail);
        }
        catch
        {
            DisposeDetailTextureResource(device, environmentDetail);
            DisposeDetailTextureResource(device, buildingDetail);
            alphaTexture?.Dispose();
            terrainTexture?.Dispose();
            throw;
        }
    }

    private static DetailTextureResource? TryCreateDetailTexture(
        IGpuDevice device,
        IDatReaderWriter dats,
        IGpuSampler sampler,
        DatReaderWriter.Types.TMTerrainDesc terrain,
        string categoryName)
    {
        uint surfaceTextureId = (uint)terrain.TerrainTex.DetailTextureId;
        if (surfaceTextureId == 0)
            return null;

        SurfaceTexture? surfaceTexture = dats.Get<SurfaceTexture>(surfaceTextureId);
        if (surfaceTexture is null || surfaceTexture.Textures.Count == 0)
        {
            Console.WriteLine(
                $"WARN: retail {categoryName} detail SurfaceTexture "
                + $"0x{surfaceTextureId:X8} missing");
            return null;
        }

        uint renderSurfaceId = (uint)surfaceTexture.Textures[0];
        RenderSurface? renderSurface = dats.Get<RenderSurface>(renderSurfaceId);
        if (renderSurface is null)
        {
            Console.WriteLine(
                $"WARN: retail {categoryName} detail RenderSurface "
                + $"0x{renderSurfaceId:X8} missing");
            return null;
        }

        Palette? palette = renderSurface.DefaultPaletteId != 0
            ? dats.Get<Palette>(renderSurface.DefaultPaletteId)
            : null;
        DecodedTexture decoded = SurfaceDecoder.DecodeRenderSurface(
            renderSurface,
            palette);
        if (ReferenceEquals(decoded, DecodedTexture.Magenta))
        {
            Console.WriteLine(
                $"WARN: retail {categoryName} detail RenderSurface "
                + $"0x{renderSurfaceId:X8} failed to decode");
            return null;
        }

        int mipLevels = Wb.RhiWorldTextureArray.MipLevelsFor(
            decoded.Width,
            decoded.Height);
        IGpuTexture? texture = null;
        GpuTextureSlot slot = GpuTextureSlot.Unassigned;
        try
        {
            texture = device.CreateTexture(new GpuTextureDescription(
                $"retail-detail-{categoryName}",
                GpuTextureKind.Texture2DArray,
                GpuTextureFormat.Rgba8Unorm,
                decoded.Width,
                decoded.Height,
                LayerCount: 1,
                MipLevelCount: mipLevels));
            texture.Upload(0, 0, decoded.Rgba8);
            texture.GenerateMipChain();
            slot = device.RegisterTexture(texture, sampler);
            var binding = new RetailDetailTextureBinding(
                slot,
                terrain.TerrainTex.DetailTexTiling,
                surfaceTextureId,
                renderSurfaceId,
                decoded.Width,
                decoded.Height);
            Console.WriteLine(
                $"Retail detail {categoryName}: SurfaceTexture "
                + $"0x{surfaceTextureId:X8} -> RenderSurface "
                + $"0x{renderSurfaceId:X8}, {decoded.Width}x{decoded.Height}, "
                + $"tiling={binding.Tiling}");
            return new DetailTextureResource(texture, binding);
        }
        catch
        {
            if (slot.IsAssigned)
                device.ReleaseTextureSlot(slot);
            texture?.Dispose();
            throw;
        }
    }

    private static void DisposeDetailTextureResource(
        IGpuDevice device,
        DetailTextureResource? resource)
    {
        if (resource is null)
            return;

        if (resource.Binding.IsAvailable)
            device.ReleaseTextureSlot(resource.Binding.TextureSlot);
        resource.Texture.Dispose();
    }


    private void ApplyAnisotropic(float level)
    {
        RhiArrays rhi = _rhi!;
        IGpuSampler sampler = rhi.Device.CreateSampler(GpuSamplerDescription.WorldRepeat with
        {
            MaxAnisotropy = Math.Max(1f, level),
        });
        if (ReferenceEquals(rhi.TerrainSampler, sampler) && rhi.TerrainSlot.IsAssigned)
            return;

        // Every built terrain tile carries this slot's index in its instance
        // data, so the slot is rewritten in place rather than released: a
        // released index is scrubbed and reused, and the tiles would sample
        // whatever texture took it next.
        rhi.TerrainSampler = sampler;
        rhi.TerrainSlot = rhi.Device.ReplaceTextureSlot(rhi.TerrainSlot, rhi.Terrain, sampler);
    }

    private static bool TryDecodeAlphaMap(
        IDatReaderWriter dats,
        uint surfaceTextureId,
        object? datLock,
        out LayerTexels decoded)
    {
        decoded = null!;

        var st = Read(datLock, () => dats.Get<SurfaceTexture>(surfaceTextureId));
        if (st is null || st.Textures.Count == 0)
            return false;

        var rs = Read(datLock, () => dats.Get<RenderSurface>((uint)st.Textures[0]));
        if (rs is null)
            return false;

        // Alpha maps ship as PFID_CUSTOM_LSCAPE_ALPHA (AC's landscape-alpha
        // format) or the more generic PFID_A8: one byte, the blend weight,
        // which the alpha array (one byte per texel) presents in every
        // channel. The terrain shader reads the weight from alpha, so any
        // other format contributes its decoded alpha. Palette is not used.
        if (TextureCache.IsUploadableCoverage(rs))
        {
            decoded = new LayerTexels(Exactly(rs.SourceData, rs.Width * rs.Height), rs.Width, rs.Height);
            return true;
        }

        var d = SurfaceDecoder.DecodeRenderSurface(rs, palette: null, isClipMap: false, isAdditive: true);
        if (ReferenceEquals(d, DecodedTexture.Magenta))
            return false;

        var weights = new byte[d.Width * d.Height];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = d.Rgba8[(i * 4) + 3];
        decoded = new LayerTexels(weights, d.Width, d.Height);
        return true;
    }

    /// <summary>A 32-bit colour image whose stored bytes are already the
    /// blue, green, red, alpha texels the terrain array takes.</summary>
    private static bool IsStoredBgra(RenderSurface rs) =>
        rs.Format == DatPixelFormat.PFID_A8R8G8B8
        && rs.Width > 0
        && rs.Height > 0
        && rs.SourceData is not null
        && rs.SourceData.Length >= rs.Width * rs.Height * 4;

    /// <summary>A decoded red, green, blue, alpha image in the array's
    /// blue, green, red, alpha order.</summary>
    internal static LayerTexels Bgra(DecodedTexture decoded)
    {
        byte[] rgba = decoded.Rgba8;
        var bgra = new byte[decoded.Width * decoded.Height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }
        return new LayerTexels(bgra, decoded.Width, decoded.Height);
    }

    /// <summary>The stored bytes when they are exactly the image, else the
    /// image's leading bytes.</summary>
    private static byte[] Exactly(byte[] stored, int length) =>
        stored.Length == length ? stored : stored[..length];

    /// <summary>Box-averages a four-byte layer that shrinks; nearest-neighbour for one that grows or matches.</summary>
    private static byte[] Resample(LayerTexels src, int dstW, int dstH)
    {
        bool growsOnASide = dstW > src.Width || dstH > src.Height;
        bool shrinksOnASide = dstW < src.Width || dstH < src.Height;
        if (!growsOnASide && shrinksOnASide)
            return TexturePixels.DownsampleBox(src.Texels, src.Width, src.Height, 4, dstW, dstH);
        return ResizeNearest(src, bytesPerTexel: 4, dstW, dstH);
    }

    private static byte[] ResizeNearest(LayerTexels src, int bytesPerTexel, int dstW, int dstH)
    {
        if (src.Width == dstW && src.Height == dstH)
            return src.Texels;

        var dst = new byte[dstW * dstH * bytesPerTexel];
        for (int y = 0; y < dstH; y++)
        {
            int srcY = y * src.Height / dstH;
            for (int x = 0; x < dstW; x++)
            {
                int srcX = x * src.Width / dstW;
                int si = (srcY * src.Width + srcX) * bytesPerTexel;
                int di = (y * dstW + x) * bytesPerTexel;
                for (int b = 0; b < bytesPerTexel; b++)
                    dst[di + b] = src.Texels[si + b];
            }
        }
        return dst;
    }

    public void SetAnisotropic(int level)
    {
        ApplyAnisotropic(level);
        Console.WriteLine($"TerrainAtlas: anisotropic updated to {level}x");
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _rhi.EnvironmentDetailTexture?.Dispose();
        _rhi.BuildingDetailTexture?.Dispose();
        _rhi.Alpha.Dispose();
        _rhi.Terrain.Dispose();
    }
}
