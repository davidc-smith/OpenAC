using AcDream.App.UI;
using AcDream.App.UI.Hud;
using AcDream.Core.Chat;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.Tests.UI.Hud;

public sealed class ChatDeckSettingsBindingTests
{
    private static (ChatDeckModel Model, ChatDeckSettingsBinding Binding) Make()
    {
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        return (model, new ChatDeckSettingsBinding(() => model));
    }

    [Fact]
    public void EveryChannel_HasAnOpaqueDefault()
    {
        foreach (ChatDeckChannel c in Enum.GetValues<ChatDeckChannel>())
            Assert.Equal(0xFFu, ChatDeckChannelColours.Default(c) >> 24);
        uint ink = ChatDeckChannelColours.Argb(ChatDeckPalette.TextInk);
        // Say is white in the retail table, so it cannot tell a real colour from the fallback; these three are not.
        Assert.NotEqual(ink, ChatDeckChannelColours.Default(ChatDeckChannel.TellOut));
        Assert.NotEqual(ink, ChatDeckChannelColours.Default(ChatDeckChannel.Combat));
        Assert.NotEqual(ink, ChatDeckChannelColours.Default(ChatDeckChannel.General));
        Assert.NotEqual(ink, ChatDeckChannelColours.Default(ChatDeckChannel.TellIn));
    }

    [Fact]
    public void EveryAppearanceSetter_ChangesOnlyItsOwnField()
    {
        var d = new ChatDeckAppearance();
        var cases = new (string Name, Action<ChatDeckSettingsBinding> Set, Func<ChatDeckAppearance, ChatDeckAppearance> Expect)[]
        {
            ("FontSize", b => b.SetFontSize(d.FontSize + 3), a => a with { FontSize = d.FontSize + 3 }),
            ("InputFontSize", b => b.SetInputFontSize(d.InputFontSize + 3), a => a with { InputFontSize = d.InputFontSize + 3 }),
            ("LineSpacing", b => b.SetLineSpacing(1.5), a => a with { LineSpacing = 1.5f }),
            ("Opacity", b => b.SetOpacity(40), a => a with { BackgroundOpacity = 0.4f }),
            ("Timestamps", b => b.SetTimestamps(2), a => a with { Timestamps = ChatDeckTimestamps.HourMinuteSecond }),
            ("ChannelPrefix", b => b.SetChannelPrefix(!d.ChannelPrefix), a => a with { ChannelPrefix = !d.ChannelPrefix }),
            ("WrapIndent", b => b.SetWrapIndent(!d.WrapIndent), a => a with { WrapIndent = !d.WrapIndent }),
            ("FadeWhenIdle", b => b.SetFadeWhenIdle(!d.FadeWhenIdle), a => a with { FadeWhenIdle = !d.FadeWhenIdle }),
            ("FadeAfterSeconds", b => b.SetFadeAfterSeconds(d.FadeAfterSeconds + 7), a => a with { FadeAfterSeconds = d.FadeAfterSeconds + 7 }),
            ("Beep", b => b.SetBeep(!d.BeepOnAlert), a => a with { BeepOnAlert = !d.BeepOnAlert }),
        };
        foreach (var (name, set, expect) in cases)
        {
            var (model, b) = Make();
            ChatDeckAppearance before = model.Settings.Appearance;
            set(b);
            ChatDeckAppearance want = expect(before);
            ChatDeckAppearance got = model.Settings.Appearance;
            Assert.NotEqual(before, got);
            Assert.True(want.FontSize == got.FontSize && want.InputFontSize == got.InputFontSize
                && want.LineSpacing == got.LineSpacing && want.BackgroundOpacity == got.BackgroundOpacity
                && want.Timestamps == got.Timestamps && want.ChannelPrefix == got.ChannelPrefix
                && want.WrapIndent == got.WrapIndent && want.FadeWhenIdle == got.FadeWhenIdle
                && want.FadeAfterSeconds == got.FadeAfterSeconds && want.BeepOnAlert == got.BeepOnAlert,
                $"{name} did not change exactly its own field");
        }
    }

    [Fact]
    public void PreviousTabKey_RoundTripsAndLeavesNext()
    {
        var (model, b) = Make();
        string next = model.Settings.Keys.NextTab;
        b.SetPreviousTabKey("Ctrl+Alt+P");
        Assert.Equal("Ctrl+Alt+P", model.Settings.Keys.PreviousTab);
        Assert.Equal("Ctrl+Alt+P", b.PreviousTabKey);
        Assert.Equal(next, model.Settings.Keys.NextTab);
    }

    [Fact]
    public void Setters_WriteTheirOwnField()
    {
        var (model, b) = Make();
        b.SetFontSize(16);
        b.SetOpacity(40);
        b.SetTimestamps(2);
        b.SetChannelPrefix(false);
        b.SetBeep(true);
        ChatDeckAppearance a = model.Settings.Appearance;
        Assert.Equal(16, a.FontSize);
        Assert.Equal(0.4f, a.BackgroundOpacity, 3);
        Assert.Equal(ChatDeckTimestamps.HourMinuteSecond, a.Timestamps);
        Assert.False(a.ChannelPrefix);
        Assert.True(a.BeepOnAlert);
        Assert.Equal(new ChatDeckAppearance().InputFontSize, a.InputFontSize);
        Assert.Equal(40, b.Opacity);
    }

    [Fact]
    public void Keys_RoundTrip()
    {
        var (model, b) = Make();
        b.SetNextTabKey("Ctrl+Alt+N");
        Assert.Equal("Ctrl+Alt+N", model.Settings.Keys.NextTab);
        Assert.Equal("Ctrl+Alt+N", b.NextTabKey);
    }

    [Fact]
    public void Colours_OverrideDefaultAndReset()
    {
        var (model, b) = Make();
        ChatDeckSettingsBinding.ColourRow row = b.Colours.First(r => r.Id == nameof(ChatDeckChannel.General));
        Assert.Equal($"#{ChatDeckChannelColours.Default(ChatDeckChannel.General):X8}", row.Hex);
        row.SetHex("#FF112233");
        Assert.Equal(0xFF112233u, model.Settings.Appearance.ChannelColours[ChatDeckChannel.General]);
        row.SetHex(row.DefaultHex);
        Assert.False(model.Settings.Appearance.ChannelColours.ContainsKey(ChatDeckChannel.General));
        row.SetHex("#FF445566");
        b.ResetColours();
        Assert.Empty(model.Settings.Appearance.ChannelColours);
    }

    [Fact]
    public void NoModel_ReadsDefaults_IgnoresSets()
    {
        var d = new ChatDeckAppearance();
        var k = new ChatDeckKeys();
        var model = new ChatDeckModel(new ChatLog(), ChatDeckDefaults.Create(), "Davian");
        ChatDeckModel? current = model;
        var b = new ChatDeckSettingsBinding(() => current);
        ChatDeckSettings before = model.Settings;
        current = null;

        var ex = Record.Exception(() =>
        {
            b.SetFontSize(18); b.SetInputFontSize(18); b.SetLineSpacing(1.5); b.SetOpacity(10);
            b.SetTimestamps(2); b.SetChannelPrefix(!d.ChannelPrefix); b.SetWrapIndent(!d.WrapIndent);
            b.SetFadeWhenIdle(!d.FadeWhenIdle); b.SetFadeAfterSeconds(99); b.SetBeep(!d.BeepOnAlert);
            b.SetNextTabKey("Ctrl+Alt+N"); b.SetPreviousTabKey("Ctrl+Alt+P");
            b.Colours[0].SetHex("#FF112233"); b.ResetColours();
        });
        Assert.Null(ex);
        Assert.Equal(d.FontSize, b.FontSize);
        Assert.Equal(d.InputFontSize, b.InputFontSize);
        Assert.Equal(Math.Round(d.LineSpacing, 1), b.LineSpacing);
        Assert.Equal((int)MathF.Round(d.BackgroundOpacity * 100f), b.Opacity);
        Assert.Equal((int)d.Timestamps, b.Timestamps);
        Assert.Equal(d.ChannelPrefix, b.ChannelPrefix);
        Assert.Equal(d.WrapIndent, b.WrapIndent);
        Assert.Equal(d.FadeWhenIdle, b.FadeWhenIdle);
        Assert.Equal(d.FadeAfterSeconds, b.FadeAfterSeconds);
        Assert.Equal(d.BeepOnAlert, b.Beep);
        Assert.Equal(k.NextTab, b.NextTabKey);
        Assert.Equal(k.PreviousTab, b.PreviousTabKey);
        Assert.Equal(before, model.Settings);
    }

    [Fact]
    public void Markup_Builds()
    {
        var (_, b) = Make();
        UiElement window = MarkupDocument.Build(ChatDeckSettingsBinding.Markup, b, _ => (0u, 0, 0));
        Assert.NotNull(window);
    }

    [Fact]
    public void Prepare_LetsHotkeyBoxesTakeCommand()
    {
        var (_, b) = Make();
        UiElement window = ChatDeckSettingsBinding.Prepare(MarkupDocument.Build(ChatDeckSettingsBinding.Markup, b, _ => (0u, 0, 0)));
        var boxes = new List<UiMarkupHotkey>();
        void Walk(UiElement e) { if (e is UiMarkupHotkey k) boxes.Add(k); foreach (var c in e.Children) Walk(c); }
        Walk(window);
        Assert.NotEmpty(boxes);
        Assert.All(boxes, k => Assert.True(k.AllowsCommand));
    }

    [Fact]
    public void Popup_BuiltWithFixedPalette_DrawsInTheDecksPalette()
    {
        var (_, b) = Make();
        var themes = new PluginUiThemeSettings().WithFixedPalette(ChatDeckPalette.Window);
        UiElement window = MarkupDocument.Build(ChatDeckSettingsBinding.Markup, b, _ => (0u, 0, 0), themes: themes);
        var panel = Assert.IsType<UiPluginMarkupPanel>(window);
        Assert.Same(ChatDeckPalette.Window, panel.Palette);
    }
}
