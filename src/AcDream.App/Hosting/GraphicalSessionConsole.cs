using AcDream.Runtime.Chat;

namespace AcDream.App.Hosting;

/// <summary>What one typed console line asks for.</summary>
internal enum GraphicalConsoleVerb
{
    /// <summary>Nothing (an empty line).</summary>
    None,

    /// <summary><c>/window [id]</c>: open a window on the session.</summary>
    Window,

    /// <summary><c>/window id</c> naming a session this process does not run.</summary>
    NoSuchSession,

    /// <summary><c>/close</c>: close the window; the session plays on.</summary>
    Close,

    /// <summary><c>/quit</c>: log off and end the client.</summary>
    Quit,

    /// <summary>Anything else: a chat line, as typed into the chat box.</summary>
    Chat,
}

/// <summary>
/// The client's console, as the launcher drives it: typed lines from
/// standard input (after the credential line), chat printed on standard
/// error. <c>/window [id]</c> opens a window on the session, <c>/close</c>
/// closes it and the session plays on, <c>/quit</c> logs off and ends the
/// client; every other line goes into the session as the chat box would
/// send it. The verbs are whole words in any case, as in the Rust client.
/// </summary>
internal sealed class GraphicalSessionConsole : IDisposable
{
    internal const string NoticePrefix = "-- ";

    private readonly GraphicalSessionHost _host;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private Thread? _reader;
    private bool _disposed;

    internal GraphicalSessionConsole(
        GraphicalSessionHost host,
        TextReader input,
        TextWriter output)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    /// <summary>Reads one typed line into what it asks for.</summary>
    internal static (GraphicalConsoleVerb Verb, string Argument) Parse(
        string line,
        string sessionId)
    {
        ArgumentNullException.ThrowIfNull(line);
        string trimmed = line.Trim();
        if (trimmed.Length == 0)
            return (GraphicalConsoleVerb.None, string.Empty);
        int space = trimmed.IndexOf(' ');
        string word = space < 0 ? trimmed : trimmed[..space];
        string rest = space < 0 ? string.Empty : trimmed[(space + 1)..].Trim();
        if (string.Equals(word, "/window", StringComparison.OrdinalIgnoreCase))
        {
            return rest.Length == 0 || string.Equals(rest, sessionId, StringComparison.Ordinal)
                ? (GraphicalConsoleVerb.Window, rest)
                : (GraphicalConsoleVerb.NoSuchSession, rest);
        }
        if (rest.Length == 0
            && string.Equals(word, "/close", StringComparison.OrdinalIgnoreCase))
        {
            return (GraphicalConsoleVerb.Close, string.Empty);
        }
        if (rest.Length == 0
            && string.Equals(word, "/quit", StringComparison.OrdinalIgnoreCase))
        {
            return (GraphicalConsoleVerb.Quit, string.Empty);
        }
        return (GraphicalConsoleVerb.Chat, trimmed);
    }

    /// <summary>
    /// The window's events as the status file and the console tell them.
    /// </summary>
    internal GraphicalWindowEvents WindowEvents => new()
    {
        Opened = () =>
        {
            _host.StatusWriter.WindowOpened(_host.SessionId);
            Notice("window opened");
        },
        Closed = reason =>
        {
            _host.StatusWriter.WindowClosed(_host.SessionId, StatusReason(reason));
            Notice($"window closed ({StatusReason(reason)})");
        },
        Failed = error =>
        {
            _host.StatusWriter.WindowFailed(_host.SessionId, error);
            Notice($"window failed: {error}");
        },
    };

    /// <summary>The words the status file uses for why a window closed.</summary>
    internal static string StatusReason(WindowDetachReason reason) => reason switch
    {
        WindowDetachReason.Requested => "requested",
        WindowDetachReason.ClosedByUser => "closedByUser",
        WindowDetachReason.RenderFailed => "renderFailed",
        WindowDetachReason.Shutdown => "shutdown",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    /// <summary>
    /// Starts printing chat and reading typed lines on a background thread.
    /// </summary>
    internal void Start()
    {
        _host.Runtime.CommunicationOwner.ChatFeed.LineAppended += WriteChatLine;
        _reader = new Thread(ReadLines)
        {
            IsBackground = true,
            Name = "console input",
        };
        _reader.Start();
    }

    /// <summary>Carries out one typed line. Any thread.</summary>
    internal void Handle(string line)
    {
        GraphicalSessionWindowLoop? loop = _host.WindowLoop;
        (GraphicalConsoleVerb verb, string argument) = Parse(line, _host.SessionId);
        switch (verb)
        {
            case GraphicalConsoleVerb.None:
                return;
            case GraphicalConsoleVerb.Window:
                loop?.RequestWindow();
                return;
            case GraphicalConsoleVerb.NoSuchSession:
                Notice($"no session is called {argument}");
                return;
            case GraphicalConsoleVerb.Close:
                if (loop is null || loop.State is not WindowAttachState.Attached)
                    Notice("no window is open");
                else
                    loop.RequestCloseWindow();
                return;
            case GraphicalConsoleVerb.Quit:
                loop?.RequestShutdown();
                return;
            case GraphicalConsoleVerb.Chat:
                // Sent on the session's own thread, as the chat box sends it.
                if (!_host.MainThreadDispatch.InvokeAndWait(
                        () => _ = _host.SubmitChatText(argument),
                        TimeSpan.FromSeconds(10)))
                {
                    Notice("the session did not take the line");
                }
                return;
        }
    }

    private void ReadLines()
    {
        while (!_disposed)
        {
            string? line;
            try
            {
                line = _input.ReadLine();
            }
            catch (IOException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (line is null)
                return;
            Handle(line);
        }
    }

    private void WriteChatLine(RuntimeChatLine line)
    {
        RuntimeChatFeed feed = _host.Runtime.CommunicationOwner.ChatFeed;
        if (!feed.BelongsTo(AcDream.Core.Chat.ChatWindowState.MainWindowId, line.LogTextType))
            return;
        WriteLine(RuntimeChatLineTags.For(line) + line.Text);
    }

    private void Notice(string text) => WriteLine(NoticePrefix + text);

    private void WriteLine(string text)
    {
        lock (_output)
        {
            _output.WriteLine(text);
            _output.Flush();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _host.Runtime.CommunicationOwner.ChatFeed.LineAppended -= WriteChatLine;
    }
}
