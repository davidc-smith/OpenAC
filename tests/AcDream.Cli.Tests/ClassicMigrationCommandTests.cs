using AcDream.Cli;

namespace AcDream.Cli.Tests;

public sealed class ClassicMigrationCommandTests
{
    [Fact]
    public void ADryRunWritesNothing()
    {
        using var directory = new TemporaryDirectory();
        string vtank = directory.Create("VirindiTank");
        Write(vtank, "Hunt.met", "meta");
        string root = Path.Combine(directory.Path, "openac");
        var output = new StringWriter();

        int exit = ClassicMigrationCommand.Run(
            ["--vtank", vtank, "--root", root, "--dry-run"],
            output,
            new StringWriter());

        Assert.Equal(0, exit);
        Assert.Contains("Would copy 1 file(s)", output.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void AMissingSourceFolderIsRefused()
    {
        using var directory = new TemporaryDirectory();
        var error = new StringWriter();

        int exit = ClassicMigrationCommand.Run(
            ["--vtank", Path.Combine(directory.Path, "missing"), "--root", directory.Path],
            new StringWriter(),
            error);

        Assert.Equal(2, exit);
        Assert.Contains("does not exist", error.ToString(), StringComparison.Ordinal);
    }

    private static void Write(string folder, string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "acdream-cli-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Create(params string[] parts)
        {
            string path = System.IO.Path.Combine([Path, .. parts]);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
