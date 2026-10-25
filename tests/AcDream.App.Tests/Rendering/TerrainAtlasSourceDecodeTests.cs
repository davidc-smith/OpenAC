using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AcDream.App.Rendering;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.Content;
using Region = DatReaderWriter.DBObjs.Region;
using Xunit;

namespace AcDream.App.Tests.Rendering;

/// <summary>
/// The terrain atlas's source layers can be decoded on a worker ahead of
/// time; an atlas built from them is the one built from the files directly,
/// and building it reads none of those layers again.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class TerrainAtlasSourceDecodeTests
{
    [Fact]
    public async Task AnAtlasFromLayersDecodedAheadMatchesOneBuiltDirectly()
    {
        using var installed = new BoundedTestDatCollection(RequireDatDir());
        IDatReaderWriter dats = installed;
        CountingDats counting = CountingDats.Over(dats);
        IDatReaderWriter counted = (IDatReaderWriter)counting;

        TerrainAtlas.DecodedSources sources =
            await TerrainAtlas.BeginDecodeSources(counted, new object());
        int decodeReads = counting.Reads.Count;
        counting.Reads.Clear();

        using TerrainAtlas ahead = TerrainAtlas.BuildBackendNeutral(
            new RecordingGpuDevice(), counted, sources: sources);
        using TerrainAtlas direct = TerrainAtlas.BuildBackendNeutral(
            new RecordingGpuDevice(), dats);

        Assert.True(decodeReads > 30, $"the decode read only {decodeReads} records");
        Assert.DoesNotContain(typeof(Region), counting.Reads);
        // Only the two detail textures are read at build time.
        Assert.True(counting.Reads.Count <= 6, string.Join(", ", counting.Reads));
        AssertSameAtlas(direct, ahead);
    }

    [Fact]
    public async Task LayersDecodedFromOtherFilesAreDecodedAgain()
    {
        using var installed = new BoundedTestDatCollection(RequireDatDir());
        IDatReaderWriter dats = installed;
        TerrainAtlas.DecodedSources elsewhere =
            await TerrainAtlas.BeginDecodeSources(dats, new object());
        CountingDats counting = CountingDats.Over(dats);

        using TerrainAtlas atlas = TerrainAtlas.BuildBackendNeutral(
            new RecordingGpuDevice(), (IDatReaderWriter)counting, sources: elsewhere);

        Assert.Contains(typeof(Region), counting.Reads);
    }

    private static void AssertSameAtlas(TerrainAtlas expected, TerrainAtlas actual)
    {
        Assert.Equal(expected.TerrainTypeToLayer.OrderBy(p => p.Key), actual.TerrainTypeToLayer.OrderBy(p => p.Key));
        Assert.Equal(expected.LayerCount, actual.LayerCount);
        Assert.Equal(expected.TilingByLayer, actual.TilingByLayer);
        Assert.Equal(expected.AlphaLayerCount, actual.AlphaLayerCount);
        Assert.Equal(expected.CornerAlphaLayers, actual.CornerAlphaLayers);
        Assert.Equal(expected.SideAlphaLayers, actual.SideAlphaLayers);
        Assert.Equal(expected.RoadAlphaLayers, actual.RoadAlphaLayers);
        Assert.Equal(expected.CornerAlphaTCodes, actual.CornerAlphaTCodes);
        Assert.Equal(expected.SideAlphaTCodes, actual.SideAlphaTCodes);
        Assert.Equal(expected.RoadAlphaRCodes, actual.RoadAlphaRCodes);
        Assert.Equal(expected.BuildingDetailTexture.SurfaceTextureId, actual.BuildingDetailTexture.SurfaceTextureId);
        Assert.Equal(expected.EnvironmentDetailTexture.SurfaceTextureId, actual.EnvironmentDetailTexture.SurfaceTextureId);
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

    /// <summary>Passes every call through, noting the type of each object read.</summary>
    public class CountingDats : DispatchProxy
    {
        private IDatReaderWriter _inner = null!;

        public List<Type> Reads { get; } = [];

        public static CountingDats Over(IDatReaderWriter inner)
        {
            object proxy = Create<IDatReaderWriter, CountingDats>();
            var counting = (CountingDats)proxy;
            counting._inner = inner;
            return counting;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.IsGenericMethod && targetMethod.Name is "Get" or "TryGet")
            {
                lock (Reads)
                    Reads.Add(targetMethod.GetGenericArguments()[0]);
            }
            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error.InnerException);
                throw;
            }
        }
    }
}
