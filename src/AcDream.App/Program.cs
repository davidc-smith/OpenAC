using AcDream.App;
using AcDream.App.Configuration;
using AcDream.App.Credentials;
using AcDream.App.Hosting;
using AcDream.App.Plugins;
using AcDream.App.Platform;
using AcDream.App.Rendering;
using AcDream.Platform;
using Serilog;

GraphicalHostPlatformServices graphicalPlatform =
    GraphicalHostPlatformServices.Resolve();
graphicalPlatform.ConfigureWindowBackend();
ApplicationPathSet applicationPaths = graphicalPlatform.Paths;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();
// An earlier version's data folder named as the install folder is refused,
// not taken over: its files stay as they were.
if (LegacyApplicationLayout.RefusalToUseAsInstallFolder(applicationPaths) is { } earlierFolder)
{
    Log.Error("{Reason}", earlierFolder);
    Log.CloseAndFlush();
    return 5;
}
// Held for the whole run, so a client update or a move of the install folder
// waits for this client, however it was started.
using InstallSessionLease? installSession =
    InstallSessionLease.TryAcquireShared(applicationPaths);
if (installSession is null)
{
    Log.Error("{Reason}", InstallSessionLease.BusyMessage(applicationPaths));
    Log.CloseAndFlush();
    return 5;
}
Log.Information(
    "graphical platform {RuntimeIdentifier}; native closure: {NativeDependencies}",
    graphicalPlatform.RuntimeIdentifier,
    string.Join(
        ", ",
        graphicalPlatform.NativeDependencies.Select(
            dependency =>
                $"{dependency.Feature}={dependency.PublishedFileName}")));

string? sessionConfigFlagPath = SessionConfigArgumentParsing.ExtractFlagValue(
    args, "--session-config", out bool sessionConfigFlagPresent);
if (sessionConfigFlagPath is null && sessionConfigFlagPresent)
{
    Log.Error(
        "--session-config requires a value (a path to the session-config document).");
    return 2;
}
string[] positionalArgs =
    SessionConfigArgumentParsing.WithoutFlagAndValue(args, "--session-config");
// The launcher's start: --console reads typed lines and prints chat;
// --no-window starts the session with no window, opened later by /window.
SessionConfigArgumentParsing.ConsoleFlags? consoleFlags =
    SessionConfigArgumentParsing.ExtractConsoleFlags(
        positionalArgs,
        sessionConfigFlagPresent,
        out positionalArgs,
        out string? consoleUsageError);
if (consoleFlags is not { } launch)
{
    Log.Error("{Usage}", consoleUsageError);
    return 2;
}

var datDirArg = positionalArgs.FirstOrDefault();
var envDatDir = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");

RuntimeOptions runtimeOptions;
if (sessionConfigFlagPath is not null)
{
    SessionConfiguration sessionConfig;
    SessionDescriptor session;
    try
    {
        (sessionConfig, session) = SessionConfigurationLoader.Load(sessionConfigFlagPath);
    }
    catch (Exception error)
        when (error is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Text.Json.JsonException
            or SessionConfigurationException)
    {
        Log.Error("--session-config invalid: {Error}", error.Message);
        return 3;
    }

    string? resolvedDatDir =
        NullIfEmpty(sessionConfig.Process?.Content?.DatDirectory)
        ?? NullIfEmpty(datDirArg)
        ?? NullIfEmpty(envDatDir);
    if (resolvedDatDir is null)
    {
        Log.Error(
            "usage: AcDream.App <dat-directory>  (or set ACDREAM_DAT_DIR, "
            + "or supply process.content.datDirectory in --session-config)");
        return 3;
    }

    AppCredentialSecret? secret = null;
    try
    {
        var resolver = new AppCredentialResolver(
            Console.In,
            applicationPaths.ConfigDirectory,
            graphicalPlatform.OperatingSystem is
                GraphicalHostOperatingSystem.Linux or
                GraphicalHostOperatingSystem.MacOS);
        secret = resolver.Resolve(session.Id, session.Credential);
        runtimeOptions = RuntimeOptions.FromSessionConfig(
            resolvedDatDir,
            Environment.GetEnvironmentVariable,
            sessionConfigFlagPath,
            sessionConfig,
            session,
            secret.Reveal());
    }
    catch (AppCredentialException error)
    {
        Log.Error("--session-config credential unavailable: {Error}", error.Message);
        return 4;
    }
    // A settings map written into the document is refused here in the same
    // words a settings file is refused below, rather than ending the client
    // with a stack trace.
    catch (AcDream.Runtime.Plugins.PluginSessionSettingsException error)
    {
        Log.Error("--session-config plugin settings invalid: {Error}", error.Message);
        return 3;
    }
    finally
    {
        secret?.Dispose();
    }

    // Env-var flow untouched when the flag is absent; when both are present
    // the flag wins — this line makes that explicit rather than silent.
    Log.Information(
        "--session-config {Path} present; overriding ACDREAM_LIVE*/ACDREAM_TEST_* "
        + "env-var live-session settings",
        sessionConfigFlagPath);
}
else
{
    var datDir = datDirArg ?? envDatDir;
    if (string.IsNullOrWhiteSpace(datDir))
    {
        Log.Error("usage: AcDream.App <dat-directory>  (or set ACDREAM_DAT_DIR)");
        return 2;
    }
    runtimeOptions = RuntimeOptions.FromEnvironment(datDir);
}

// The game files open on a worker while the rest of the start runs; they
// are taken, and any failure to open them reported, where the session takes
// them below, before the plugins start.
Task<GraphicalSessionContent>? contentOpening = runtimeOptions.VulkanCapabilityProbe
    ? null
    : Task.Run(() => GraphicalSessionContent.Open(
        runtimeOptions,
        line => Log.Warning("{Line}", line)));
// The startup settings each plugin was given. A session-config document that
// names them is the more specific instruction and outranks the launch option
// pointing at a file. A named file that cannot be read is a startup fault and
// says so: a plugin whose settings silently came out empty would behave
// differently here than it does on the client with no window, which is the
// one thing these settings exist to prevent.
AcDream.Runtime.Plugins.PluginSessionSettings pluginSessionSettings;
try
{
    pluginSessionSettings =
        runtimeOptions.SessionPluginSettings
        ?? (runtimeOptions.PluginSettingsFile is { } settingsPath
            ? AcDream.Runtime.Plugins.PluginSessionSettings.ReadFile(settingsPath)
            : AcDream.Runtime.Plugins.PluginSessionSettings.Empty);
}
catch (AcDream.Runtime.Plugins.PluginSessionSettingsException error)
{
    Log.Error("ACDREAM_PLUGIN_SETTINGS_FILE invalid: {Error}", error.Message);
    return sessionConfigFlagPath is null ? 2 : 3;
}

if (runtimeOptions.DevTools)
{
    Log.Information(
        "ACDREAM_DEVTOOLS=1: enables " +
        "the optional Vulkan validation/debug-utils extensions.");
}

var worldGameState = new AcDream.Core.Plugins.WorldGameState();
// A plugin handler that throws is skipped, not hidden: a plugin author
// whose handler stopped running needs to be told why, in the same words
// whichever client is running.
var worldEvents = new AcDream.Core.Plugins.WorldEvents(
    line => Log.Warning("{Line}", line));
var uiRegistry = new AcDream.App.Plugins.BufferedUiRegistry();
using var renderPackRegistry = new AcDream.App.Plugins.BufferedRenderPackRegistry();
using IDisposable atmosphericPackRegistration = renderPackRegistry.Register(
    AcDream.App.Rendering.Packs.BuiltInAtmosphericRenderPack.Descriptor,
    AcDream.App.Rendering.Packs.BuiltInAtmosphericRenderPack.CreateAssets(
        Path.Combine(
            AppContext.BaseDirectory,
            "Rendering",
            "Shaders",
            "spv")));
// One factory, shared with the windowless host: what the surface needs
// before a plugin can reach it cannot be present on one client and quietly
// absent on the other.
using var automation = AcDream.Runtime.Plugins.RuntimeAutomationBindings
    .CreateSurface(
        AcDream.App.Plugins.GraphicalAutomationCapabilities.BuildSurfaceInputs(
            new AcDream.App.Plugins.GraphicalSurfaceInputParts
            {
                Events = worldEvents,
                PeerDirectory = applicationPaths.PluginPeersDirectory,
                PluginTags = runtimeOptions.PluginTags,
            }));
var lootClassifiers = new AcDream.Core.Plugins.PluginLootClassifierRegistry();
var hotkeyRegistry = new AcDream.App.Input.AppHotkeyRegistry(
    Path.Combine(applicationPaths.ConfigDirectory, "plugin-hotkeys.json"));
// The session is the host's for the life of the process; a window is built
// over it and borrows what it owns.
using var sessionHost = new GraphicalSessionHost(
    runtimeOptions,
    worldGameState,
    worldEvents,
    automation,
    applicationPaths.LogsDirectory);
// The game files are the session's, opened once for the process: the
// session plays from them with or without a window, and a window borrows
// them.
if (contentOpening is not null)
{
    GraphicalSessionContent content;
    try
    {
        content = contentOpening.GetAwaiter().GetResult();
    }
    catch (Exception error)
        when (error is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or NotSupportedException)
    {
        Log.Error("the game files could not be opened: {Error}", error.Message);
        Log.CloseAndFlush();
        return 3;
    }
    Log.Information(
        "prepared assets: opened '{Path}'",
        runtimeOptions.PreparedAssetPath);
    sessionHost.UseContent(content);
}
var host = new AppPluginHost(
    new SerilogAdapter(Log.Logger),
    worldGameState,
    worldEvents,
    sessionHost.Runtime.ActionOwner.Selection,
    uiRegistry,
    automation,
    new AcDream.Core.Plugins.FilePluginStorage(
        applicationPaths.PluginStorageDirectory),
    automation.PluginCommands,
    lootClassifiers,
    new AcDream.Core.Plugins.FilePluginStorage(
        runtimeOptions.VtankProfileDirectoryOverride
            ?? applicationPaths.VtankProfilesDirectory),
    new AcDream.App.Plugins.WindowPluginClipboard(
        () => sessionHost.Window?.ClipboardKeyboard,
        () => sessionHost.MainThreadDispatch),
    hotkeyRegistry,
    new AcDream.App.Plugins.WindowPluginHostWindow(
        () => sessionHost.Window?.PluginWindowHandle,
        () => sessionHost.Window?.PluginWindowIsMinimized ?? false,
        () => sessionHost.MainThreadDispatch),
    sessionHost.WorldLines,
    pluginSessionSettings);
GraphicalPluginSession pluginSession = GraphicalPluginSession.Create(
    applicationPaths,
    runtimeOptions.Plugins,
    runtimeOptions.SessionId ?? "app",
    host,
    sessionHost.StatusWriter,
    renderPackRegistry);
sessionHost.StartPluginHosting(pluginSession);

// With the console (the launcher's start) typed lines are read once the
// credential line has been, and chat prints on standard error.
using GraphicalSessionConsole? console = launch.Console
    ? new GraphicalSessionConsole(sessionHost, Console.In, Console.Error)
    : null;
// Ctrl+C and Ctrl+Break log the character off, then the process exits.
Console.CancelKeyPress += (_, cancel) =>
{
    cancel.Cancel = true;
    sessionHost.WindowLoop?.RequestShutdown();
};
GraphicalSessionEnd end;
try
{
    try
    {
        console?.Start();
        end = sessionHost.Run(
            session => new GameWindow(
                session,
                uiRegistry,
                graphicalPlatform,
                renderPackRegistry,
                hotkeyRegistry),
            windowAtStart: !launch.NoWindow,
            events: console?.WindowEvents,
            console: launch.Console);
    }
    catch (NotSupportedException error)
    {
        Log.Error("{GraphicalStartupFailure}", error.Message);
        return 6;
    }
}
finally
{
    Log.CloseAndFlush();
}

// 0 graceful, 5 the connection went, 6 the client could not finish.
return end switch
{
    GraphicalSessionEnd.Graceful => 0,
    GraphicalSessionEnd.ConnectionLost => 5,
    _ => 6,
};

static string? NullIfEmpty(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value;
