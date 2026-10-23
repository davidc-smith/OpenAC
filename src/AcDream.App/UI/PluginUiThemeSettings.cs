using System.Numerics;
using AcDream.Plugin.Abstractions;
using AcDream.UI.Abstractions.Panels.Settings;

namespace AcDream.App.UI;

public enum PluginUiTheme { Classic, Moss, Brass }

/// <summary>One appearance preference shared by opted-in plugin windows.</summary>
public sealed class PluginUiThemeSettings
{
    private readonly SettingsStore? _store;
    private PluginUiTheme _theme;
    public UiDatFont? ModernFont { get; }
    public PluginUiThemeSettings(SettingsStore? store = null, UiDatFont? modernFont = null)
    {
        _store = store;
        ModernFont = modernFont;
        _theme = Enum.TryParse<PluginUiTheme>(store?.LoadPluginUiTheme(), out var value)
            && Enum.IsDefined(value) ? value : PluginUiTheme.Classic;
    }
    public PluginUiTheme Theme
    {
        get => _theme;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_theme == value) return;
            _store?.SavePluginUiTheme(value.ToString());
            _theme = value;
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
    public PluginUiPalette? Palette => Theme switch
    {
        PluginUiTheme.Moss => PluginUiPalette.Moss,
        PluginUiTheme.Brass => PluginUiPalette.Brass,
        _ => null,
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
    internal void DrawCheck(UiRenderContext ctx, float x, float y, bool value)
    {
        ctx.DrawFill(x, y, 11, 11, value ? Selected : Field);
        ctx.DrawRectOutline(x, y, 11, 11, value ? Accent : Border, 1);
        if (value) ctx.DrawFill(x + 3, y + 3, 5, 5, Accent);
    }
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
    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_settings.Palette is not { } p) { base.OnDraw(ctx); return; }
        ctx.DrawFill(0, 0, Width, Height, p.Background);
    }
    protected override void OnDrawAfterChildren(UiRenderContext ctx)
    {
        if (_settings.Palette is not { } p) { base.OnDrawAfterChildren(ctx); return; }
        ctx.DrawRectOutline(0, 0, Width, Height, p.Border, 1);
        if (Resizable && DrawResizeAffordances)
            ctx.DrawFill(Width - 7, Height - 3, 5, 1, p.Muted);
    }
}
