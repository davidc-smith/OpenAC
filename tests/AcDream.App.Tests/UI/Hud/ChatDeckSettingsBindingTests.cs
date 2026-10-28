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
        var b = new ChatDeckSettingsBinding(() => null);
        b.SetFontSize(18);
        Assert.Equal(new ChatDeckAppearance().FontSize, b.FontSize);
    }

    [Fact]
    public void Markup_Builds()
    {
        var (_, b) = Make();
        UiElement window = MarkupDocument.Build(ChatDeckSettingsBinding.Markup, b, _ => (0u, 0, 0));
        Assert.NotNull(window);
    }
}
