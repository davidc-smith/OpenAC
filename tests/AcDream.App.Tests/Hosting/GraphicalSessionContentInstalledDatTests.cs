using AcDream.App.Hosting;
using AcDream.App.Tests.UI.Layout;
using AcDream.Core.Plugins;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The session host opens the game files for the process, before any window:
/// what the session reads from them is installed, and its collision is built
/// from them with nothing drawn.
/// </summary>
[Trait("Lane", "InstalledDat")]
public sealed class GraphicalSessionContentInstalledDatTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "acdream-session-content-" + Guid.NewGuid().ToString("N"));

    public GraphicalSessionContentInstalledDatTests() =>
        Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [InstalledDatFact]
    public void The_host_holds_the_game_files_and_builds_collision_from_them_with_no_window()
    {
        string datDirectory = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "Asheron's Call");
        string? package = Environment.GetEnvironmentVariable("ACDREAM_PAK_PATH");
        Assert.True(File.Exists(package), "Lane=InstalledDat needs ACDREAM_PAK_PATH here.");
        RuntimeOptions options = RuntimeOptions.Parse(datDirectory, _ => null) with
        {
            SessionId = "content",
            StatusFilePath = Path.Combine(_directory, "status.jsonl"),
            PreparedAssetPath = package!,
        };
        using var host = new GraphicalSessionHost(
            options,
            new WorldGameState(),
            new WorldEvents());
        Assert.False(host.Collision.HasContent);

        host.UseContent(GraphicalSessionContent.Open(options, _ => { }));

        Assert.Null(host.Window);
        Assert.NotNull(host.Content);
        Assert.True(host.Collision.HasContent);
        Assert.True(host.Runtime.CharacterOwner.Spellbook.Metadata.Count > 0);
        Assert.NotEmpty(host.Runtime.Session.CharacterCreationState.Options.HeritagesById);
        Assert.Throws<InvalidOperationException>(
            () => host.UseContent(host.Content!));
    }

    [InstalledDatFact]
    public void A_prepared_package_that_cannot_open_fails_the_open_with_its_own_error()
    {
        // The spell and height tables are read on a worker while the prepared
        // package opens; a package that cannot open must still be the error
        // reported, with the worker finished before the game files close.
        string datDirectory = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents",
                "Asheron's Call");
        RuntimeOptions options = RuntimeOptions.Parse(datDirectory, _ => null) with
        {
            SessionId = "content",
            StatusFilePath = Path.Combine(_directory, "status.jsonl"),
            PreparedAssetPath = Path.Combine(_directory, "missing.pak"),
        };

        Assert.ThrowsAny<IOException>(() => GraphicalSessionContent.Open(options, _ => { }));
    }
}
