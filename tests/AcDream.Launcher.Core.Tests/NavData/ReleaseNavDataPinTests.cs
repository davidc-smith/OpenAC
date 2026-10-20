using System.Net;
using System.Text;
using AcDream.Launcher.Core.NavData;
using AcDream.Launcher.Core.Updates;
using AcDream.Rynth.NavData;

namespace AcDream.Launcher.Core.Tests.NavData;

public sealed class ReleaseNavDataPinTests
{
    private const string Sha = "09b2a02d54e6240a0adf320c0e6b31f2843a2af303e78b3a7feacdab935b7a02";

    private static string Document(string extra = "", string sha = Sha, int schema = 1, string url = "https://aelrynth.com/n.zip", long size = 1572337084) =>
        $$"""
        {"schemaVersion":{{schema}},"url":"{{url}}","size":{{size}},"sha256":"{{sha}}","files":40203,
         "bytes":4731616273,"id":"{{sha}}","rynthSuite":"e6fffb0f","pinned":"2026-10-19"{{extra}}}
        """;

    [Fact]
    public void ParsesTheDocumentedShape()
    {
        NavDataPin pin = ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document()));
        Assert.Equal(Sha, pin.Sha256);
        Assert.Equal(40203, pin.Files);
        Assert.Equal(1572337084, pin.Size);
    }

    [Fact]
    public void RejectsAnUnknownMember() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(",\"extra\":1"))));

    [Fact]
    public void RejectsABadHash() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(sha: "abc"))));

    [Fact]
    public void RejectsAnotherSchema() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(schema: 2))));

    [Fact]
    public void RejectsPlainHttp() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(url: "http://aelrynth.com/n.zip"))));

    [Fact]
    public void RejectsAZeroSize() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(size: 0))));

    [Fact]
    public void RejectsANonHexHash() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(Document(sha: new string('z', 64)))));

    [Fact]
    public void RejectsANonHexId() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(
            Document().Replace($"\"id\":\"{Sha}\"", $"\"id\":\"{new string('z', 64)}\""))));

    [Fact]
    public void RejectsZeroFiles() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(
            Document().Replace("\"files\":40203", "\"files\":0"))));

    [Fact]
    public void RejectsZeroBytes() =>
        Assert.Throws<LauncherUpdateException>(() => ReleaseNavDataPin.Parse(Encoding.UTF8.GetBytes(
            Document().Replace("\"bytes\":4731616273", "\"bytes\":0"))));

    [Fact]
    public void BuiltInPinIsTheRepositoryPin()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AcDream.slnx")))
            root = Path.GetDirectoryName(root)!;
        Assert.Equal(NavDataPin.Load(Path.Combine(root, "external", "rynth-navdata.json")), ReleaseNavDataPin.BuiltIn());
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private static ReleaseManifestClient Client(Handler handler) =>
        ReleaseManifestClient.CreateForTransportTest(
            new Uri("https://example.test/latest/manifest.json"), allowLoopbackHttp: false, handler);

    [Fact]
    public async Task FetchReadsThePinBesideTheManifest()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Document()) });
        using ReleaseManifestClient client = Client(handler);

        NavDataPin? pin = await client.FetchNavDataPinAsync();

        Assert.Equal(Sha, pin?.Sha256);
        Assert.Equal(new Uri("https://example.test/latest/rynth-navdata.json"), handler.Requested);
    }

    [Fact]
    public async Task FetchOfAMissingPinIsNull()
    {
        using ReleaseManifestClient client = Client(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await client.FetchNavDataPinAsync());
    }

    [Fact]
    public async Task FetchOfAnInvalidPinIsNull()
    {
        using ReleaseManifestClient client = Client(new Handler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"schemaVersion\":2}") }));
        Assert.Null(await client.FetchNavDataPinAsync());
    }

    [Fact]
    public async Task FetchThatTimesOutIsNull()
    {
        using ReleaseManifestClient client = ReleaseManifestClient.CreateForTransportTest(
            new Uri("https://example.test/latest/manifest.json"), allowLoopbackHttp: false, new HangingHandler());
        client.FingerprintTimeout = TimeSpan.FromMilliseconds(100);
        Assert.Null(await client.FetchNavDataPinAsync());
    }

    [Fact]
    public async Task FetchPropagatesTheCallersCancellation()
    {
        using ReleaseManifestClient client = ReleaseManifestClient.CreateForTransportTest(
            new Uri("https://example.test/latest/manifest.json"), allowLoopbackHttp: false, new HangingHandler());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchNavDataPinAsync(cts.Token));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
