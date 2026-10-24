using System.Net;
using AcDream.App.Hosting;
using AcDream.App.Plugins;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Core.Plugins;
using AcDream.Runtime;
using AcDream.Runtime.Plugins;
using AcDream.Runtime.Session;

namespace AcDream.App.Tests.Hosting;

/// <summary>
/// A graphical client's session started with no window, over a stand-in for
/// the server: the session host as the client builds it, with the plugin
/// surface the client hands plugins, and a world connection that is never
/// negotiated (nothing goes over a wire) but logs a character in, can fall
/// silent and can refuse logins.
/// </summary>
internal sealed class WindowlessSessionFixture : IDisposable
{
    internal const uint CharacterId = 0x50000001u;
    internal const string CharacterName = "+Windowless";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "acdream-windowless-session-" + Guid.NewGuid().ToString("N"));
    private bool _ended;

    internal WindowlessSessionFixture(
        IReadOnlyDictionary<CharacterOptionId, bool>? declaredOptions = null,
        TimeProvider? time = null)
    {
        Directory.CreateDirectory(_directory);
        Events = new WorldEvents();
        Automation = RuntimeAutomationBindings.CreateSurface(
            GraphicalAutomationCapabilities.BuildSurfaceInputs(
                new GraphicalSurfaceInputParts
                {
                    Events = Events,
                    PeerDirectory = Path.Combine(_directory, "peers"),
                }));
        Host = new GraphicalSessionHost(
            RuntimeOptions.Parse(_directory, _ => null) with
            {
                SessionId = "s1",
                StatusFilePath = StatusPath,
                LiveMode = true,
                LiveHost = "127.0.0.1",
                LivePort = 9000,
                LiveUser = "account",
                LivePass = "password",
                LiveCharacterSelector = new LiveSessionCharacterSelector(
                    CharacterName: CharacterName),
                DeclaredCharacterOptions = declaredOptions
                    ?? new Dictionary<CharacterOptionId, bool>(),
            },
            new WorldGameState(),
            Events,
            Automation,
            Path.Combine(_directory, "logs"),
            Connections,
            time);
    }

    internal GraphicalSessionHost Host { get; }

    internal RuntimeAutomationSurface Automation { get; }

    internal WorldEvents Events { get; }

    internal FakeWorldConnections Connections { get; } = new();

    internal string StatusPath => Path.Combine(_directory, "status.jsonl");

    /// <summary>
    /// Starts the session with no window, as <c>--no-window</c> does, and
    /// runs its frames until the character is in the world.
    /// </summary>
    internal void EnterWorld()
    {
        Host.ApplyAutomationWithoutWindow();
        Host.StartSession();
        for (int frame = 0; frame < 20 && !Host.Runtime.Session.IsInWorld; frame++)
            Tick();
        Assert.True(Host.Runtime.Session.IsInWorld, "the character entered the world");
    }

    /// <summary>One update of the session with no window.</summary>
    internal void Tick() => Host.Tick(0.015, Host.WindowlessFrame);

    /// <summary>The events of the status file, in order.</summary>
    internal string[] StatusEvents() =>
        File.Exists(StatusPath)
            ? [.. File.ReadAllLines(StatusPath)
                .Select(line => System.Text.Json.JsonDocument.Parse(line)
                    .RootElement.GetProperty("e").GetString()!)]
            : [];

    /// <summary>Logs off and shuts the session down, as <c>/quit</c> does.</summary>
    internal GraphicalSessionEnd End()
    {
        _ended = true;
        return Host.EndSessionWithoutWindow();
    }

    public void Dispose()
    {
        if (!_ended)
            End();
        Host.Dispose();
        Automation.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The stand-in for the server. A login connects at once and finds the
    /// one character; while the server is down a login times out and a
    /// connection in play reads as lost.
    /// </summary>
    internal sealed class FakeWorldConnections : ILiveSessionOperations
    {
        internal bool ServerDown { get; set; }

        internal List<WorldSession> Sessions { get; } = [];

        internal List<CharacterList.Character> Roster { get; } =
            [new CharacterList.Character(CharacterId, CharacterName, 0u)];

        public IPEndPoint ResolveEndpoint(string host, int port) =>
            new(IPAddress.Loopback, port);

        public WorldSession CreateSession(IPEndPoint endpoint)
        {
            var session = new WorldSession(endpoint, new NoTransport());
            // Never negotiated, so a reliable send has no cipher to go out
            // under; the sends a client in the world makes on its own are
            // taken here instead.
            session.GameMessageCapture = (_, _) => { };
            Sessions.Add(session);
            return session;
        }

        public void Connect(WorldSession session, string user, string password)
        {
            if (ServerDown)
                throw new TimeoutException("ConnectRequest not received");
        }

        public void BeginConnect(WorldSession session, string user, string password)
        {
            if (ServerDown)
                throw new TimeoutException("ConnectRequest not received");
        }

        public bool PollConnect(WorldSession session) => true;

        public CharacterList.Parsed GetCharacters(WorldSession session) =>
            new(0u, [.. Roster], [], 11, "account", true, true);

        public void StartCharacterSelectionReceive(WorldSession session)
        {
        }

        public void EnterWorld(WorldSession session, int activeCharacterIndex)
        {
        }

        public void EnterWorldByGuid(
            WorldSession session,
            uint characterGuid,
            string accountName)
        {
        }

        public void Tick(WorldSession session)
        {
        }

        public bool IsConnectionLost(WorldSession session) => ServerDown;

        public void DisposeSession(WorldSession session) => session.Dispose();

        public void RequestCharacterLogOff(WorldSession session)
        {
        }

        public void ReturnToCharacterSelect(WorldSession session)
        {
        }
    }

    private sealed class NoTransport : IWorldSessionTransport
    {
        public void Send(ReadOnlySpan<byte> datagram)
        {
        }

        public void Send(IPEndPoint remote, ReadOnlySpan<byte> datagram)
        {
        }

        public int Receive(Span<byte> destination, TimeSpan timeout, out IPEndPoint? from)
        {
            from = null;
            return -1;
        }

        public ValueTask<NetReceiveResult> ReceiveAsync(
            Memory<byte> destination,
            CancellationToken cancellationToken) =>
            throw new OperationCanceledException(cancellationToken);

        public void Dispose()
        {
        }
    }
}
