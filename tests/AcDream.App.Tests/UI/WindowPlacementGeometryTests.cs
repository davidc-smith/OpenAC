using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.Tests.UI;

public sealed class WindowPlacementGeometryTests
{
    [Fact]
    public void CommandDeck_DefaultsToBottomCentre()
    {
        var authored = new UiWindowLayout(0f, 0f, 420f, 150f, true, false, false);
        var windows = new Dictionary<string, UiWindowLayout>();

        var placed = WindowPlacementGeometry.Default(WindowNames.CommandDeck, authored, 1920, 1080, windows);

        Assert.Equal((1920f - 420f) * 0.5f, placed.X);
        Assert.Equal(1080f - 150f - 20f, placed.Y);
    }
}
