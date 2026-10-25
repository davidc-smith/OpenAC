using System;
using System.Collections.Generic;
using System.Linq;
using AcDream.App.Rendering;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.Content;
using DatReaderWriter;
using DatReaderWriter.Options;
using DatReaderWriter.Types;

namespace AcDream.App.Tests.UI;

/// <summary>
/// A font's glyph atlases are uploaded the first time its text is drawn,
/// not when the font is loaded.
/// </summary>
public sealed class UiDatFontLazyAtlasTests
{
    [Fact]
    public void TheAtlasesAreUploadedOnceOnFirstUse()
    {
        int uploads = 0;
        var font = new UiDatFont(
            () =>
            {
                uploads++;
                return (new UiDatFont.Atlas(7u, 256, 128), new UiDatFont.Atlas(8u, 64, 32));
            },
            lineHeight: 16f,
            baselineOffset: 12f,
            glyphs: new Dictionary<char, FontCharDesc> { ['A'] = new FontCharDesc { Unicode = 'A', Width = 9 } });

        Assert.Equal(16f, font.LineHeight);
        Assert.Equal(9f, font.MeasureWidth("A"));
        Assert.Equal(0, uploads);

        Assert.Equal(7u, font.ForegroundTexture);
        Assert.Equal((256, 128), (font.ForegroundWidth, font.ForegroundHeight));
        Assert.True(font.HasBackground);
        Assert.Equal((8u, 64, 32), (font.BackgroundTexture, font.BackgroundWidth, font.BackgroundHeight));
        Assert.Equal(1, uploads);
    }

    [Fact]
    [Trait("Lane", "InstalledDat")]
    public void LoadingAnInstalledFontUploadsNothingUntilItIsDrawn()
    {
        string? datDir = ContentDatDir();
        if (datDir is null)
            Assert.Fail("Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");

        using var dats = new DatCollection(datDir, DatAccessType.Read);
        using var adapter = new DatCollectionAdapter(dats);
        var device = new RecordingGpuDevice();
        var cache = new TextureCache(device, adapter);
        var datLock = new object();
        // The cache's own placeholder texture is there from the start.
        int before = device.OfKind<GpuRecordedTextureRegistration>().Count();

        UiDatFont? font = UiDatFont.Load(adapter, cache, UiDatFont.DefaultFontId, datLock);

        Assert.NotNull(font);
        Assert.True(font!.TryGetGlyph('A', out _));
        Assert.Equal(before, device.OfKind<GpuRecordedTextureRegistration>().Count());

        Assert.NotEqual(0u, font.ForegroundTexture);
        Assert.True(font.ForegroundWidth > 0 && font.ForegroundHeight > 0);
        int registered = device.OfKind<GpuRecordedTextureRegistration>().Count();
        Assert.True(registered > before);
        _ = font.BackgroundTexture;
        Assert.Equal(registered, device.OfKind<GpuRecordedTextureRegistration>().Count());
    }

    private static string? ContentDatDir()
    {
        string? fromEnv = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv) && System.IO.Directory.Exists(fromEnv)) return fromEnv;
        string def = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Asheron's Call");
        return System.IO.Directory.Exists(def) ? def : null;
    }
}
