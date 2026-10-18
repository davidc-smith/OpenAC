using AcDream.App.Extensions.Overlay;
using AcDream.App.Rendering;
using AcDream.App.Tests.Rendering;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using AcDream.Content;
using AcDream.Core.Items;
using DatReaderWriter;
using DatReaderWriter.Options;

namespace AcDream.App.Tests.Extensions;

[Trait("Lane", "InstalledDat")]
public sealed class OverlayIconsInstalledDatTests
{
    [Fact]
    public void RealIconsAndSpellIconsResolveToIssuedIds()
    {
        string? datDir = InstalledDatTestPath.Resolve();
        if (datDir is null)
        {
            Assert.Fail("Lane=InstalledDat requires an installed retail DAT directory; see docs/release-gate.md.");
            return;
        }

        using var dats = new DatCollection(datDir, DatAccessType.Read);
        using var adapter = new DatCollectionAdapter(dats);
        var device = new RecordingGpuDevice();
        using var cache = new TextureCache(device, adapter);
        var composer = new IconComposer(adapter, cache);
        var backend = new RetailPluginImageBackend(adapter, new object(), cache, composer, new ClientObjectTable());
        var log = new List<string>();
        var icons = new OverlayIcons(log.Add);
        icons.Bind(backend);

        IntPtr icon = icons.Icon(0x06002C41u);
        IntPtr spell = icons.SpellIcon(1);

        Assert.NotEqual(IntPtr.Zero, icon);
        Assert.NotEqual(IntPtr.Zero, spell);
        Assert.True(icons.IsIssued(icon));
        Assert.True(icons.IsIssued(spell));
        Assert.Empty(log);
    }
}
