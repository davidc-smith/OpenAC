using System.Numerics;
using AcDream.App.UI;
using AcDream.UI.Abstractions.Panels.Settings;
namespace AcDream.App.Tests.UI;

public sealed class PluginUiThemeTests
{
    private sealed class Binding
    {
        public uint StateColor => 0xFF123456;
        public IReadOnlyList<string> Items => ["A", "B"];
        public string Selected => "A";
    }
    private static UiNineSlicePanel Build(string theme, PluginUiThemeSettings settings) => MarkupDocument.Build($$"""
        <panel x="0" y="0" w="250" h="200" {{theme}}>
          <group x="0" y="0" w="200" h="150" visible="false">
            <label x="0" y="0" text="ExactVariableName" />
            <label x="0" y="20" text="Status" color="{StateColor}" />
            <label x="0" y="40" text="Muted" color="theme:muted|#FF123456" />
            <menu x="0" y="60" w="150" h="20" items="{Items}" selected="{Selected}" searchable="true" />
          </group>
        </panel>
        """, new Binding(), _ => (0u, 0, 0), themes: settings);

    [Fact]
    public void OptedInWindowSwitchesAndRestoresClassicIncludingHiddenGroups()
    {
        var settings = new PluginUiThemeSettings();
        var panel = Build("theme=\"plugin\"", settings);
        var root = new UiRoot(); root.AddChild(panel);
        var group = panel.Children[0];
        var label = (UiLabel)group.Children[0];
        var status = (UiLabel)group.Children[1];
        var muted = (UiLabel)group.Children[2];
        var original = label.TextColor;
        var originalMuted = muted.TextColorSource!();
        foreach (var theme in new[] { PluginUiTheme.Moss, PluginUiTheme.Brass })
        {
            settings.Theme = theme; root.Tick(0.016, 10);
            Assert.Equal(settings.Palette!.Text, label.TextColor);
            Assert.False(label.Outline);
            Assert.Equal(originalMuted, status.TextColorSource!());
            Assert.Equal(settings.Palette.Muted, muted.TextColorSource!());
            Assert.Equal("ExactVariableName", label.TextSource!());
        }
        settings.Theme = PluginUiTheme.Classic; root.Tick(0.016, 20);
        Assert.Equal(original, label.TextColor);
        Assert.Equal(originalMuted, muted.TextColorSource!());
        Assert.True(label.Outline);
        Assert.True(((UiMenu)group.Children[3]).Searchable);
    }

    [Fact]
    public void NonOptedInWindowKeepsClassicWhenGlobalThemeChanges()
    {
        var settings = new PluginUiThemeSettings { Theme = PluginUiTheme.Moss };
        var panel = Build("", settings);
        Assert.IsType<UiNineSlicePanel>(panel);
        Assert.True(((UiLabel)panel.Children[0].Children[0]).Outline);
        Assert.Equal(Vector4.One, ((UiLabel)panel.Children[0].Children[0]).TextColor);
    }

    [Fact]
    public void ReadOnlyMultilineDescriptionKeepsBindingAndDoesNotCaptureTyping()
    {
        var panel = MarkupDocument.Build("""
            <panel x="0" y="0" w="200" h="100">
              <field x="1" y="1" w="180" h="70" text="ExactVariableName description"
                     oneline="false" editable="false" />
            </panel>
            """, new object(), _ => (0u, 0, 0));
        var field = Assert.IsType<UiField>(panel.Children[0]);
        Assert.False(field.OneLine);
        Assert.False(field.Editable);
        Assert.False(field.AcceptsFocus);
        Assert.False(field.IsEditControl);
        Assert.Equal("ExactVariableName description", field.BoundText!());
    }

    [Theory]
    [InlineData(PluginUiTheme.LavenderDusk, "Lavender Dusk", 0x17141Fu, 0xBBA0FFu, 0x512F4Au)]
    [InlineData(PluginUiTheme.Ember, "Ember", 0x151A21u, 0xFF9447u, 0x1E3B44u)]
    [InlineData(PluginUiTheme.Bloodmoon, "Bloodmoon", 0x1C1617u, 0xFF6B6Bu, 0x4D2327u)]
    [InlineData(PluginUiTheme.Glacier, "Glacier", 0x11161Cu, 0x5ED6E8u, 0x1F3554u)]
    [InlineData(PluginUiTheme.Parchment, "Parchment", 0xEDE7DCu, 0xB4471Fu, 0xDDCFB6u)]
    public void NewThemesHaveTheirNameAndColours(PluginUiTheme theme, string name, uint background, uint accent, uint selected)
    {
        var settings = new PluginUiThemeSettings { Theme = theme };
        var palette = settings.Palette!;
        Assert.Equal(Rgb(background), palette.Background);
        Assert.Equal(Rgb(accent), palette.Accent);
        Assert.Equal(Rgb(selected), palette.Selected);
        Assert.Same(palette, settings.DockPalette);
        Assert.Equal(name, settings.ThemeInfo.DisplayName);
        Assert.Equal(theme.ToString(), settings.ThemeInfo.Kind.ToString());
    }

    [Fact]
    public void EveryThemeButClassicHasItsOwnPaletteAndAMatchingKind()
    {
        var themes = Enum.GetValues<PluginUiTheme>();
        Assert.Equal(themes.Length, PluginUiThemeSettings.ThemeNames.Count);
        Assert.Equal(
            themes.Select(t => t.ToString()),
            Enum.GetValues<AcDream.Plugin.Abstractions.PluginUiThemeKind>().Select(k => k.ToString()));
        var palettes = themes.Where(t => t != PluginUiTheme.Classic)
            .Select(t => new PluginUiThemeSettings { Theme = t }.Palette).ToList();
        Assert.All(palettes, Assert.NotNull);
        Assert.Equal(palettes.Count, palettes.Distinct().Count());
    }

    [Fact]
    public void OnlyParchmentIsLight()
    {
        foreach (PluginUiTheme theme in Enum.GetValues<PluginUiTheme>())
        {
            var settings = new PluginUiThemeSettings { Theme = theme };
            bool light = theme == PluginUiTheme.Parchment;
            Assert.Equal(light, settings.Palette?.IsLight == true);
            Assert.Equal(light, settings.ThemeInfo.IsLight);
        }
        Assert.False(PluginUiPalette.ClassicDock.IsLight);
    }

    [Theory]
    [InlineData("theme=\"plugin\"", PluginUiTheme.Parchment, true)]
    [InlineData("theme=\"plugin\"", PluginUiTheme.Moss, false)]
    [InlineData("theme=\"plugin\"", PluginUiTheme.Classic, false)]
    [InlineData("", PluginUiTheme.Parchment, false)]
    public void ALightColourIsUsedUnderALightThemeInAThemedWindow(string theme, PluginUiTheme selected, bool light)
    {
        var settings = new PluginUiThemeSettings();
        var panel = MarkupDocument.Build($$"""
            <panel x="0" y="0" w="250" h="200" {{theme}}>
              <label x="0" y="0" text="Status" color="#FF112233|light:#FF445566" />
              <group x="0" y="20" w="50" h="50" border="#FF778899|light:#FFAABBCC" />
            </panel>
            """, new Binding(), _ => (0u, 0, 0), themes: settings);
        var root = new UiRoot(); root.AddChild(panel);
        var label = (UiLabel)panel.Children[0];
        var group = (UiPanel)panel.Children[1];
        Assert.Equal(Rgba(0xFF112233u), label.TextColor);

        settings.Theme = selected; root.Tick(0.016, 10);

        Assert.Equal(Rgba(light ? 0xFF445566u : 0xFF112233u), label.TextColorSource!());
        Assert.Equal(Rgba(light ? 0xFFAABBCCu : 0xFF778899u), group.BorderColorSource!());
    }

    private static Vector4 Rgba(uint argb) =>
        new((argb >> 16 & 255) / 255f, (argb >> 8 & 255) / 255f, (argb & 255) / 255f, (argb >> 24) / 255f);

    [Fact]
    public void ANewThemePersistsByName()
    {
        string path = Path.Combine(Path.GetTempPath(), $"plugin-theme-{Guid.NewGuid()}.json");
        try
        {
            var store = new SettingsStore(path);
            new PluginUiThemeSettings(store).Theme = PluginUiTheme.Parchment;
            Assert.Equal("Parchment", store.LoadPluginUi().Theme);
            Assert.Equal(PluginUiTheme.Parchment, new PluginUiThemeSettings(store).Theme);
        }
        finally { File.Delete(path); }
    }

    private static Vector4 Rgb(uint rgb) =>
        new((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1f);

    [Fact]
    public void ThemePersistsWithoutReplacingOtherSettings()
    {
        string path = Path.Combine(Path.GetTempPath(), $"plugin-theme-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "{\"other\":{\"keep\":true}}");
            var store = new SettingsStore(path);
            Assert.Equal(PluginUiTheme.Classic, new PluginUiThemeSettings(store).Theme);
            new PluginUiThemeSettings(store).Theme = PluginUiTheme.Brass;
            Assert.Equal(PluginUiTheme.Brass, new PluginUiThemeSettings(store).Theme);
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            Assert.True(json.RootElement.GetProperty("other").GetProperty("keep").GetBoolean());
        }
        finally { File.Delete(path); }
    }
}
