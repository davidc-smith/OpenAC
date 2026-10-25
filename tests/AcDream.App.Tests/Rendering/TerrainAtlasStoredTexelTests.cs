using System;
using System.IO;
using System.Linq;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.Content;
using AcDream.Core.Textures;
using Palette = DatReaderWriter.DBObjs.Palette;
using Region = DatReaderWriter.DBObjs.Region;
using RenderSurface = DatReaderWriter.DBObjs.RenderSurface;
using SurfaceTexture = DatReaderWriter.DBObjs.SurfaceTexture;
using Xunit;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// The terrain array takes the game files' 32-bit colour images in their
/// stored blue, green, red, alpha order, and the alpha array one byte per
/// texel shown in every channel; sampled, both are exactly the four-channel
/// decode they replace.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class TerrainAtlasStoredTexelTests
{
    [Fact]
    public void EveryLayerSamplesAsTheFourChannelDecode()
    {
        using var installed = new BoundedTestDatCollection(RequireDatDir());
        IDatReaderWriter dats = installed;
        var device = new RecordingGpuDevice();
        device.Clear();

        using TerrainAtlas atlas = TerrainAtlas.BuildBackendNeutral(device, dats);

        RecordingGpuTexture terrain = device.CreatedTextures.Single(t => t.Name == "terrain-atlas");
        RecordingGpuTexture alpha = device.CreatedTextures.Single(t => t.Name == "terrain-alpha-atlas");
        Assert.Equal(GpuTextureFormat.Bgra8Unorm, terrain.Format);
        Assert.Equal(GpuTextureFormat.R8Unorm, alpha.Format);
        Assert.Equal(GpuTextureSwizzle.Replicate, alpha.Swizzle);

        var texMerge = dats.Get<Region>(0x13000000u)!.TerrainInfo!.LandSurfaces!.TexMerge!;
        int checkedLayers = 0;
        var seen = new System.Collections.Generic.HashSet<uint>();
        foreach (var desc in texMerge.TerrainDesc)
        {
            // A terrain type's first description names its layer.
            if (!seen.Add((uint)desc.TerrainType))
                continue;
            uint layer = atlas.TerrainTypeToLayer[(uint)desc.TerrainType];
            DecodedTexture expected = Decode(dats, (uint)desc.TerrainTex.TextureId, isAdditive: false);
            Assert.Equal((expected.Width, expected.Height), (terrain.Width, terrain.Height));
            byte[] bgra = terrain.UploadedBytes[(int)layer];
            Assert.Equal(expected.Rgba8.Length, bgra.Length);
            for (int i = 0; i < bgra.Length; i += 4)
            {
                if (bgra[i] != expected.Rgba8[i + 2] || bgra[i + 1] != expected.Rgba8[i + 1]
                    || bgra[i + 2] != expected.Rgba8[i] || bgra[i + 3] != expected.Rgba8[i + 3])
                {
                    Assert.Fail($"terrain layer {layer} differs at texel {i / 4}");
                }
            }
            checkedLayers++;
        }
        Assert.True(checkedLayers > 20, $"only {checkedLayers} terrain layers");

        var alphaIds = texMerge.CornerTerrainMaps.Select(m => (uint)m.TextureId)
            .Concat(texMerge.SideTerrainMaps.Select(m => (uint)m.TextureId))
            .Concat(texMerge.RoadMaps.Select(m => (uint)m.TextureId))
            .ToList();
        Assert.Equal(alphaIds.Count, alpha.UploadedBytes.Count);
        for (int layer = 0; layer < alphaIds.Count; layer++)
        {
            DecodedTexture expected = Decode(dats, alphaIds[layer], isAdditive: true);
            Assert.Equal((expected.Width, expected.Height), (alpha.Width, alpha.Height));
            byte[] weights = alpha.UploadedBytes[layer];
            Assert.Equal(expected.Width * expected.Height, weights.Length);
            for (int i = 0; i < weights.Length; i++)
            {
                // Every channel of the four-channel decode is the weight.
                for (int c = 0; c < 4; c++)
                {
                    if (expected.Rgba8[(i * 4) + c] != weights[i])
                        Assert.Fail($"alpha layer {layer} differs at texel {i}, channel {c}");
                }
            }
        }
    }

    private static DecodedTexture Decode(IDatReaderWriter dats, uint surfaceTextureId, bool isAdditive)
    {
        SurfaceTexture st = dats.Get<SurfaceTexture>(surfaceTextureId)!;
        RenderSurface rs = dats.Get<RenderSurface>((uint)st.Textures[0])!;
        Palette? palette = rs.DefaultPaletteId != 0 ? dats.Get<Palette>(rs.DefaultPaletteId) : null;
        return SurfaceDecoder.DecodeRenderSurface(rs, palette, isClipMap: false, isAdditive: isAdditive);
    }

    private static string RequireDatDir()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;
        string def = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents",
            "Asheron's Call");
        if (Directory.Exists(def))
            return def;
        Assert.Fail("Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
        return string.Empty;
    }
}
