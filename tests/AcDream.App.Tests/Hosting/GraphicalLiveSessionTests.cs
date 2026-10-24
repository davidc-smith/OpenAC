using System.Runtime.CompilerServices;
using AcDream.App.Hosting;
using AcDream.App.Plugins;
using AcDream.Core.Net;
using AcDream.Core.Plugins;
using AcDream.Runtime;
using AcDream.Runtime.Chat;
using AcDream.Runtime.Session;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The graphical client's live session belongs to the session host, which
/// builds it before any window; a window lends it what only a window can do
/// while it is attached. These pin what the session does with a window and
/// without one. (The Rust client was built this way from the start -- its
/// runtime owns the session and never depends on a window -- so there is no
/// Rust test to port for this step; the window-less answers are the ones
/// both clients' windowless sessions give.)
/// </summary>
public sealed class GraphicalLiveSessionTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "acdream-live-session-" + Guid.NewGuid().ToString("N"));

    public GraphicalLiveSessionTests() => Directory.CreateDirectory(_directory);

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

    private GraphicalSessionHost NewHost() =>
        new(
            RuntimeOptions.Parse(_directory, _ => null) with
            {
                SessionId = "s1",
                StatusFilePath = Path.Combine(_directory, "status.jsonl"),
            },
            new WorldGameState(),
            new WorldEvents());

    private static T Part<T>() where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static GraphicalSessionPresentation Window(
        List<string> noted,
        ILiveSessionEventRouting? route = null) => new()
    {
        CreateEvents = _ =>
        {
            noted.Add("window route");
            return route ?? new NoRoute();
        },
        Reset = generation => noted.Add($"window reset {generation.Value}"),
        ClientCommands = new RuntimeClientCommandHostBindings(),
        Settings = Part<AcDream.App.Settings.RuntimeSettingsController>(),
        PlayerModeAutoEntry = Part<AcDream.App.Input.PlayerModeAutoEntry>(),
        WorldState = Part<AcDream.App.Streaming.GpuWorldState>(),
        Teleport = Part<AcDream.App.Streaming.DeferredLocalPlayerTeleportNetworkSink>(),
    };

    private static LiveSessionHostBindings Bindings(
        Func<GraphicalSessionPresentation?> window,
        List<string> noted,
        ILiveSessionEventRouting? windowlessRoute = null) =>
        GraphicalAutomationCapabilities.BuildSessionHostBindings(
            new GraphicalSessionHostParts
            {
                CreateCommands = _ => throw new NotSupportedException(),
                ResetWithoutWindow = generation =>
                    noted.Add($"runtime reset {generation.Value}"),
                CreateEventsWithoutWindow = _ =>
                {
                    noted.Add("windowless route");
                    return windowlessRoute ?? new NoRoute();
                },
                Presentation = window,
                Identity = new AcDream.App.Input.LocalPlayerIdentityState(),
                Communication = Part<AcDream.Runtime.Gameplay.RuntimeCommunicationState>(),
                Combat = Part<AcDream.Core.Combat.CombatState>(),
                StatusWriter = new SessionStatusWriter(null),
                SessionId = "s1",
            });

    [Theory]
    [InlineData(RuntimeCharacterSelectionLifecycle.Inactive, false, false, false, null)]
    [InlineData(RuntimeCharacterSelectionLifecycle.AwaitingSelection, false, false, false, null)]
    [InlineData(RuntimeCharacterSelectionLifecycle.InWorld, true, false, false, null)]
    [InlineData(RuntimeCharacterSelectionLifecycle.Connecting, false, false, false, "connecting")]
    [InlineData(RuntimeCharacterSelectionLifecycle.EnteringWorld, false, false, false, "entering the world")]
    [InlineData(RuntimeCharacterSelectionLifecycle.InWorld, false, false, false, "entering the world")]
    [InlineData(RuntimeCharacterSelectionLifecycle.InWorld, true, true, false, "in a portal")]
    [InlineData(RuntimeCharacterSelectionLifecycle.InWorld, true, false, true, "logging out")]
    public void A_window_opens_on_the_session_only_between_its_steps(
        RuntimeCharacterSelectionLifecycle lifecycle,
        bool loginCompleteSent,
        bool inPortal,
        bool loggingOut,
        string? expected)
    {
        Assert.Equal(
            expected,
            GraphicalLiveSession.WhyAWindowCannotOpen(
                lifecycle,
                loginCompleteSent,
                inPortal,
                loggingOut));
    }

    [Fact]
    public void The_live_session_and_its_command_surface_exist_before_any_window()
    {
        using GraphicalSessionHost host = NewHost();

        Assert.Null(host.Window);
        Assert.NotNull(host.LiveSession.Host);
        Assert.NotNull(host.LiveSession.Commands);
        Assert.NotNull(host.LiveSession.LoginCommands);
        Assert.False(host.LiveSession.HasPresentation);
        Assert.Null(host.LiveSession.Host.CurrentSession);
        // The character the client reads is the runtime's own.
        host.PlayerIdentity.ServerGuid = 0x50000001u;
        Assert.Equal(0x50000001u, host.Runtime.PlayerIdentity.ServerGuid);
    }

    [Fact]
    public void A_reset_with_no_window_puts_the_runtime_back_alone_and_with_one_runs_the_windows()
    {
        var noted = new List<string>();
        GraphicalSessionPresentation? attached = null;
        LiveSessionHostBindings bindings = Bindings(() => attached, noted);

        bindings.Reset(new RuntimeGenerationToken(3));
        attached = Window(noted);
        bindings.Reset(new RuntimeGenerationToken(4));
        attached = null;
        bindings.Reset(new RuntimeGenerationToken(5));

        // Each binding asks for the window when it runs, not when it was
        // built: the same bindings serve a window that comes and goes.
        Assert.Equal(
            ["runtime reset 3", "window reset 4", "runtime reset 5"],
            noted);
    }

    [Fact]
    public void A_world_connection_is_taken_hold_of_by_the_attached_window_or_without_one_by_the_windowless_route()
    {
        var noted = new List<string>();
        GraphicalSessionPresentation? attached = null;
        var windowless = new NoRoute();
        LiveSessionHostBindings bindings = Bindings(() => attached, noted, windowless);
        WorldSession session = Part<WorldSession>();

        // With no window the session still enters the world: the route every
        // client without a window builds takes hold of the connection.
        Assert.Same(windowless, bindings.Routing.CreateEvents(session));

        var route = new NoRoute();
        attached = Window(noted, route);
        Assert.Same(route, bindings.Routing.CreateEvents(session));
        Assert.Equal(["windowless route", "window route"], noted);
    }

    [Fact]
    public void The_session_host_builds_the_windowless_route_and_frame_over_its_own_collision()
    {
        using GraphicalSessionHost host = NewHost();

        // Built on first use and kept: one route and one frame for the
        // process, whichever connection or window comes and goes.
        Assert.Same(host.WindowlessRoute, host.WindowlessRoute);
        Assert.Same(host.WindowlessFrame, host.WindowlessFrame);
        Assert.Null(host.WindowlessRoute.CurrentSession);

        // The frame runs with nothing drawn and no connection: an update of
        // a session waiting to connect.
        host.Tick(0.015, host.WindowlessFrame);
        Assert.Null(host.Window);
        Assert.False(host.Runtime.Session.IsInWorld);
    }

    [Fact]
    public void Login_steps_that_only_a_window_takes_are_skipped_without_one()
    {
        var noted = new List<string>();
        LiveSessionHostBindings bindings = Bindings(() => null, noted);

        // None of these reaches a window that is not there; they would throw
        // on the bare parts above if they did.
        bindings.Selection.SetVitalsIdentity(1u);
        bindings.Selection.MarkPersistent(1u);
        bindings.Selection.ArmLoginTunnel!();
        bindings.EnteredWorld.SetActiveCharacter("Horan");
        bindings.EnteredWorld.RestoreLayout();
        bindings.EnteredWorld.SyncToolbar();
        bindings.EnteredWorld.LoadCharacterSettings("Horan");
        bindings.EnteredWorld.ArmPlayerModeAutoEntry();
        bindings.EnteredWorld.ResumeWorldAudio!();

        Assert.Empty(noted);
    }

    [Fact]
    public void A_command_that_needs_a_window_says_so_without_one_and_reaches_the_window_with_one()
    {
        var said = new List<string>();
        RuntimeClientCommandHostBindings? current = null;
        RuntimeClientCommandHostBindings hooks =
            GraphicalAutomationCapabilities.ResolveClientCommandsPerCall(
                () => current,
                () => said.Add(RuntimeClientCommandBindings.NotAvailableWithoutAWindow));
        bool continued = false;

        hooks.ToggleFrameRate!();
        hooks.SaveUi!("raid");
        hooks.SetFieldOfView!(70f);
        hooks.ShowConfirmation!("Really?", _ => continued = true);
        hooks.SetUiLocked!(true);

        // Four commands needed a window; the lock is a character option and
        // only its display needs one, so it says nothing. A question nobody
        // can answer does not go ahead.
        Assert.Equal(4, said.Count);
        Assert.False(continued);

        var reached = new List<string>();
        current = new RuntimeClientCommandHostBindings
        {
            ToggleFrameRate = () => reached.Add("frame rate"),
            SaveUi = name => reached.Add($"save {name}"),
            SetFieldOfView = degrees => reached.Add($"fov {degrees}"),
            ShowConfirmation = (_, completed) => completed(true),
            SetUiLocked = locked => reached.Add($"lock {locked}"),
        };
        said.Clear();

        hooks.ToggleFrameRate!();
        hooks.SaveUi!("raid");
        hooks.SetFieldOfView!(70f);
        hooks.ShowConfirmation!("Really?", _ => continued = true);
        hooks.SetUiLocked!(true);

        Assert.Empty(said);
        Assert.True(continued);
        Assert.Equal(["frame rate", "save raid", "fov 70", "lock True"], reached);
    }

    [Fact]
    public void One_window_presents_the_session_at_a_time_and_leaving_frees_the_session()
    {
        using GraphicalSessionHost host = NewHost();
        var noted = new List<string>();
        GraphicalSessionPresentation first = Window(noted);
        GraphicalSessionPresentation second = Window(noted);

        IDisposable firstLease = host.LiveSession.AttachPresentation(first);
        Assert.True(host.LiveSession.HasPresentation);
        Assert.Throws<InvalidOperationException>(
            () => host.LiveSession.AttachPresentation(second));

        firstLease.Dispose();
        Assert.False(host.LiveSession.HasPresentation);

        using IDisposable secondLease = host.LiveSession.AttachPresentation(second);
        // A stale lease let go again does not take the next window away.
        firstLease.Dispose();
        Assert.True(host.LiveSession.HasPresentation);
    }

    private sealed class NoRoute : ILiveSessionEventRouting
    {
        public void Attach()
        {
        }

        public void Dispose()
        {
        }
    }
}
