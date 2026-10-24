using System;
using System.Collections.Generic;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Options;
using DatReaderWriter.Types;

namespace AcDream.Content.Tests;

/// <summary>
/// The bulk reader must decode textures and palettes to exactly what the
/// package's own field-by-field reader produces.
/// </summary>
public sealed class DatBulkObjectReaderTests
{
    [Theory]
    [InlineData(PixelFormat.PFID_P8, true)]
    [InlineData(PixelFormat.PFID_INDEX16, true)]
    [InlineData(PixelFormat.PFID_A8R8G8B8, false)]
    [InlineData(PixelFormat.PFID_DXT5, false)]
    public void ARenderSurfaceDecodesLikeThePackageReader(PixelFormat format, bool hasPalette)
    {
        var source = new RenderSurface
        {
            Id = 0x06001234,
            DataCategory = 7,
            Width = 3,
            Height = 5,
            Format = format,
            SourceData = Pattern(3 * 5 * 4, seed: 11),
            DefaultPaletteId = hasPalette ? 0x04000ABCu : 0u,
        };
        byte[] bytes = Pack(source);

        RenderSurface expected = Unpack<RenderSurface>(bytes, source.Id);
        RenderSurface actual = DatBulkObjectReader.ReadRenderSurface(bytes, source.Id);

        AssertSame(expected, actual);
        Assert.Equal(hasPalette ? 0x04000ABCu : 0u, actual.DefaultPaletteId);
    }

    [Fact]
    public void ARenderSurfaceTakesTheRequestedIdAsThePackageReaderDoes()
    {
        var source = new RenderSurface
        {
            Id = 0x06000001,
            Width = 1,
            Height = 1,
            Format = PixelFormat.PFID_R8G8B8,
            SourceData = [1, 2, 3],
        };
        byte[] bytes = Pack(source);

        RenderSurface actual = DatBulkObjectReader.ReadRenderSurface(bytes, 0x06000002);

        Assert.Equal(0x06000002u, actual.Id);
        AssertSame(Unpack<RenderSurface>(bytes, 0x06000002), actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2048)]
    public void APaletteDecodesLikeThePackageReader(int count)
    {
        var source = new Palette { Id = 0x04000ABC };
        byte[] channels = Pattern(count * 4, seed: 3);
        for (int i = 0; i < count; i++)
        {
            source.Colors.Add(new ColorARGB
            {
                Blue = channels[i * 4],
                Green = channels[i * 4 + 1],
                Red = channels[i * 4 + 2],
                Alpha = channels[i * 4 + 3],
            });
        }
        byte[] bytes = Pack(source);

        Palette expected = Unpack<Palette>(bytes, source.Id);
        Palette actual = DatBulkObjectReader.ReadPalette(bytes, source.Id);

        AssertSame(expected, actual);
    }

    [Fact]
    public void ATruncatedRecordThrowsAsThePackageReaderDoes()
    {
        var source = new RenderSurface
        {
            Id = 0x06000001,
            Width = 2,
            Height = 2,
            Format = PixelFormat.PFID_P8,
            SourceData = Pattern(4, seed: 1),
            DefaultPaletteId = 0x04000001,
        };
        byte[] bytes = Pack(source).AsSpan(0, 20).ToArray();

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => Unpack<RenderSurface>(bytes, 0x06000001));
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => DatBulkObjectReader.ReadRenderSurface(bytes, 0x06000001));
    }

    /// <summary>
    /// Every palette and a spread of textures in the installed portal and
    /// high-resolution files decode identically through the content seam and
    /// through the package's own reader.
    /// </summary>
    [Fact]
    [Trait("Lane", "InstalledDat")]
    public void TheInstalledFilesDecodeIdenticallyThroughTheSeam()
    {
        string? datDir = ContentConformanceDats.ResolveDatDir();
        if (datDir is null)
        {
            Assert.Fail(
                "Lane=InstalledDat requires an installed retail DAT directory; "
                + "see docs/release-gate.md.");
            return;
        }

        using var dats = new DatCollection(datDir, DatAccessType.Read);
        using var adapter = new DatCollectionAdapter(dats);
        int surfaces = 0;
        int palettes = 0;
        foreach ((DatDatabase raw, IDatDatabase seam) in new[]
                 {
                     (dats.Portal, adapter.Portal),
                     (dats.HighRes, adapter.HighRes),
                 })
        {
            int index = 0;
            foreach (uint id in raw.GetAllIdsOfType<RenderSurface>())
            {
                // Every 7th texture keeps the lane quick; formats are interleaved by id.
                if (index++ % 7 != 0) continue;
                Assert.True(raw.TryGet<RenderSurface>(id, out var expected));
                Assert.True(seam.TryGet<RenderSurface>(id, out var actual));
                AssertSame(expected!, actual!);
                surfaces++;
            }

            foreach (uint id in raw.GetAllIdsOfType<Palette>())
            {
                Assert.True(raw.TryGet<Palette>(id, out var expected));
                Assert.True(seam.TryGet<Palette>(id, out var actual));
                AssertSame(expected!, actual!);
                palettes++;
            }
        }

        Assert.True(surfaces > 100, $"only {surfaces} textures compared");
        Assert.True(palettes > 100, $"only {palettes} palettes compared");
    }

    private static void AssertSame(RenderSurface expected, RenderSurface actual)
    {
        Assert.NotSame(expected, actual);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.DataCategory, actual.DataCategory);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Format, actual.Format);
        Assert.Equal(expected.DefaultPaletteId, actual.DefaultPaletteId);
        Assert.Equal(expected.SourceData, actual.SourceData);
    }

    private static void AssertSame(Palette expected, Palette actual)
    {
        Assert.NotSame(expected, actual);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.DataCategory, actual.DataCategory);
        Assert.Equal(expected.Colors.Count, actual.Colors.Count);
        for (int i = 0; i < expected.Colors.Count; i++)
        {
            ColorARGB e = expected.Colors[i];
            ColorARGB a = actual.Colors[i];
            Assert.Equal(
                (e.Alpha, e.Red, e.Green, e.Blue),
                (a.Alpha, a.Red, a.Green, a.Blue));
        }
    }

    private static byte[] Pack(IDBObj source)
    {
        var buffer = new byte[1 << 16];
        var writer = new DatBinWriter(buffer);
        Assert.True(source.Pack(writer));
        return buffer.AsSpan(0, writer.Offset).ToArray();
    }

    private static T Unpack<T>(byte[] bytes, uint fileId) where T : IDBObj, new()
    {
        var value = new T();
        Assert.True(value.Unpack(new DatBinReader(bytes)));
        // The package's database read stamps the requested id last.
        value.Id = fileId;
        return value;
    }

    private static byte[] Pattern(int length, int seed)
    {
        var bytes = new byte[length];
        var random = new Random(seed);
        random.NextBytes(bytes);
        return bytes;
    }
}
