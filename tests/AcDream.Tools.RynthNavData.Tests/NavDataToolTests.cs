using System.IO.Compression;
using System.Security.Cryptography;

namespace AcDream.Tools.RynthNavData.Tests;

public sealed class NavDataToolTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rynth-navdata-tool-").FullName;
    private readonly LocalServer _server = new();
    private readonly HttpClient _http = new();

    private string Cache => Path.Combine(_root, "cache");
    private string NavData => Path.Combine(_root, "NavData");

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static byte[] Archive(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }

    private static readonly byte[] Good = Archive(("nav_A9B4.tile", "tile a"), ("nav_A9B5.tile", "tile b"), ("locations.json", "{}"));

    private Task<NavDataPin> Pin(byte[] archive) =>
        NavDataPinner.PinAsync(_server.Add("RynthNav-NavData.zip", archive), Cache, "e6fffb0f", new DateOnly(2026, 10, 19), _http, TextWriter.Null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task PinRecordsTheArchive()
    {
        NavDataPin pin = await Pin(Good);

        Assert.Equal(Good.Length, pin.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Good)).ToLowerInvariant(), pin.Sha256);
        Assert.Equal(pin.Sha256, pin.Id);
        Assert.Equal(3, pin.Files);
        Assert.Equal("tile a".Length + "tile b".Length + 2, pin.Bytes);
        Assert.Equal("e6fffb0f", pin.RynthSuite);
        Assert.Equal("2026-10-19", pin.Pinned);
        Assert.True(File.Exists(Path.Combine(Cache, $"navdata-{pin.Id}.zip")));
    }

    [Fact]
    public async Task PinRefusesANameRynthNavWouldNotRead()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Pin(Archive(("nav_A9B4.tile", "a"), ("evil.exe", "x"))));
        Assert.Empty(Directory.GetFiles(Cache, "navdata-*.zip"));
    }

    [Fact]
    public async Task ThePinRoundTripsThroughItsFile()
    {
        NavDataPin pin = await Pin(Good);
        string path = Path.Combine(_root, "rynth-navdata.json");
        pin.Save(path);

        Assert.Equal(pin, NavDataPin.Load(path));
        Assert.Contains("\"sha256\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPutsTheTilesInAndKeepsThePlayersFiles()
    {
        NavDataPin pin = await Pin(Good);
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "atlas.txt"), "my places");

        await NavDataInstaller.InstallAsync(pin, NavData, Cache, null, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));
        Assert.Equal("my places", File.ReadAllText(Path.Combine(NavData, "atlas.txt")));
        Assert.Equal(pin.Id, File.ReadAllText(Path.Combine(NavData, "navdata.id")).Trim());
        Assert.True(Directory.Exists(NavData + ".previous"));
    }

    [Fact]
    public async Task InstallRefusesAnArchiveThatIsNotThePinnedOne()
    {
        NavDataPin pin = await Pin(Good) with { Sha256 = new string('0', 64), Id = new string('0', 64) };
        File.Delete(Path.Combine(Cache, $"navdata-{Convert.ToHexString(SHA256.HashData(Good)).ToLowerInvariant()}.zip"));
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "atlas.txt"), "my places");

        await Assert.ThrowsAsync<InvalidDataException>(() => NavDataInstaller.InstallAsync(pin, NavData, Cache, null, TestContext.Current.CancellationToken));

        Assert.Equal(["atlas.txt"], Directory.GetFiles(NavData).Select(Path.GetFileName));
    }

    [Fact]
    public async Task InstallLeavesLocalTilesAlone()
    {
        NavDataPin pin = await Pin(Good);
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "navdata.local"), "");

        string said = await NavDataInstaller.InstallAsync(pin, NavData, Cache, null, TestContext.Current.CancellationToken);

        Assert.Contains("navdata.local", said, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));
    }

    [Fact]
    public async Task InstallLeavesALauncherManagedFolderAloneUnlessForced()
    {
        NavDataPin pin = await Pin(Good);
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "navdata.id"), new string('a', 32));
        int before = _server.Requests;

        string said = await NavDataInstaller.InstallAsync(pin, NavData, Cache, null, TestContext.Current.CancellationToken);

        Assert.Contains("launcher", said, StringComparison.Ordinal);
        Assert.Equal(before, _server.Requests);
        Assert.False(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));

        await NavDataInstaller.InstallAsync(pin, NavData, Cache, null, TestContext.Current.CancellationToken, force: true);

        Assert.True(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));
        Assert.Equal(pin.Id, File.ReadAllText(Path.Combine(NavData, "navdata.id")).Trim());
    }

    [Fact]
    public async Task AFailedDownloadLeavesNoPartFile()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => NavDataPinner.PinAsync(_server.Base + "missing.zip", Cache,
            "e6fffb0f", new DateOnly(2026, 10, 19), _http, TextWriter.Null, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(Cache, "navdata-pin.part")));
    }

    [Fact]
    public async Task InstallDoesNothingWhenThePinnedTilesAreThere()
    {
        NavDataPin pin = await Pin(Good);
        Directory.CreateDirectory(NavData);
        File.WriteAllText(Path.Combine(NavData, "navdata.id"), pin.Id);
        int before = _server.Requests;

        await NavDataInstaller.InstallAsync(pin, NavData, Cache, null, TestContext.Current.CancellationToken);

        Assert.Equal(before, _server.Requests);
        Assert.False(File.Exists(Path.Combine(NavData, "nav_A9B4.tile")));
    }
}
