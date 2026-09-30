// Copyright (c) OpenAC contributors.
// Distributed under the terms of the MIT license.

using AcDream.Plugin.Abstractions;
using AcDream.Plugin.Tests.Fixtures;

namespace AcDream.Plugin.Tests;

/// <summary>
/// The font surface is additive with inert defaults: a host that never heard
/// of it answers every request with no font, and a painter that predates it
/// still draws the text, in the interface font.
/// </summary>
public sealed class PluginFontsContractTests
{
    private sealed class BareRegistry : IUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }
    }

    private sealed class BareScopedRegistry : IScopedUiRegistry
    {
        public void AddMarkupPanel(string markupPath, object binding)
        {
        }

        public IDisposable RegisterMarkupPanel(string markupPath, object binding) =>
            NoOpUiRegistration.Instance;
    }

    /// <summary>A painter written before fonts existed: only the original members.</summary>
    private sealed class OldPainter : IPluginPainter
    {
        public List<(string Text, bool Outline)> Drawn { get; } = [];
        public int Width => 10;
        public int Height => 10;
        public void Clear(PluginColor color) { }
        public void FillRect(PluginRect rect, PluginColor color) { }
        public void StrokeRect(PluginRect rect, PluginColor color, float thickness = 1f) { }
        public void DrawLine(PluginPoint from, PluginPoint to, PluginColor color, float thickness = 1f) { }
        public void DrawText(string text, PluginPoint position, PluginColor color, bool outline = false) =>
            Drawn.Add((text, outline));
        public PluginSize MeasureText(string text) => new(text.Length * 7, 13);
        public void DrawImage(PluginImage image, PluginRect destination, PluginColor tint) { }
        public void DrawImageTransformed(
            PluginImage image, PluginRect destination, PluginColor tint, double rotationRadians,
            PluginPoint pivot, double scaleX = 1.0, double scaleY = 1.0) { }
        public void PushClip(PluginRect rect) { }
        public void PopClip() { }
    }

    [Fact]
    public void NoFontIsNotValid()
    {
        Assert.False(PluginFont.None.IsValid);
        Assert.Equal(0, PluginFont.None.Handle);
        Assert.True(new PluginFont(1, 16f, 22f, 17f).IsValid);
    }

    [Fact]
    public void ARegistryThatNeverHeardOfFontsHandsOutTheInertSurface()
    {
        IUiRegistry registry = new BareRegistry();
        IScopedUiRegistry scoped = new BareScopedRegistry();

        Assert.Same(NoOpPluginFonts.Instance, registry.Fonts);
        Assert.Same(NoOpPluginFonts.Instance, scoped.FontsFor(new PluginUiOwner("p", "P")));
        Assert.Same(NoOpPluginFonts.Instance, ((IUiRegistry)NoOpUiRegistry.Instance).Fonts);
    }

    [Fact]
    public void TheInertSurfaceRefusesEverythingAndHoldsNothing()
    {
        IPluginFonts fonts = NoOpPluginFonts.Instance;
        bool opened = false;

        Assert.False(fonts.IsAvailable);
        Assert.Equal(PluginFont.None, fonts.Bundled(16f));
        Assert.Equal(PluginFont.None, fonts.FromStream("fonts/Inter.ttf", () =>
        {
            opened = true;
            return new MemoryStream();
        }, 16f, new PluginFontOptions { Ranges = [new PluginCodepointRange(0xE000, 0xF8FF)] }));
        Assert.False(opened);
        Assert.False(fonts.Release(new PluginFont(1, 16f, 22f, 17f)));
        Assert.Equal(0, fonts.Count);
        Assert.Equal(0, fonts.MaximumCount);
        Assert.Equal(0L, fonts.MaximumBytes);
        Assert.Equal(0, fonts.MaximumGlyphs);
        Assert.Equal(0f, fonts.MinimumPixelSize);
        Assert.Equal(0f, fonts.MaximumPixelSize);
    }

    [Fact]
    public void APainterThatPredatesFontsDrawsAndMeasuresInItsOwnFont()
    {
        var old = new OldPainter();
        IPluginPainter painter = old;
        var font = new PluginFont(3, 20f, 27f, 21f);

        painter.DrawText("Golem", new PluginPoint(1, 2), PluginColor.White, font, outline: true);
        PluginSize measured = painter.MeasureText("Golem", font);

        Assert.Equal([("Golem", true)], old.Drawn);
        Assert.Equal(new PluginSize(35, 13), measured);
    }

    [Fact]
    public void TheFakeHostAnswersInertly()
    {
        var host = new FakePluginHost();

        Assert.False(host.Ui.Fonts.IsAvailable);
        Assert.Equal(PluginFont.None, host.Ui.Fonts.Bundled(16f));
    }
}
