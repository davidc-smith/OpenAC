using System.Net;
using System.Net.Sockets;

namespace AcDream.Tools.RynthNavData.Tests;

/// <summary>Serves byte arrays from http://localhost:&lt;free port&gt;/ and counts requests.</summary>
internal sealed class LocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public LocalServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Base = $"http://localhost:{port}/";
        _listener.Prefixes.Add(Base);
        _listener.Start();
        _ = Task.Run(Serve);
    }

    public string Base { get; }

    public int Requests;

    public string Add(string name, byte[] content)
    {
        lock (_files)
            _files[name] = content;
        return Base + name;
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; }
            Interlocked.Increment(ref Requests);
            byte[]? body;
            lock (_files)
                _files.TryGetValue(context.Request.Url!.AbsolutePath.TrimStart('/'), out body);
            context.Response.StatusCode = body is null ? 404 : 200;
            if (body is not null)
            {
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body);
            }

            context.Response.Close();
        }
    }

    public void Dispose() => _listener.Stop();
}
