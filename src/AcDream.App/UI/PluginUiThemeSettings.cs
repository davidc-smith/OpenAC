using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI;

public enum PluginUiTheme { Classic, Moss, Brass }

/// <summary>Where the plugin dock sits: floating anywhere, or locked to the left or right screen edge.</summary>
public enum PluginDockMode { Floating, Left, Right }

/// <summary>One appearance preference shared by opted-in plugin windows.</summary>
public sealed class PluginUiThemeSettings
{
    private readonly SettingsStore? _store;
    private PluginUiTheme _theme;
    private PluginDockMode _dock;
    private readonly Lazy<UiDatFont>? _modernFont;
    private readonly Lazy<UiDatFont>? _modernTitleFont;

    /// <summary>The font themed windows set their text in; loaded the first time it is read.</summary>
    public UiDatFont? ModernFont => _modernFont?.Value;

    /// <summary>The heavier weight themed windows set their titles in; null falls back to <see cref="ModernFont"/>.</summary>
    public UiDatFont? ModernTitleFont => _modernTitleFont?.Value;

    /// <summary>Whether there is a <see cref="ModernFont"/>, without loading it.</summary>
    public bool HasModernFont => _modernFont is not null;

    public PluginUiThemeSettings(
        SettingsStore? store = null, Lazy<UiDatFont>? modernFont = null, Lazy<UiDatFont>? modernTitleFont = null)
    {
        _store = store;
        _modernFont = modernFont;
        _modernTitleFont = modernTitleFont;
        PluginUiSettings saved = store?.LoadPluginUi() ?? PluginUiSettings.Default;
        _theme = Enum.TryParse<PluginUiTheme>(saved.Theme, out var value)
            && Enum.IsDefined(value) ? value : PluginUiTheme.Classic;
        _dock = ParseDock(saved.Dock);
    }
    public PluginUiTheme Theme
    {
        get => _theme;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_theme == value) return;
            _theme = value;
            Save();
            ThemeChanged?.Invoke();
        }
    }

    /// <summary>Raised from the <see cref="Theme"/> setter when the player picks another theme.</summary>
    public event Action? ThemeChanged;

    /// <summary>What the Appearance picker calls each theme, in <see cref="PluginUiTheme"/> order.</summary>
    internal static IReadOnlyList<string> ThemeNames { get; } = ["Classic", "Charcoal + moss", "Warm graphite + brass"];

    /// <summary>The selected theme as plugins read it: its kind, name and colours.</summary>
    public PluginUiThemeInfo ThemeInfo => Theme == PluginUiTheme.Classic
        ? PluginUiThemeInfo.Classic
        : new PluginUiThemeInfo((PluginUiThemeKind)Theme, ThemeNames[(int)Theme], Palette?.ToPluginPalette());

    /// <summary>Where the plugin dock sits. Saved with the theme.</summary>
    public PluginDockMode Dock
    {
        get => _dock;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_dock == value) return;
            _dock = value;
            Save();
        }
    }
    public PluginUiPalette? Palette => Theme switch
    {
        PluginUiTheme.Moss => PluginUiPalette.Moss,
        PluginUiTheme.Brass => PluginUiPalette.Brass,
        _ => null,
    };

    /// <summary>The colours the plugin dock draws in: the theme's palette, or the dock's own Classic set.</summary>
    public PluginUiPalette DockPalette => Palette ?? PluginUiPalette.ClassicDock;

    private void Save() => _store?.SavePluginUi(new PluginUiSettings(_theme.ToString(), DockName(_dock)));

    private static string DockName(PluginDockMode mode) => mode switch
    {
        PluginDockMode.Left => "left",
        PluginDockMode.Right => "right",
        _ => "floating",
    };

    private static PluginDockMode ParseDock(string? name) => name?.ToLowerInvariant() switch
    {
        "left" => PluginDockMode.Left,
        "right" => PluginDockMode.Right,
        _ => PluginDockMode.Floating,
    };
}

public sealed record PluginUiPalette(Vector4 Background, Vector4 Field, Vector4 Border,
    Vector4 Text, Vector4 Muted, Vector4 Accent, Vector4 Selected)
{
    private static Vector4 C(uint rgb) => new((rgb >> 16 & 255) / 255f,
        (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, 1f);
    public static PluginUiPalette Moss { get; } = new(C(0x171D1B), C(0x101613),
        C(0x35443B), C(0xE0E8E2), C(0x9AA99E), C(0x97BE81), C(0x344B37));
    public static PluginUiPalette Brass { get; } = new(C(0x221F1B), C(0x181612),
        C(0x4C4335), C(0xE9E2D5), C(0xB2A58E), C(0xC9A665), C(0x51442D));

    /// <summary>
    /// The plugin dock's colours under Classic, taken from the Classic dock's old black and gold.
    /// Only the dock uses it: it is not a theme, and Classic windows still draw unthemed.
    /// </summary>
    internal static PluginUiPalette ClassicDock { get; } = new(C(0x000000) with { W = 0.88f }, C(0x060605),
        C(0x9E7A29), C(0xF0EBDD), C(0xA8925C), C(0xDBB852), C(0x17300E));
    /// <summary>The palette as plugins read it, each channel rounded to a byte.</summary>
    public PluginThemePalette ToPluginPalette() => new(
        ToColor(Background), ToColor(Field), ToColor(Border),
        ToColor(Text), ToColor(Muted), ToColor(Accent), ToColor(Selected));

    private static PluginColor ToColor(Vector4 c) => new(Byte(c.X), Byte(c.Y), Byte(c.Z), Byte(c.W));

    private static byte Byte(float channel) =>
        (byte)MathF.Round(Math.Clamp(channel, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);

    public Vector4 Token(string name) => name switch
    {
        "text" => Text, "muted" => Muted, "field" => Field,
        "border" => Border, "accent" => Accent, "background" => Background,
        _ => throw new FormatException($"Unknown plugin theme color '{name}'."),
    };
    internal void DrawCheck(UiRenderContext ctx, float x, float y, bool value) =>
        PluginUiStyle.Check(ctx, this, x, y, value);
}

internal sealed class UiPluginMarkupPanel : UiNineSlicePanel
{
    private readonly PluginUiThemeSettings _settings;
    private readonly List<Action<PluginUiPalette?>> _apply = new();
    private PluginUiTheme? _last;
    public UiPluginMarkupPanel(Func<uint, (uint, int, int)> resolve, PluginUiThemeSettings settings)
        : base(resolve) => _settings = settings;
    public UiDatFont? ModernFont => _settings.ModernFont;
    public UiDatFont? ModernTitleFont => _settings.ModernTitleFont;
    public bool HasModernFont => _settings.HasModernFont;
    public void AddThemeAction(Action<PluginUiPalette?> action)
    {
        _apply.Add(action);
        action(_settings.Palette);
    }
    protected override void OnTick(double dt)
    {
        base.OnTick(dt);
        if (_last == _settings.Theme) return;
        _last = _settings.Theme;
        foreach (var apply in _apply) apply(_settings.Palette);
    }
    /// <summary>Whether the window's markup gave it a title, so the header band is drawn.</summary>
    internal bool HasTitle { get; set; }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_settings.Palette is not { } p) { base.OnDraw(ctx); return; }
        PluginUiStyle.WindowShadow(ctx, Width, Height);
        ctx.FillRoundedRect(0, 0, Width, Height, PluginUiStyle.WindowRadius, p.Background);
        if (HasTitle) PluginUiStyle.Header(ctx, p, Width);
    }
    protected override void OnDrawAfterChildren(UiRenderContext ctx)
    {
        if (_settings.Palette is not { } p) { base.OnDrawAfterChildren(ctx); return; }
        ctx.StrokeRoundedRect(0.5f, 0.5f, Width - 1f, Height - 1f, PluginUiStyle.WindowRadius - 0.5f, p.Border, 1f);
        if (Resizable && DrawResizeAffordances)
            PluginUiStyle.ResizeGrip(ctx, p, Width, Height);
    }
}
