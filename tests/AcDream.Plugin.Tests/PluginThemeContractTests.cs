// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The theme and the bundled font's weights are additive with inert
/// defaults: a host that never heard of them answers Classic, never raises a
/// change, and refuses a weight it does not have.
/// </summary>
public sealed class PluginThemeContractTests
{
    private sealed class BareRegistry : IUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }
    }

    /// <summary>A font surface written before weights existed: only the size overload.</summary>
    private sealed class OldFonts : IPluginFonts
    {
        public List<float> Asked { get; } = [];

        public PluginFont Bundled(float pixelSize)
        {
            Asked.Add(pixelSize);
            return new PluginFont(7, pixelSize, pixelSize * 1.4f, pixelSize);
        }
    }

    [Fact]
    public void AHostThatPredatesThemesAnswersClassic()
    {
        IUiRegistry ui = new BareRegistry();

        Assert.Same(PluginUiThemeInfo.Classic, ui.Theme);
        Assert.Equal(PluginUiThemeKind.Classic, ui.Theme.Kind);
        Assert.Equal("Classic", ui.Theme.DisplayName);
        Assert.Null(ui.Theme.Palette);
    }

    [Fact]
    public void AHostThatPredatesThemesAcceptsAndIgnoresHandlers()
    {
        IUiRegistry ui = new BareRegistry();
        Action<PluginUiThemeInfo> handler = _ => { };

        ui.ThemeChanged += handler;
        ui.ThemeChanged -= handler;
    }

    [Fact]
    public void AHostThatPredatesTheScreenSizeAnswersZeroAndIgnoresHandlers()
    {
        IUiRegistry ui = new BareRegistry();
        Action<PluginSize> handler = _ => { };

        ui.ScreenSizeChanged += handler;
        ui.ScreenSizeChanged -= handler;

        Assert.Equal(default, ui.ScreenSize);
        Assert.Equal(new PluginSize(0, 0), ((IUiRegistry)NoOpUiRegistry.Instance).ScreenSize);
    }

    [Fact]
    public void OutsidePressesAreOffByDefaultAndTheNewKindKeepsTheOldNumbers()
    {
        Assert.False(new PluginCanvasDescriptor("menu", 10, 10).WantsOutsidePresses);
        Assert.Equal(
            [0, 1, 2, 3, 4, 5],
            Enum.GetValues<PluginPointerEventKind>().Select(kind => (int)kind));
        Assert.Equal(PluginPointerEventKind.Cancelled + 1, PluginPointerEventKind.PressedOutside);
    }

    [Fact]
    public void TheNoOpRegistryAnswersClassic() =>
        Assert.Same(PluginUiThemeInfo.Classic, ((IUiRegistry)NoOpUiRegistry.Instance).Theme);

    [Fact]
    public void AnOldFontSurfaceServesRegularThroughTheSizeOverloadAndRefusesSemiBold()
    {
        var old = new OldFonts();
        IPluginFonts fonts = old;

        Assert.Equal(fonts.Bundled(16f), fonts.Bundled(16f, PluginFontWeight.Regular));
        Assert.Equal(PluginFont.None, fonts.Bundled(16f, PluginFontWeight.SemiBold));
        Assert.Equal([16f, 16f], old.Asked);
    }

    [Fact]
    public void TheNoOpFontSurfaceRefusesEveryWeight()
    {
        IPluginFonts fonts = NoOpPluginFonts.Instance;

        Assert.Equal(PluginFont.None, fonts.Bundled(16f, PluginFontWeight.Regular));
        Assert.Equal(PluginFont.None, fonts.Bundled(16f, PluginFontWeight.SemiBold));
    }

    [Fact]
    public void ThemesWithTheSameColoursAreEqual()
    {
        PluginThemePalette Palette() => new(
            new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12),
            new(13, 14, 15), new(16, 17, 18), new(19, 20, 21));

        Assert.Equal(
            new PluginUiThemeInfo(PluginUiThemeKind.Moss, "Charcoal + moss", Palette()),
            new PluginUiThemeInfo(PluginUiThemeKind.Moss, "Charcoal + moss", Palette()));
    }

    [Fact]
    public void TheMetricsAreTheThemedWindowsSizes()
    {
        Assert.Equal(10f, PluginThemeMetrics.WindowRadius);
        Assert.Equal(6f, PluginThemeMetrics.ControlRadius);
        Assert.Equal(24f, PluginThemeMetrics.HeaderHeight);
        Assert.Equal(26f, PluginThemeMetrics.SwitchWidth);
        Assert.Equal(14f, PluginThemeMetrics.SwitchHeight);
        Assert.Equal(8f, PluginThemeMetrics.SwitchKnob);
    }
}
