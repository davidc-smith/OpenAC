using AcDream.Core.Plugins;
using AcDream.Core.Textures;
using AcDream.Tests.Fixtures.PluginIcons;

namespace AcDream.Core.Tests.Plugins;

public sealed class PluginImageFileTests
{
    [Theory]
    [InlineData("icons/sword.png")]
    [InlineData(@"icons\sword.png")]
    [InlineData("icons/shield.JPG")]
    [InlineData("icons/helm.jpeg")]
    public void ResolvePathAcceptsAPngOrJpegInsideTheFolder(string relative)
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "icons"));
        string name = Path.GetFileName(relative.Replace('\\', '/'));
        File.WriteAllBytes(Path.Combine(directory.Path, "icons", name), [1]);

        Assert.True(PluginImageFile.TryResolvePath(directory.Path, relative, out string? full, out string? reason), reason);
        Assert.EndsWith(name, full, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../outside.png", "points outside")]
    [InlineData("/etc/icon.png", "points outside")]
    [InlineData("icons/loot.svg", "not a .png or .jpg")]
    [InlineData("icons/loot.gif", "not a .png or .jpg")]
    [InlineData("missing.png", "does not exist")]
    public void ResolvePathRejectsAnythingElse(string relative, string named)
    {
        using var outer = new TemporaryDirectory();
        string folder = Path.Combine(outer.Path, "plugin");
        Directory.CreateDirectory(Path.Combine(folder, "icons"));
        File.WriteAllBytes(Path.Combine(outer.Path, "outside.png"), PngTestData.Sized(4, 4));
        File.WriteAllBytes(Path.Combine(folder, "icons", "loot.svg"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "icons", "loot.gif"), [1]);

        Assert.False(PluginImageFile.TryResolvePath(folder, relative, out _, out string? reason));
        Assert.Contains(named, reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadsAPngOfAnySizeUpToTheLimit()
    {
        using var directory = new TemporaryDirectory();
        string path = Write(directory, "wide.png", PngTestData.Sized(48, 16));

        Assert.True(PluginImageFile.TryLoad(path, out DecodedTexture? texture, out string? reason), reason);
        Assert.Equal(48, texture.Width);
        Assert.Equal(16, texture.Height);
        Assert.Equal(48 * 16 * 4, texture.Rgba8.Length);
    }

    [Fact]
    public void LoadsAJpegAsOpaqueRgba()
    {
        using var directory = new TemporaryDirectory();
        string path = Write(directory, "red.jpg", JpegTestData.Red());

        Assert.True(PluginImageFile.TryLoad(path, out DecodedTexture? texture, out string? reason), reason);
        Assert.Equal(JpegTestData.Width, texture.Width);
        Assert.Equal(JpegTestData.Height, texture.Height);
        Assert.Equal(255, texture.Rgba8[3]);
        Assert.True(texture.Rgba8[0] > 150 && texture.Rgba8[1] < 100, "the first pixel should be red");
    }

    [Fact]
    public void RefusesAnImageWiderThanTheLimitBeforeDecodingIt()
    {
        using var directory = new TemporaryDirectory();
        string path = Write(directory, "big.png", PngTestData.Sized(PluginImageFile.MaximumDimension + 1, 1));

        Assert.False(PluginImageFile.TryLoad(path, out _, out string? reason));
        Assert.Contains("512", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFileLargerThanTheByteLimit()
    {
        using var directory = new TemporaryDirectory();
        string path = Write(directory, "huge.png", new byte[PluginImageFile.MaximumBytes + 1]);

        Assert.False(PluginImageFile.TryLoad(path, out _, out string? reason));
        Assert.Contains("1 MiB", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFileThatIsNeitherPngNorJpegWhateverItsName()
    {
        using var directory = new TemporaryDirectory();
        string path = Write(directory, "fake.png", "GIF89a not really"u8.ToArray());

        Assert.False(PluginImageFile.TryLoad(path, out _, out string? reason));
        Assert.Contains("not a PNG or JPEG", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPngWhosePixelsDoNotDecode()
    {
        using var directory = new TemporaryDirectory();
        byte[] png = PngTestData.Sized(4, 4);
        Array.Resize(ref png, png.Length - 20);
        string path = Write(directory, "cut.png", png);

        Assert.False(PluginImageFile.TryLoad(path, out _, out string? reason));
        Assert.Contains("could not be decoded", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAMissingFileWithoutThrowing()
    {
        using var directory = new TemporaryDirectory();

        Assert.False(PluginImageFile.TryLoad(Path.Combine(directory.Path, "gone.png"), out _, out string? reason));
        Assert.Contains("could not be read", reason, StringComparison.Ordinal);
    }

    private static string Write(TemporaryDirectory directory, string name, byte[] bytes)
    {
        string path = Path.Combine(directory.Path, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"acdream-plugin-image-{Guid.NewGuid():N}");
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
