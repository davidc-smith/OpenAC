using AcDream.App.UI.Hud;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI.Hud;

public sealed class HudTextTests
{
    [Fact]
    public void ASet_SavesAndRaisesChangedOnce_AnEqualSetDoesNothing()
    {
        var saved = new List<HudTextSettings>();
        var text = new HudText(null, saved.Add);
        int changed = 0;
        text.Changed += () => changed++;

        text.SetGlobal(130);
        text.SetGlobal(128);                    // snaps to 130: no change
        text.SetOverride(HudDeck.Gear, 150);
        text.SetOverride(HudDeck.Gear, 150);

        Assert.Equal(2, changed);
        Assert.Equal(2, saved.Count);
        Assert.Equal(1.3f, text.FactorFor(HudDeck.Command), 3);
        Assert.Equal(1.5f, text.FactorFor(HudDeck.Gear), 3);
    }

    [Fact]
    public void Load_WithoutAStore_IsTheDefaults() =>
        Assert.Equal(HudTextSettings.Default, HudText.Load(null).Settings);

    [Fact]
    public void Load_ReadsWhatAnEarlierRunSaved()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hudtext-{Guid.NewGuid():N}.json");
        try
        {
            HudText first = HudText.Load(new SettingsStore(path));
            first.SetGlobal(120);
            first.SetOverride(HudDeck.Chat, 90);

            HudText second = HudText.Load(new SettingsStore(path));
            Assert.Equal(120, second.Settings.Global);
            Assert.Equal(90, second.Settings.PercentFor(HudDeck.Chat));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
