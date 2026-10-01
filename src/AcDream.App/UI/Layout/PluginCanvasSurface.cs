using System.Numerics;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;

namespace AcDream.App.UI.Layout;

/// <summary>
/// What the interface needs from the renderer to paint plugin canvases:
/// the device the off-screen targets come from, the frame the passes go
/// into, where the interface shaders are, and how many framebuffer pixels
/// the window has per point. Public so it can travel in the runtime's
/// bindings; the renderer types themselves stay internal.
/// </summary>
public sealed class PluginCanvasHostServices
{
    internal PluginCanvasHostServices(
        IGpuDevice device,
        ICurrentGpuFrameSource frames,
        string shaderDirectory,
        Func<Vector2>? framebufferPerPoint)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Frames = frames ?? throw new ArgumentNullException(nameof(frames));
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderDirectory);
        ShaderDirectory = shaderDirectory;
        FramebufferPerPoint = framebufferPerPoint ?? (static () => Vector2.One);
    }

    /// <summary>
    /// The device the targets come from and go back to. It waits for the
    /// frames in flight itself when a target is given back; nothing here
    /// keeps a retirement ledger of its own.
    /// </summary>
    internal IGpuDevice Device { get; }

    internal ICurrentGpuFrameSource Frames { get; }

    internal string ShaderDirectory { get; }

    /// <summary>
    /// Framebuffer pixels per window point on each axis, read every frame:
    /// 2 on a typical high-density display, and it changes when the window
    /// moves to a display of another density. One without a window.
    /// </summary>
    internal Func<Vector2> FramebufferPerPoint { get; }
}

/// <summary>
/// The one drawing surface every plugin canvas repaints through. The main
/// interface renderer cannot be borrowed for this: a repaint happens while
/// the interface is still collecting its own frame, and would land in the
/// middle of it. So the surface keeps a renderer and a context of its own,
/// begun at the canvas's size for each repaint and flushed into the
/// canvas's off-screen target in a pass of its own, before the interface's
/// pass opens.
///
/// <para>Repaints run one at a time on the interface thread, so one painter
/// serves them all, bound for the duration of each plugin callback and
/// unbound after.</para>
/// </summary>
internal sealed class PluginCanvasSurface : IDisposable
{
    internal const string PassName = "plugin-canvas";

    private readonly PluginCanvasHostServices _services;
    private readonly TextRenderer _renderer;
    private readonly UiRenderContext _context;
    private readonly PluginPainter _painter = new();
    private readonly Func<bool> _overShapeBudget;
    private bool _disposed;

    internal PluginCanvasSurface(PluginCanvasHostServices services, UiDatFont? font)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Font = font;
        // The target is cleared to transparent and holds premultiplied colour
        // once painted; straight alpha on both channels would store alpha
        // squared and every translucent pixel would show too faint.
        _renderer = new TextRenderer(
            services.Device,
            services.Frames,
            services.ShaderDirectory,
            GpuBlendMode.StraightAlphaIntoPremultiplied);
        // No linear twins, unlike the fixed canvas: a canvas painted above
        // one pixel per canvas pixel is not magnified afterwards (except
        // behind a pre-game screen, where nobody sees it), so the interface
        // font keeps its nearest-sampled, whole-pixel look.
        _renderer.LinearTwinResolver = null;
        _context = new UiRenderContext(_renderer, Vector2.Zero);
        _overShapeBudget = () => _painter.ShapeBudgetExceeded;
    }

    internal PluginCanvasHostServices Services => _services;

    internal UiDatFont? Font { get; }

    /// <summary>The renderer the repaints collect into; tests read its runs.</summary>
    internal TextRenderer Renderer => _renderer;

    /// <summary>The frame slot in flight, or null between frames, when nothing can be painted.</summary>
    internal int? CurrentFrameSlot => _services.Frames.CurrentFrame?.SlotIndex;

    /// <summary>
    /// Runs one plugin paint callback into a target under the guard. Returns
    /// what the guard returned: true when the callback drew and left the
    /// clip stack as it found it. The target is cleared and drawn either
    /// way, so a callback that threw halfway leaves a blank canvas rather
    /// than half of one over the last. A paint that asked for more shape
    /// vertices than one paint may counts against the guard as an overrun.
    ///
    /// <para>At a <paramref name="pixelScale"/> above 1 the target is that
    /// many times the canvas's size. The context, the clip and the painter
    /// stay in canvas pixels, as the interface does on the fixed canvas:
    /// only the renderer multiplies, as the vertices go out.</para>
    /// </summary>
    internal bool Repaint(
        PluginCanvasRegistration registration,
        IGpuRenderTarget target,
        UiDrawCallbackGuard guard,
        PluginImages? images,
        PluginFonts? fonts = null,
        Action<CanvasShapeProblem, string>? shapeProblems = null,
        float pixelScale = 1f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(guard);
        int width = registration.Width;
        int height = registration.Height;
        var size = new Vector2(width, height);

        _renderer.Begin(new Vector2(
            CanvasPixelScale.DeviceSize(width, pixelScale), CanvasPixelScale.DeviceSize(height, pixelScale)));
        _renderer.CanvasScale = new Vector2(pixelScale);
        _context.Begin(size, null);
        _context.PushClip(0f, 0f, width, height);
        _painter.Bind(_context, Font, images, width, height, fonts, shapeProblems, pixelScale);
        bool drew;
        try
        {
            Action<Plugin.Abstractions.IPluginPainter>? paint = registration.Paint;
            drew = paint is not null && guard.Invoke(_context, _ => paint(_painter), _overShapeBudget);
        }
        finally
        {
            _painter.Unbind();
            _context.PopClip();
            _renderer.CanvasScale = Vector2.One;
        }
        _renderer.FlushTo(target, Vector4.Zero, null, PassName);
        return drew;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
    }
}
