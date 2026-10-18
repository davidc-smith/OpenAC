using AcDream.Core.Plugins;
using AcDream.Core.Textures;
using AcDream.Tests.Fixtures.PluginIcons;

namespace AcDream.Core.Tests.Plugins;

public sealed class PluginIconFileTests
{
    [Fact]
    public void LoadsAValidIcon()
    {
        using var directory = new TemporaryDirectory();
        WriteIcon(directory.Path, PngTestData.Valid());

        Assert.True(PluginIconFile.TryLoad(directory.Path, out DecodedTexture? texture));
        Assert.Equal(64, texture.Width);
        Assert.Equal(64, texture.Height);
        Assert.Equal(64 * 64 * 4, texture.Rgba8.Length);
    }

    [Fact]
    public void FailsWithNoIconFile()
    {
        using var directory = new TemporaryDirectory();

        Assert.False(PluginIconFile.TryLoad(directory.Path, out _));
    }

    [Fact]
    public void FailsWhenOverMaximumBytes()
    {
        using var directory = new TemporaryDirectory();
        WriteIcon(directory.Path, PngTestData.OverMaximumBytes());

        Assert.False(PluginIconFile.TryLoad(directory.Path, out _));
    }

    [Fact]
    public void FailsWithTheWrongDimensions()
    {
        using var directory = new TemporaryDirectory();
        WriteIcon(directory.Path, PngTestData.WrongDimensions());

        Assert.False(PluginIconFile.TryLoad(directory.Path, out _));
    }

    [Fact]
    public void FailsWhenAnimated()
    {
        using var directory = new TemporaryDirectory();
        WriteIcon(directory.Path, PngTestData.AnimatedControl());

        Assert.False(PluginIconFile.TryLoad(directory.Path, out _));
    }

    [Fact]
    public void FailsWithBytesAfterIend()
    {
        using var directory = new TemporaryDirectory();
        WriteIcon(directory.Path, PngTestData.TrailingBytesAfterIend());

        Assert.False(PluginIconFile.TryLoad(directory.Path, out _));
    }

    [Fact]
    public void FailsWithCorruptPixelDataThatPassesTheHeaderRules()
    {
        using var directory = new TemporaryDirectory();
        byte[] bytes = PngTestData.CorruptPixelData();
        PluginIconFile.ValidateHeader(bytes);
        WriteIcon(directory.Path, bytes);

        Assert.False(PluginIconFile.TryLoad(directory.Path, out _));
    }

    [Fact]
    public void RefusesAnInflateBombWithoutAllocatingItsExpandedSize()
    {
        using var directory = new TemporaryDirectory();
        WriteIcon(directory.Path, PngTestData.InflateBomb());

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool loaded = PluginIconFile.TryLoad(directory.Path, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.False(loaded);
        Assert.True(
            allocated < 1_000_000,
            $"expected well under the bomb's 4,000,000-byte expansion, allocated {allocated}");
    }

    [Fact]
    public void ResolvePathAcceptsARelativePngInsideTheFolder()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, "icons"));
        File.WriteAllBytes(System.IO.Path.Combine(directory.Path, "icons", "combat.png"), PngTestData.Valid());

        Assert.True(PluginIconFile.TryResolvePath(directory.Path, @"icons\combat.png", out string? full, out string? reason), reason);
        Assert.EndsWith("combat.png", full, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/etc/icon.png", "outside")]
    [InlineData("C:/icon.png", "outside")]
    [InlineData("../outside.png", "outside")]
    [InlineData("icons/../../outside.png", "outside")]
    [InlineData("icons/combat.svg", "is not a .png file")]
    [InlineData("icons/combat.jpg", "is not a .png file")]
    [InlineData("icons/missing.png", "does not exist")]
    public void ResolvePathRejectsAnythingElse(string iconFile, string named)
    {
        using var outer = new TemporaryDirectory();
        string folder = System.IO.Path.Combine(outer.Path, "plugin");
        Directory.CreateDirectory(System.IO.Path.Combine(folder, "icons"));
        File.WriteAllBytes(System.IO.Path.Combine(outer.Path, "outside.png"), PngTestData.Valid());
        File.WriteAllText(System.IO.Path.Combine(folder, "icons", "combat.svg"), "x");
        File.WriteAllText(System.IO.Path.Combine(folder, "icons", "combat.jpg"), "x");

        Assert.False(PluginIconFile.TryResolvePath(folder, iconFile, out _, out string? reason));
        Assert.Contains(named, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePathRejectsALinkThatPointsOutside()
    {
        using var outer = new TemporaryDirectory();
        string folder = System.IO.Path.Combine(outer.Path, "plugin");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(System.IO.Path.Combine(outer.Path, "outside.png"), PngTestData.Valid());
        if (!TryLink(() => File.CreateSymbolicLink(System.IO.Path.Combine(folder, "via.png"), "../outside.png"))) return;

        Assert.False(PluginIconFile.TryResolvePath(folder, "via.png", out _, out string? reason));
        Assert.Contains("outside", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvePathAcceptsALinkThatStaysInside()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, "real"));
        File.WriteAllBytes(System.IO.Path.Combine(directory.Path, "real", "combat.png"), PngTestData.Valid());
        if (!TryLink(() => File.CreateSymbolicLink(System.IO.Path.Combine(directory.Path, "combat.png"), "real/combat.png"))) return;

        Assert.True(PluginIconFile.TryResolvePath(directory.Path, "combat.png", out string? full, out string? reason), reason);
        Assert.EndsWith(System.IO.Path.Combine("real", "combat.png"), full, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFileDecodesAValidIconAnywhereInTheFolder()
    {
        using var directory = new TemporaryDirectory();
        string path = System.IO.Path.Combine(directory.Path, "combat.png");
        File.WriteAllBytes(path, PngTestData.Valid());

        Assert.True(PluginIconFile.TryLoadFile(path, out DecodedTexture? texture, out string? reason), reason);
        Assert.Equal((64, 64, 64 * 64 * 4), (texture.Width, texture.Height, texture.Rgba8.Length));
    }

    public static TheoryData<string, string> RefusedFiles() => new()
    {
        { nameof(PngTestData.WrongDimensions), "64x64" },
        { nameof(PngTestData.OverMaximumBytes), "64 KiB" },
        { nameof(PngTestData.NoSignature), "not a PNG" },
        { nameof(PngTestData.AnimatedControl), "animated" },
        { nameof(PngTestData.CorruptPixelData), "decoded" },
    };

    [Theory]
    [MemberData(nameof(RefusedFiles))]
    public void LoadFileRefusesWhatIconPngRefusesWithAReason(string fixture, string named)
    {
        using var directory = new TemporaryDirectory();
        string path = System.IO.Path.Combine(directory.Path, "combat.png");
        File.WriteAllBytes(path, fixture switch
        {
            nameof(PngTestData.WrongDimensions) => PngTestData.WrongDimensions(),
            nameof(PngTestData.OverMaximumBytes) => PngTestData.OverMaximumBytes(),
            nameof(PngTestData.NoSignature) => PngTestData.NoSignature(),
            nameof(PngTestData.AnimatedControl) => PngTestData.AnimatedControl(),
            _ => PngTestData.CorruptPixelData(),
        });

        Assert.False(PluginIconFile.TryLoadFile(path, out _, out string? reason));
        Assert.Contains(named, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadFileReportsAMissingFile()
    {
        using var directory = new TemporaryDirectory();

        Assert.False(PluginIconFile.TryLoadFile(System.IO.Path.Combine(directory.Path, "gone.png"), out _, out string? reason));
        Assert.Equal("does not exist", reason);
    }

    private static bool TryLink(Action create)
    {
        try
        {
            create();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false; // Symlinks need a privilege on some Windows machines.
        }
    }

    private static void WriteIcon(string pluginDirectory, byte[] bytes) =>
        File.WriteAllBytes(Path.Combine(pluginDirectory, PluginIconFile.FileName), bytes);

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"acdream-plugin-icon-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
