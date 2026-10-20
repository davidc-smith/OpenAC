using AcDream.Launcher.Core.NavData;

namespace AcDream.Launcher.Core.Tests.NavData;

public sealed class LauncherNavDataConsentStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "navdata-consent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NoFileIsNotAgreed() =>
        Assert.False(new LauncherNavDataConsentStore(Path.Combine(_dir, LauncherNavDataConsentStore.FileName)).IsAgreed);

    [Fact]
    public void AgreeIsRemembered()
    {
        string path = Path.Combine(_dir, LauncherNavDataConsentStore.FileName);
        new LauncherNavDataConsentStore(path).Agree();
        Assert.True(new LauncherNavDataConsentStore(path).IsAgreed);
        Assert.Contains("\"agreed\": true", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void CorruptFileIsNotAgreed()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, LauncherNavDataConsentStore.FileName);
        File.WriteAllText(path, "{not json");
        Assert.False(new LauncherNavDataConsentStore(path).IsAgreed);
    }
}
