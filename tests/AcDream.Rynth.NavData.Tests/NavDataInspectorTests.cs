using AcDream.Rynth.NavData;

namespace AcDream.Rynth.NavData.Tests;

public sealed class NavDataInspectorTests : IDisposable
{
    private const string PinId = "09b2a02d54e6240a0adf320c0e6b31f2843a2af303e78b3a7feacdab935b7a02";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navdata-inspect-" + Guid.NewGuid().ToString("N"));
    private static readonly NavDataPin Pin = new("https://example.invalid/n.zip", 10, PinId, 3, 30, PinId, "e6fffb0f", "2026-10-19");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NoFolderIsMissing() =>
        Assert.Equal(NavDataInstallState.Missing, NavDataInspector.Inspect(Pin, _dir).State);

    [Fact]
    public void MatchingIdIsCurrent()
    {
        Write("navdata.id", PinId.ToUpperInvariant() + "\n");
        Assert.Equal(NavDataInstallState.Current, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void OtherOfOurIdsIsOutdated()
    {
        Write("navdata.id", new string('a', 64));
        Assert.Equal(NavDataInstallState.Outdated, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void TheirLaunchersIdIsLeftToIt()
    {
        Write("navdata.id", new string('b', 32));
        Assert.Equal(NavDataInstallState.RynthCoreManaged, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void SixtyFourNonHexCharactersAreNotOurs()
    {
        Write("navdata.id", new string('z', 64));
        Assert.Equal(NavDataInstallState.RynthCoreManaged, NavDataInspector.Inspect(Pin, _dir).State);
        Assert.False(NavDataInspector.IsOurId(new string('z', 64)));
        Assert.True(NavDataInspector.IsOurId(PinId));
    }

    [Fact]
    public void LocalMarkerWinsOverEverything()
    {
        Write("navdata.id", PinId);
        Write("navdata.local", "");
        Assert.Equal(NavDataInstallState.LocalMarker, NavDataInspector.Inspect(Pin, _dir).State);
    }

    [Fact]
    public void InspectionCarriesTheFolderAndInstalledId()
    {
        Write("navdata.id", new string('a', 64));
        NavDataInspection inspection = NavDataInspector.Inspect(Pin, _dir);
        Assert.Equal(_dir, inspection.Directory);
        Assert.Equal(new string('a', 64), inspection.InstalledId);
    }

    [Fact]
    public void OurStagesMatchTheirs() =>
        Assert.Equal(
            Enum.GetNames<RynthCore.App.Avalonia.DownloadStage>(),
            Enum.GetNames<NavDataStage>());

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, name), text);
    }
}
