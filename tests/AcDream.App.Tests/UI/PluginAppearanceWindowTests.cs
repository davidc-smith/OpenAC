using AcDream.App.UI;

namespace AcDream.App.Tests.UI;

/// <summary>
/// The Plugin appearance window behind the dock's gear: the gear opens and
/// closes it, as a plugin's own slot does for its window.
/// </summary>
public sealed class PluginAppearanceWindowTests
{
    private static (UiRoot Root, PluginAppearanceWindow Window, List<Action> Closers) Make()
    {
        var root = new UiRoot { Width = 800f, Height = 600f };
        var closers = new List<Action>();
        var window = new PluginAppearanceWindow(root, close =>
        {
            closers.Add(close);
            return new UiPanel { Width = 300f, Height = 200f };
        });
        return (root, window, closers);
    }

    [Fact]
    public void TheFirstToggleBuildsAndShowsTheWindow()
    {
        var (root, window, closers) = Make();

        window.Toggle();

        Assert.True(window.IsOpen);
        Assert.Single(closers);
        Assert.Single(root.Children.OfType<UiPanel>());
    }

    [Fact]
    public void ASecondToggleClosesIt()
    {
        var (_, window, _) = Make();

        window.Toggle();
        window.Toggle();

        Assert.False(window.IsOpen);
    }

    [Fact]
    public void ToggleReopensTheSameWindowRatherThanBuildingAnother()
    {
        var (root, window, closers) = Make();

        window.Toggle();
        window.Toggle();
        window.Toggle();

        Assert.True(window.IsOpen);
        Assert.Single(closers);
        Assert.Single(root.Children.OfType<UiPanel>());
    }

    [Fact]
    public void ItsCloseButtonClosesIt_AndTheNextToggleOpensIt()
    {
        var (_, window, closers) = Make();
        window.Toggle();

        closers[0]();
        Assert.False(window.IsOpen);

        window.Toggle();
        Assert.True(window.IsOpen);
    }
}
