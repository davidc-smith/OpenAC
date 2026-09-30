using System.Collections.Generic;
using System.Threading;
using AcDream.App.Plugins;
using AcDream.App.Tests.UI;
using AcDream.App.UI;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.Tests.Plugins;

/// <summary>
/// One plugin's fonts: counted once per distinct font, size and characters,
/// held as often as asked, refused past the budget with one report, and let
/// go when the interface goes.
/// </summary>
public sealed class PluginFontTableTests
{
    internal sealed class FakeFontBackend : IPluginFontBackend
    {
        public const uint FirstTexture = 900u;
        private uint _next = FirstTexture;
        public List<(uint Texture, int Width, int Height)> Uploaded { get; } = [];
        public List<uint> Released { get; } = [];

        public uint UploadCoverage(byte[] coverage, int width, int height, string debugName)
        {
            uint texture = _next++;
            Uploaded.Add((texture, width, height));
            return texture;
        }

        public bool ReleaseCoverage(uint texture)
        {
            Released.Add(texture);
            return true;
        }
    }

    private sealed class Rig
    {
        public FakeFontBackend Backend { get; } = new();
        public BundledCanvasFontCache Bundled { get; }
        public List<string> Reports { get; } = [];
        public PluginFontTable Table { get; }

        public Rig(PluginFontBudget? budget = null)
        {
            Bundled = new BundledCanvasFontCache(Backend, BundledUiFont.ReadEmbeddedFontBytes, 2048);
            Table = new PluginFontTable("example.plugin", budget ?? PluginFontBudget.Default, Reports.Add);
            Table.Bind(Backend, Bundled, Environment.CurrentManagedThreadId);
        }
    }

    private static Func<Stream> Otf(Action? opened = null) => () =>
    {
        opened?.Invoke();
        return new MemoryStream(CanvasFontBakerTests.CffFixture());
    };

    [Fact]
    public void UnboundEveryRequestAnswersNoFont()
    {
        var table = new PluginFontTable("example.plugin", PluginFontBudget.Default);
        bool opened = false;

        Assert.Equal(PluginFont.None, table.AcquireBundled(16f));
        Assert.Equal(PluginFont.None, table.AcquireStream("f.otf", Otf(() => opened = true), 16f, null));
        Assert.False(opened);
        Assert.False(table.IsBound);
    }

    [Fact]
    public void TheSameBundledSizeIsOneHandleHeldTwice()
    {
        var rig = new Rig();

        PluginFont first = rig.Table.AcquireBundled(16f);
        PluginFont second = rig.Table.AcquireBundled(16f);

        Assert.True(first.IsValid);
        Assert.Equal(first, second);
        Assert.Equal(16f, first.PixelSize);
        Assert.True(first.LineHeight >= 16f);
        Assert.Equal(1, rig.Table.Count);
        Assert.Single(rig.Backend.Uploaded);
        Assert.True(rig.Table.Release(first));
        Assert.True(rig.Table.TryResolve(first, out _));
        Assert.True(rig.Table.Release(first));
        Assert.False(rig.Table.TryResolve(first, out _));
        Assert.False(rig.Table.Release(first));
        Assert.Equal(0L, rig.Table.OwnedBytes);
    }

    [Fact]
    public void APluginFontIsReadOnceCountedAndGivenBack()
    {
        var rig = new Rig();
        int opens = 0;

        PluginFont font = rig.Table.AcquireStream("fonts/fixture.otf", Otf(() => opens++), 16f, null);
        PluginFont again = rig.Table.AcquireStream("fonts/fixture.otf", Otf(() => opens++), 16f, null);

        Assert.True(font.IsValid);
        Assert.Equal(font, again);
        Assert.Equal(1, opens);
        (uint texture, int width, int height) = Assert.Single(rig.Backend.Uploaded);
        long expected = CanvasFontBakerTests.CffFixture().Length + (long)width * height;
        Assert.Equal(expected, rig.Table.OwnedBytes);

        rig.Table.Release(font);
        rig.Table.Release(font);

        Assert.Equal([texture], rig.Backend.Released);
        Assert.Equal(0L, rig.Table.OwnedBytes);
    }

    [Fact]
    public void ADifferentSizeOrRangeIsADifferentFont()
    {
        var rig = new Rig();

        PluginFont sixteen = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);
        PluginFont twenty = rig.Table.AcquireStream("f.otf", Otf(), 20f, null);
        PluginFont capsOnly = rig.Table.AcquireStream("f.otf", Otf(), 16f, [new PluginCodepointRange('A', 'Z')]);

        Assert.Equal(3, new[] { sixteen.Handle, twenty.Handle, capsOnly.Handle }.Distinct().Count());
        Assert.Equal(3, rig.Table.Count);
    }

    [Theory]
    [InlineData(5.9f)]
    [InlineData(64.1f)]
    [InlineData(float.NaN)]
    public void ASizeOutsideTheBudgetIsRefusedAndReportedOnce(float size)
    {
        var rig = new Rig();

        Assert.Equal(PluginFont.None, rig.Table.AcquireBundled(size));
        Assert.Equal(PluginFont.None, rig.Table.AcquireBundled(size));

        Assert.Single(rig.Reports);
        Assert.Contains("6 to 64", rig.Reports[0]);
    }

    [Fact]
    public void TheSeventeenthFontIsRefused()
    {
        var rig = new Rig();
        for (int size = 6; size < 22; size++)
            Assert.True(rig.Table.AcquireBundled(size).IsValid);

        Assert.Equal(PluginFont.None, rig.Table.AcquireBundled(40f));
        Assert.Contains(rig.Reports, line => line.Contains("16 fonts"));
    }

    [Fact]
    public void AFontPastTheByteBudgetIsRefused()
    {
        var rig = new Rig(PluginFontBudget.Default with { MaximumBytes = 1024 });

        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("f.otf", Otf(), 16f, null));
        Assert.Contains(rig.Reports, line => line.Contains("budget"));
        Assert.Empty(rig.Backend.Uploaded);
    }

    [Fact]
    public void StreamsThatFailAreRefusedNotThrown()
    {
        var rig = new Rig();

        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("throws.ttf", () => throw new IOException("gone"), 16f, null));
        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("null.ttf", () => null!, 16f, null));
        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("junk.ttf", () => new MemoryStream(new byte[64]), 16f, null));
        Assert.Equal(PluginFont.None, rig.Table.AcquireStream("icons.otf", Otf(), 16f, [new PluginCodepointRange(0xE000, 0xE0FF)]));

        Assert.Equal(4, rig.Reports.Count);
        Assert.Contains(rig.Reports, line => line.Contains("gone"));
        Assert.Contains(rig.Reports, line => line.Contains("not a TrueType or OpenType font"));
        Assert.Contains(rig.Reports, line => line.Contains("none of the requested characters"));
    }

    [Fact]
    public void AnotherThreadIsRefusedWhileBound()
    {
        var rig = new Rig();
        Exception? failure = null;

        var worker = new Thread(() =>
        {
            try { rig.Table.AcquireBundled(16f); }
            catch (Exception caught) { failure = caught; }
        });
        worker.Start();
        worker.Join();

        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public void UnbindingLetsEveryFontGo()
    {
        var rig = new Rig();
        PluginFont bundled = rig.Table.AcquireBundled(16f);
        PluginFont own = rig.Table.AcquireStream("f.otf", Otf(), 16f, null);

        rig.Table.Unbind();

        Assert.False(rig.Table.TryResolve(bundled, out _));
        Assert.False(rig.Table.TryResolve(own, out _));
        Assert.Equal(0, rig.Table.Count);
        Assert.Equal(0, rig.Bundled.HeldCount);
        Assert.Equal(2, rig.Backend.Released.Count);
    }
}
