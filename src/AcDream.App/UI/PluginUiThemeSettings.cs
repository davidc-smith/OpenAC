using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI;

public enum PluginUiTheme { Classic, Moss, Brass, LavenderDusk, Ember, Bloodmoon, Glacier, Parchment }

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
    private PluginUiPalette? _fixedPalette;

    /// <summary>
    /// Settings that always draw with <paramref name="palette"/> and share this one's fonts;
    /// they never save. For windows that keep their own look whatever the plugin theme.
    /// </summary>
    internal PluginUiThemeSettings WithFixedPalette(PluginUiPalette palette) =>
        new(null, _modernFont, _modernTitleFont) { _fixedPalette = palette };

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
    internal static IReadOnlyList<string> ThemeNames { get; } = ["Classic", "Charcoal + moss", "Warm graphite + brass", "Lavender Dusk", "Ember", "Bloodmoon", "Glacier", "Parchment"];

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
    public PluginUiPalette? Palette => _fixedPalette ?? Theme switch
    {
        PluginUiTheme.Moss => PluginUiPalette.Moss,
        PluginUiTheme.Brass => PluginUiPalette.Brass,
        PluginUiTheme.LavenderDusk => PluginUiPalette.LavenderDusk,
        PluginUiTheme.Ember => PluginUiPalette.Ember,
        PluginUiTheme.Bloodmoon => PluginUiPalette.Bloodmoon,
        PluginUiTheme.Glacier => PluginUiPalette.Glacier,
        PluginUiTheme.Parchment => PluginUiPalette.Parchment,
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
    public static PluginUiPalette LavenderDusk { get; } = new(C(0x17141F), C(0x100E17),
        C(0x352C4A), C(0xEEEAF6), C(0xA69EBA), C(0xBBA0FF), C(0x512F4A));
    public static PluginUiPalette Ember { get; } = new(C(0x151A21), C(0x0E1218),
        C(0x2C3644), C(0xE8ECF1), C(0x9AA6B5), C(0xFF9447), C(0x1E3B44));
    public static PluginUiPalette Bloodmoon { get; } = new(C(0x1C1617), C(0x140F10),
        C(0x43302F), C(0xF2E9E7), C(0xB8A3A0), C(0xFF6B6B), C(0x4D2327));
    public static PluginUiPalette Glacier { get; } = new(C(0x11161C), C(0x0A0E13),
        C(0x26323F), C(0xE4EEF4), C(0x93A4B3), C(0x5ED6E8), C(0x1F3554));

    /// <summary>The one light theme: paper face, ink text, rust accent.</summary>
    public static PluginUiPalette Parchment { get; } = new(C(0xEDE7DC), C(0xFAF7F1),
        C(0xC9BFAE), C(0x2A2520), C(0x655C50), C(0xB4471F), C(0xDDCFB6));

    /// <summary>
    /// The plugin dock's colours under Classic, taken from the Classic dock's old black and gold.
    /// Only the dock uses it: it is not a theme, and Classic windows still draw unthemed.
    /// </summary>
    internal static PluginUiPalette ClassicDock { get; } = new(C(0x000000) with { W = 0.88f }, C(0x060605),
        C(0x9E7A29), C(0xF0EBDD), C(0xA8925C), C(0xDBB852), C(0x17300E));
    /// <summary>Whether the face is pale: a light theme, meant for dark text and art.</summary>
    public bool IsLight => 0.2126f * Background.X + 0.7152f * Background.Y + 0.0722f * Background.Z > 0.5f;

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
    // The window's own theme actions; each repeat copy's sit in its scope,
    // so dropping a copy is one flag, not a search of every action.
    private readonly List<Action<PluginUiPalette?>> _apply = new();
    private readonly List<ThemeActionScope> _scopes = new();
    private int _disposedScopes;
    private PluginUiTheme? _last;
    public UiPluginMarkupPanel(Func<uint, (uint, int, int)> resolve, PluginUiThemeSettings settings)
        : base(resolve) => _settings = settings;
    public UiDatFont? ModernFont => _settings.ModernFont;
    public UiDatFont? ModernTitleFont => _settings.ModernTitleFont;
    public bool HasModernFont => _settings.HasModernFont;

    /// <summary>The palette the panel draws with now, or null under Classic.</summary>
    internal PluginUiPalette? Palette => _settings.Palette;
    private ThemeActionScope? _scope;

    /// <summary>
    /// Starts recording the theme actions added until the scope is disposed;
    /// disposing it removes them, so a repeat copy's look stops following the
    /// theme once the copy is gone.
    /// </summary>
    internal ThemeActionScope BeginThemeScope()
    {
        if (_scope is not null) throw new InvalidOperationException("a theme scope is already open");
        _scope = new ThemeActionScope(this);
        _scopes.Add(_scope);
        return _scope;
    }

    /// <summary>How many theme actions run on a theme change: the window's and every live scope's.</summary>
    internal int ThemeActionCount
    {
        get
        {
            int count = _apply.Count;
            foreach (var scope in _scopes)
                if (!scope.Disposed) count += scope.Actions.Count;
            return count;
        }
    }

    internal sealed class ThemeActionScope(UiPluginMarkupPanel panel) : IDisposable
    {
        internal readonly List<Action<PluginUiPalette?>> Actions = new();
        private bool _closed;

        internal bool Disposed { get; private set; }

        /// <summary>Stops recording; the actions keep running until <see cref="Dispose"/>.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            if (ReferenceEquals(panel._scope, this)) panel._scope = null;
        }

        /// <summary>Stops the actions; the panel drops the scope on its next tick.</summary>
        public void Dispose()
        {
            if (Disposed) return;
            Close();
            Disposed = true;
            panel._disposedScopes++;
        }
    }

    public void AddThemeAction(Action<PluginUiPalette?> action)
    {
        (_scope?.Actions ?? _apply).Add(action);
        action(_settings.Palette);
    }
    protected override void OnTick(double dt)
    {
        base.OnTick(dt);
        if (_disposedScopes > 0)
        {
            _scopes.RemoveAll(static scope => scope.Disposed);
            _disposedScopes = 0;
        }
        if (_last == _settings.Theme) return;
        _last = _settings.Theme;
        foreach (var apply in _apply) apply(_settings.Palette);
        foreach (var scope in _scopes)
        {
            if (scope.Disposed) continue;
            foreach (var apply in scope.Actions) apply(_settings.Palette);
        }
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
