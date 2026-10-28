using System.Globalization;
using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

/// <summary>What the Chat Deck settings popup reads and writes: the appearance, the extra tab keys, the channel colours.</summary>
public sealed class ChatDeckSettingsBinding
{
    /// <summary>
    /// Lets the popup's hotkey boxes record Cmd chords and connects the title bar's X to
    /// <paramref name="close"/>; returns <paramref name="window"/>.
    /// </summary>
    internal static UiElement Prepare(UiElement window, Action close)
    {
        Walk(window, close);
        return window;
    }

    private static void Walk(UiElement element, Action close)
    {
        if (element is UiMarkupHotkey box) box.AllowsCommand = true;
        if (element is PluginTitleBar bar) bar.CloseRequested += close;
        foreach (UiElement child in element.Children) Walk(child, close);
    }

    private readonly Func<ChatDeckModel?> _model;

    public ChatDeckSettingsBinding(Func<ChatDeckModel?> model)
    {
        _model = model;
        Colours = Enum.GetValues<ChatDeckChannel>().Select(c => new ColourRow(this, c)).ToList();
    }

    private ChatDeckAppearance A => _model()?.Settings.Appearance ?? new ChatDeckAppearance();
    private ChatDeckKeys K => _model()?.Settings.Keys ?? new ChatDeckKeys();
    private void Set(Func<ChatDeckAppearance, ChatDeckAppearance> change) { if (_model() is { } m) m.SetAppearance(change(m.Settings.Appearance)); }
    private void SetKeys(Func<ChatDeckKeys, ChatDeckKeys> change) { if (_model() is { } m) m.SetKeys(change(m.Settings.Keys)); }

    public int FontSize => A.FontSize;
    public Action<int> SetFontSize => v => Set(a => a with { FontSize = v });
    public int InputFontSize => A.InputFontSize;
    public Action<int> SetInputFontSize => v => Set(a => a with { InputFontSize = v });
    public double LineSpacing => Math.Round(A.LineSpacing, 1);
    public Action<double> SetLineSpacing => v => Set(a => a with { LineSpacing = (float)v });
    /// <summary>Background opacity as a percent.</summary>
    public int Opacity => (int)MathF.Round(A.BackgroundOpacity * 100f);
    public Action<int> SetOpacity => v => Set(a => a with { BackgroundOpacity = v / 100f });
    public int Timestamps => (int)A.Timestamps;
    public Action<int> SetTimestamps => v => Set(a => a with { Timestamps = (ChatDeckTimestamps)Math.Clamp(v, 0, 2) });
    public bool ChannelPrefix => A.ChannelPrefix;
    public Action<bool> SetChannelPrefix => v => Set(a => a with { ChannelPrefix = v });
    public bool WrapIndent => A.WrapIndent;
    public Action<bool> SetWrapIndent => v => Set(a => a with { WrapIndent = v });
    public bool FadeWhenIdle => A.FadeWhenIdle;
    public Action<bool> SetFadeWhenIdle => v => Set(a => a with { FadeWhenIdle = v });
    public int FadeAfterSeconds => A.FadeAfterSeconds;
    public Action<int> SetFadeAfterSeconds => v => Set(a => a with { FadeAfterSeconds = v });
    public bool Beep => A.BeepOnAlert;
    public Action<bool> SetBeep => v => Set(a => a with { BeepOnAlert = v });

    public string NextTabKey => K.NextTab;
    public Action<string> SetNextTabKey => v => SetKeys(k => k with { NextTab = v });
    public string PreviousTabKey => K.PreviousTab;
    public Action<string> SetPreviousTabKey => v => SetKeys(k => k with { PreviousTab = v });

    public IReadOnlyList<ColourRow> Colours { get; }
    public Action ResetColours => () => Set(a => a with { ChannelColours = new Dictionary<ChatDeckChannel, uint>() });
    public Action? Close { get; set; }

    internal uint ColourOf(ChatDeckChannel c) =>
        A.ChannelColours.TryGetValue(c, out uint argb) ? argb : ChatDeckChannelColours.Default(c);

    internal void SetColour(ChatDeckChannel c, string hex)
    {
        if (!uint.TryParse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb)) return;
        Set(a =>
        {
            var map = new Dictionary<ChatDeckChannel, uint>(a.ChannelColours);
            if (argb == ChatDeckChannelColours.Default(c)) map.Remove(c);
            else map[c] = argb;
            return a with { ChannelColours = map };
        });
    }

    /// <summary>One channel's colour in the popup's list.</summary>
    public sealed class ColourRow(ChatDeckSettingsBinding owner, ChatDeckChannel channel)
    {
        public string Id => channel.ToString();
        public string Name => ChatDeckChannels.DisplayName(channel);
        public string Hex => $"#{owner.ColourOf(channel):X8}";
        public string DefaultHex => $"#{ChatDeckChannelColours.Default(channel):X8}";
        public Action<string> SetHex => v => owner.SetColour(channel, v);
    }

    public const string Markup = """
        <panel x="90" y="80" w="340" h="560" title="Chat Deck settings" theme="plugin" layout="column" padding="10" gap="6">
          <group layout="row" gap="8" align="center" h="22">
            <label text="Font size" w="150" />
            <number value="{FontSize}" min="10" max="20" step="1" onchange="{SetFontSize}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Input font size" w="150" />
            <number value="{InputFontSize}" min="10" max="20" step="1" onchange="{SetInputFontSize}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Line spacing" w="150" />
            <number value="{LineSpacing}" min="1" max="1.6" step="0.1" decimals="1" onchange="{SetLineSpacing}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Background opacity %" w="150" />
            <number value="{Opacity}" min="0" max="100" step="5" onchange="{SetOpacity}" w="70" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Timestamps" w="90" />
            <radio items="Off|HH:MM|HH:MM:SS" selected="{Timestamps}" onchange="{SetTimestamps}" orientation="horizontal" style="segmented" grow="1" h="20" />
          </group>
          <checkbox text="Show channel names ([General])" checked="{ChannelPrefix}" onchange="{SetChannelPrefix}" h="16" />
          <checkbox text="Indent wrapped lines" checked="{WrapIndent}" onchange="{SetWrapIndent}" h="16" />
          <group layout="row" gap="8" align="center" h="22">
            <checkbox text="Fade when idle after (s)" checked="{FadeWhenIdle}" onchange="{SetFadeWhenIdle}" w="190" h="16" />
            <number value="{FadeAfterSeconds}" min="1" max="600" step="1" onchange="{SetFadeAfterSeconds}" w="70" />
          </group>
          <checkbox text="Beep on alert" checked="{Beep}" onchange="{SetBeep}" h="16" />
          <separator h="1" />
          <group layout="row" gap="8" align="center" h="22">
            <label text="Next tab key" w="110" />
            <hotkey value="{NextTabKey}" onchange="{SetNextTabKey}" grow="1" h="20" />
          </group>
          <group layout="row" gap="8" align="center" h="22">
            <label text="Previous tab key" w="110" />
            <hotkey value="{PreviousTabKey}" onchange="{SetPreviousTabKey}" grow="1" h="20" />
          </group>
          <separator h="1" />
          <group layout="row" gap="8" align="center" h="22">
            <label text="Channel colours" grow="1" />
            <button text="Reset all" onclick="{ResetColours}" w="80" h="20" />
          </group>
          <group layout="column" gap="3" scroll="y" grow="1">
            <repeat items="{Colours}" key="{Id}">
              <group layout="row" gap="8" align="center" h="20">
                <label text="{Name}" color="{Hex}" grow="1" />
                <color value="{Hex}" onchange="{SetHex}" w="40" h="16" />
              </group>
            </repeat>
          </group>
          <group layout="row" gap="8" h="24">
            <group grow="1" />
            <button text="Close" onclick="{Close}" w="72" h="24" />
          </group>
        </panel>
        """;
}
