using AcDream.App.UI;
using Xunit;

namespace AcDream.App.Tests.UI;

public sealed class PluginThemeScopeTests
{
    private static (UiPluginMarkupPanel panel, PluginUiThemeSettings settings) Make()
    {
        var settings = new PluginUiThemeSettings();
        return (new UiPluginMarkupPanel(_ => (0u, 0, 0), settings), settings);
    }

    private static void SwitchTheme(UiPluginMarkupPanel panel, PluginUiThemeSettings settings, PluginUiTheme theme)
    {
        settings.Theme = theme;
        panel.TickSelfAndChildren(0.01);
    }

    [Fact]
    public void Scoped_actions_run_until_the_scope_is_disposed()
    {
        var (panel, settings) = Make();
        int runs = 0;
        var scope = panel.BeginThemeScope();
        panel.AddThemeAction(_ => runs++);
        scope.Close();
        int afterAdd = runs;

        SwitchTheme(panel, settings, PluginUiTheme.Moss);
        Assert.True(runs > afterAdd);

        scope.Dispose();
        int afterDispose = runs;
        SwitchTheme(panel, settings, PluginUiTheme.Classic);
        Assert.Equal(afterDispose, runs);
    }

    [Fact]
    public void Actions_outside_a_scope_are_unaffected()
    {
        var (panel, settings) = Make();
        int outside = 0;
        panel.AddThemeAction(_ => outside++);
        var scope = panel.BeginThemeScope();
        panel.AddThemeAction(_ => { });
        scope.Dispose();
        int before = outside;

        SwitchTheme(panel, settings, PluginUiTheme.Moss);

        Assert.True(outside > before);
    }

    [Fact]
    public void Actions_added_after_close_are_not_recorded()
    {
        var (panel, settings) = Make();
        int late = 0;
        var scope = panel.BeginThemeScope();
        scope.Close();
        panel.AddThemeAction(_ => late++);
        scope.Dispose();
        int before = late;

        SwitchTheme(panel, settings, PluginUiTheme.Moss);

        Assert.True(late > before);
    }

    [Fact]
    public void A_second_open_scope_throws_and_a_closed_one_allows_the_next()
    {
        var (panel, _) = Make();
        var scope = panel.BeginThemeScope();

        Assert.Throws<InvalidOperationException>(() => panel.BeginThemeScope());

        scope.Close();
        panel.BeginThemeScope().Dispose();
    }
}
