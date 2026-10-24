using AcDream.App.Hosting;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Update;
using AcDream.Core.Plugins;
using AcDream.Runtime;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// The graphical client's session host: what it owns before any window is
/// built, the order of one update, and the process's last status line.
/// </summary>
public sealed class GraphicalSessionHostTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "acdream-session-host-" + Guid.NewGuid().ToString("N"));

    public GraphicalSessionHostTests() => Directory.CreateDirectory(_directory);

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

    private string StatusPath => Path.Combine(_directory, "status.jsonl");

    private GraphicalSessionHost NewHost(
        WorldEvents? events = null,
        string? sessionId = "s1") =>
        new(
            RuntimeOptions.Parse(_directory, _ => null) with
            {
                SessionId = sessionId,
                StatusFilePath = StatusPath,
            },
            new WorldGameState(),
            events ?? new WorldEvents());

    private string[] StatusEvents() =>
        File.ReadAllLines(StatusPath)
            .Select(line => System.Text.Json.JsonDocument.Parse(line)
                .RootElement.GetProperty("e").GetString()!)
            .ToArray();

    [Fact]
    public void The_session_exists_before_any_window()
    {
        using GraphicalSessionHost host = NewHost();

        Assert.Null(host.Window);
        Assert.False(host.Runtime.Session.IsInWorld);
        // The runtime is held for the process by the host, not by a window.
        Assert.Equal(1, host.Runtime.CaptureOwnership().HostLeaseCount);
        Assert.Same(host.Runtime.ActionOwner.Selection, host.Runtime.ActionOwner.Selection);
        Assert.Equal("s1", host.SessionId);
    }

    [Fact]
    public void An_unnamed_session_is_called_app()
    {
        using GraphicalSessionHost host = NewHost(sessionId: null);

        Assert.Equal("app", host.SessionId);
    }

    [Fact]
    public void One_update_drains_plugin_work_then_runs_the_frame_then_ticks_plugins()
    {
        var events = new WorldEvents();
        var order = new List<string>();
        events.Tick += _ => order.Add("plugin tick");
        using GraphicalSessionHost host = NewHost(events);
        bool ran = false;
        var plugin = new Thread(() =>
            ran = host.MainThreadDispatch.InvokeAndWait(
                () => order.Add("plugin work"),
                TimeSpan.FromSeconds(10)));
        plugin.Start();
        var frame = new RecordingFrame(order);

        // The work is queued from another thread at some point; it runs at
        // the start of the next update, ahead of that update's frame.
        for (int i = 0; i < 1000 && !order.Contains("plugin work"); i++)
        {
            host.Tick(0.05, frame);
            if (!order.Contains("plugin work"))
                Thread.Sleep(5);
        }

        Assert.True(plugin.Join(TimeSpan.FromSeconds(10)));
        Assert.True(ran);
        int work = order.IndexOf("plugin work");
        Assert.Equal("frame", order[work + 1]);
        Assert.Equal("plugin tick", order[work + 2]);
        Assert.All(frame.Inputs, input => Assert.Equal(0.05, input.HostDeltaSeconds));
        // Every update runs its frame before its plugin tick.
        Assert.True(order.IndexOf("frame") < order.IndexOf("plugin tick"));
    }

    [Fact]
    public void Plugin_hosting_starts_once()
    {
        using GraphicalSessionHost host = NewHost();

        Assert.Null(host.PluginSession);
        Assert.Throws<ArgumentNullException>(() => host.StartPluginHosting(null!));
    }

    [Theory]
    [InlineData(nameof(GameWindowLifetimeStatus.Complete), false, 0, "graceful")]
    [InlineData(nameof(GameWindowLifetimeStatus.CompleteWithCleanupFailures), false, 1, "shutdown-incomplete")]
    [InlineData(nameof(GameWindowLifetimeStatus.CompleteWithDeferredNativeRelease), false, 1, "shutdown-incomplete")]
    [InlineData(nameof(GameWindowLifetimeStatus.Complete), true, 1, "crashed")]
    public void The_last_status_line_follows_how_the_window_shut_down(
        string status,
        bool crashed,
        int code,
        string reason)
    {
        using (GraphicalSessionHost host = NewHost())
        {
            host.ReportExited(
                new GameWindowLifetimeReport(
                    Enum.Parse<GameWindowLifetimeStatus>(status), null, [], null),
                crashed);
        }

        System.Text.Json.JsonElement exited = System.Text.Json.JsonDocument
            .Parse(File.ReadAllLines(StatusPath).Last())
            .RootElement;
        Assert.Equal("exited", exited.GetProperty("e").GetString());
        Assert.Equal("s1", exited.GetProperty("sessionId").GetString());
        Assert.Equal(code, exited.GetProperty("code").GetInt32());
        Assert.Equal(reason, exited.GetProperty("reason").GetString());
    }

    [Fact]
    public void A_session_that_never_entered_the_world_says_nothing_as_it_stops()
    {
        using (GraphicalSessionHost host = NewHost())
        {
            host.ReportSessionStopping();
            host.ReportExited(
                new GameWindowLifetimeReport(GameWindowLifetimeStatus.Complete, null, [], null),
                crashed: false);
        }

        Assert.Equal(["exited"], StatusEvents());
    }

    [Fact]
    public void Disposing_the_host_releases_the_status_file()
    {
        GraphicalSessionHost host = NewHost();
        host.ReportExited(
            new GameWindowLifetimeReport(GameWindowLifetimeStatus.Complete, null, [], null),
            crashed: false);

        host.Dispose();

        File.Delete(StatusPath);
        Assert.False(File.Exists(StatusPath));
    }

    private sealed class RecordingFrame(List<string> order) : IGraphicalSessionFrame
    {
        public List<UpdateFrameInput> Inputs { get; } = [];

        public void TickSessionFrame(UpdateFrameInput input)
        {
            Inputs.Add(input);
            order.Add("frame");
        }
    }
}
