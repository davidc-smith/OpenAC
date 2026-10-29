using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.UI.Abstractions.Tests.Panels.Settings;

public sealed class SettingsStoreHudTextTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"s-{Guid.NewGuid():N}.json");

    [Fact]
    public void HudText_RoundTrips_BesideTheModernHudSwitch()
    {
        string path = TempPath();
        try
        {
            var store = new SettingsStore(path);
            Assert.Null(store.LoadHudText());
            store.SaveHudText("""{"version":1,"global":120,"overrides":{"gear":150}}""");
            store.SaveMisc(MiscSettings.Default with { ModernHud = true });

            var again = new SettingsStore(path);
            string? json = again.LoadHudText();
            Assert.NotNull(json);
            Assert.Contains("\"global\":120", json);
            Assert.Contains("\"gear\":150", json);
            Assert.True(again.LoadMisc().ModernHud);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SaveHudText_RefusesTextThatIsNotAJsonObject()
    {
        string path = TempPath();
        try
        {
            var store = new SettingsStore(path);
            Assert.Throws<ArgumentException>(() => store.SaveHudText("[1]"));
            Assert.Throws<ArgumentException>(() => store.SaveHudText("nope"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
