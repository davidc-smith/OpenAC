using AcDream.Headless.Configuration;
using AcDream.Launcher.Core.Launching;
using AcDream.Launcher.Core.Orchestration;
using AcDream.Launcher.Core.Profiles;
using AcDream.Launcher.Core.Updates;

namespace AcDream.Headless.Tests;

public sealed class LauncherHeadlessCommandLineContractTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "acdream-headless-cli-contract",
        Guid.NewGuid().ToString("N"));

    public LauncherHeadlessCommandLineContractTests()
    {
        Directory.CreateDirectory(AppDirectory);
        // The resolver picks the graphical host per OS; the stub has to match.
        string suffix = PayloadExecutableNames.SuffixForCurrentOs();
        CreateStubExecutable(
            Path.Combine(
                AppDirectory,
                PayloadExecutableNames.GraphicalHostForCurrentOs() + suffix));
        CreateStubExecutable(
            Path.Combine(AppDirectory, PayloadExecutableNames.HeadlessHost + suffix));
        File.WriteAllText(
            Path.Combine(AppDirectory, AcDream.Platform.ClientCapabilities.SingleInstallRootMarkerFileName),
            "single root");
    }

    private static void CreateStubExecutable(string path)
    {
        File.WriteAllText(path, "stub");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ProbeArgumentsParseAsAHeadlessRunCommand()
    {
        LauncherProcessSpec spec = ExecutableSet().CreateProbeSpec(ConfigPath);

        HeadlessCommandLine parsed = HeadlessCommandLine.Parse(spec.Arguments);

        Assert.Equal("run", parsed.Command);
        Assert.Equal(ConfigPath, parsed.ConfigurationPath);
    }

    /// <summary>
    /// A headless row runs the windowless host only where no window can open (no display);
    /// elsewhere it starts the graphical client with no window.
    /// </summary>
    [Fact]
    public void HeadlessPlayArgumentsWithoutWindowSupportParseAsAHeadlessRunCommand()
    {
        LauncherProcessSpec spec = ExecutableSet()
            .CreatePlaySpec(LaunchMode.Headless, ConfigPath, windowSupport: false);

        HeadlessCommandLine parsed = HeadlessCommandLine.Parse(spec.Arguments);

        Assert.Equal("run", parsed.Command);
        Assert.Equal(ConfigPath, parsed.ConfigurationPath);
    }

    [Theory]
    [InlineData(LaunchMode.Gui)]
    [InlineData(LaunchMode.GuiSelect)]
    [InlineData(LaunchMode.Headless)]
    public void GraphicalArgumentsAreNotAHeadlessCommandLine(LaunchMode mode)
    {
        LauncherProcessSpec spec = ExecutableSet().CreatePlaySpec(mode, ConfigPath);

        Assert.Equal(
            mode == LaunchMode.Headless
                ? ["--session-config", ConfigPath, "--console", "--no-window"]
                : ["--session-config", ConfigPath, "--console"],
            spec.Arguments);
        Assert.Throws<HeadlessCommandLineException>(
            () => HeadlessCommandLine.Parse(spec.Arguments));
    }

    private string AppDirectory => Path.Combine(_root, "app");

    private string ConfigPath => Path.Combine(_root, "session.json");

    private LauncherExecutableSet ExecutableSet() =>
        LauncherExecutableSet.FromDirectory(AppDirectory);
}
