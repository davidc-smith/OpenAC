using AcDream.Rynth.NavData;
using RynthCore.App.Avalonia;

namespace AcDream.Rynth.NavData.Tests;

public sealed class NavDataPinAndProgressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navdata-pin-" + Guid.NewGuid().ToString("N"));
    private static readonly NavDataPin Pin = new("https://example.invalid/n.zip", 1_600_000_000, "ab", 3, 4_700_000_000, "cd", "e6fffb0f", "2026-10-19");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveThenLoadRoundTrips()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "pin.json");
        Pin.Save(path);
        Assert.Equal(Pin, NavDataPin.Load(path));
        Assert.Contains("\"rynthSuite\"", File.ReadAllText(path));
    }

    [Fact]
    public void ParseReadsWhatSaveWrote()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "pin.json");
        Pin.Save(path);
        Assert.Equal(Pin, NavDataPin.Parse(File.ReadAllBytes(path)));
    }

    [Fact]
    public void ParseRejectsANullDocument() =>
        Assert.Throws<InvalidDataException>(() => NavDataPin.Parse("null"u8));

    [Fact]
    public void AdapterMapsEveryStagePercentAndDetail()
    {
        var seen = new List<NavDataProgress>();
        var adapter = new NavDataInstaller.Adapter(new Collect(seen));
        var expected = new List<NavDataProgress>();
        foreach (DownloadStage stage in Enum.GetValues<DownloadStage>())
        {
            var theirs = new DownloadProgress("RynthNav map", stage, 25, 100);
            adapter.Report(theirs);
            expected.Add(new NavDataProgress(Enum.Parse<NavDataStage>(stage.ToString()), 25, theirs.Detail));
        }
        var none = new DownloadProgress("RynthNav map", DownloadStage.Installing, 0, 0);
        adapter.Report(none);
        expected.Add(new NavDataProgress(NavDataStage.Installing, null, none.Detail));
        Assert.Equal(expected, seen);
        Assert.Equal(Enum.GetValues<DownloadStage>().Length + 1, seen.Count);
    }

    private sealed class Collect(List<NavDataProgress> l) : IProgress<NavDataProgress>
    {
        public void Report(NavDataProgress value) => l.Add(value);
    }
}
