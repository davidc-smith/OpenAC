using System.Reflection;
using AcDream.Rynth.Host;
using AcDream.TestSupport;

namespace AcDream.Rynth.Host.Tests;

public sealed class RynthAiPluginTests : IDisposable
{
    private readonly List<string> _log = new();
    private readonly RynthConfigSandbox _sandbox = new("rynth-ai-");
    private readonly FakeGameView _view = new() { PlayerId = 0x50000001u };

    public void Dispose() => _sandbox.Dispose();

    private RynthHost CreateAiHost() =>
        new(_view, _log.Add, [typeof(RynthCore.Plugin.RynthAi.PluginExports)]);

    private static Assembly Theirs => typeof(RynthCore.Plugin.RynthAi.PluginExports).Assembly;

    [Fact]
    public void RynthAiLoadsAndTicks()
    {
        using RynthHost host = CreateAiHost();
        RynthPlugin ai = Assert.Single(host.Plugins);
        Assert.True(ai.IsActive);
        Assert.Contains("RynthAi", ai.Name, StringComparison.Ordinal);

        for (int i = 0; i < 30; i++)
            host.Tick();

        Assert.True(ai.IsActive);
        Assert.DoesNotContain(_log, line => line.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOwnerCarriesTheVersionTheExportReports()
    {
        using RynthHost host = CreateAiHost();
        RynthPlugin ai = host.Plugins[0];
        string? version = ai.CallStringExport("RynthPluginVersion");
        Assert.False(string.IsNullOrEmpty(version));
        Assert.Equal(version, ai.Owner.VersionString);
    }

    [Fact]
    public void TheSnapshotAnswersBeforeLogin()
    {
        using RynthHost host = CreateAiHost();
        string? json = host.Plugins[0].CallStringExport("RynthPluginGetSnapshotJson");
        Assert.NotNull(json);
        Assert.Contains("\"macroRunning\":false", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RynthCore.Plugin.RynthAi.Combat.SpellData.txt")]
    [InlineData("RynthCore.Plugin.RynthAi.CreatureData.creature_resists.tsv")]
    [InlineData("RynthCore.Plugin.RynthAi.CreatureData.creature_types.txt")]
    [InlineData("RynthCore.Plugin.RynthAi.Data.creature_weakness.json")]
    public void TheirResourcesKeepTheirNames(string name) =>
        Assert.Contains(name, Theirs.GetManifestResourceNames());

    [Fact]
    public void OurVariableNamesAreTheirs()
    {
        Type paths = Theirs.GetType("RynthCore.Install.RynthInstallPaths", throwOnError: true)!;
        Assert.Equal(paths.GetField("CoreEnvironmentVariable")!.GetValue(null), RynthInstallFolders.CoreVariable);
        Assert.Equal(paths.GetField("SuiteEnvironmentVariable")!.GetValue(null), RynthInstallFolders.SuiteVariable);
    }

    [Fact]
    public void OffWindowsUnsetFoldersGoUnderApplicationData()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal(Path.Combine("/home/me/.config", "RynthCore"), env[RynthInstallFolders.CoreVariable]);
        Assert.Equal(Path.Combine("/home/me/.config", "RynthSuite"), env[RynthInstallFolders.SuiteVariable]);
    }

    [Fact]
    public void FoldersThePlayerSetAreKept()
    {
        var env = new Dictionary<string, string?> { [RynthInstallFolders.SuiteVariable] = "/srv/rynthsuite" };
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal("/srv/rynthsuite", env[RynthInstallFolders.SuiteVariable]);
        Assert.Equal(Path.Combine("/home/me/.config", "RynthCore"), env[RynthInstallFolders.CoreVariable]);
    }

    [Fact]
    public void OnWindowsTheirOwnLookupIsLeftAlone()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: true, @"C:\Users\me\AppData\Roaming");
        Assert.Empty(env);
        Assert.False(env.ContainsKey(RynthInstallFolders.HotkeysVariable));
    }

    [Fact]
    public void OffWindowsTheHotkeysFileGoesBesideTheCoreFolder()
    {
        var env = new Dictionary<string, string?>();
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal(Path.Combine("/home/me/.config", "RynthCore", "hotkeys.txt"), env[RynthInstallFolders.HotkeysVariable]);

        env[RynthInstallFolders.HotkeysVariable] = "/mine.txt";
        RynthInstallFolders.Apply(name => env.GetValueOrDefault(name), (name, value) => env[name] = value, isWindows: false, "/home/me/.config");
        Assert.Equal("/mine.txt", env[RynthInstallFolders.HotkeysVariable]);
    }
}
