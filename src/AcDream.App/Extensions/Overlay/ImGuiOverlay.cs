using System.Diagnostics;
using System.Numerics;
using AcDream.App.Rendering;
using AcDream.ClientExtensions;
using ImGuiNET;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// The client's Dear ImGui overlay. Created with the window (before anything
/// GPU exists) so extensions can hold it from Attach; Initialize gives it a
/// renderer and the window size once the GPU is up. Until then, and if
/// starting fails, it stays inert. It is the dev-tools frame hook: BeginFrame
/// (before the world pass) starts the ImGui frame, Render (after the retained
/// UI) runs the callbacks and draws, AbortFrame closes a frame that failed.
/// </summary>
internal sealed class ImGuiOverlay : IClientOverlay, IDevToolsFrameLifecycle, IDisposable
{
    private readonly Action<string> _log;
    private readonly List<Action> _callbacks = new();
    private readonly OverlayIcons _icons;
    private readonly OverlayFonts _fonts;
    private readonly Stopwatch _summaryClock = Stopwatch.StartNew();
    private const int MaxFrameSummaries = 5;
    private int _summariesLogged;
    private IImGuiDrawRenderer? _renderer;
    private Func<(Vector2 Points, Vector2 Pixels)>? _windowSize;
    private IntPtr _context;
    private bool _frameOpen;
    private bool _failed;
    private readonly List<IOverlayKeyboardSink> _sinks = new();
    private bool _imguiWantsKeyboard;

    public ImGuiOverlay(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _icons = new OverlayIcons(_log);
        _fonts = new OverlayFonts(_log);
    }

    /// <summary>Input arrives here from the window's devices.</summary>
    public ImGuiInputQueue Input { get; } = new();

    public bool WantsMouse { get; private set; }

    public bool WantsKeyboard => _imguiWantsKeyboard || AnySinkCapturing();

    /// <summary>Where the clipboard lives: the window's keyboard, once the input feed is up.</summary>
    internal IClipboardSource? ClipboardSource { get; set; }

    public string ClipboardText
    {
        get => ClipboardSource?.ClipboardText ?? string.Empty;
        set
        {
            if (ClipboardSource is { } source)
                source.ClipboardText = value ?? string.Empty;
        }
    }

    public IDisposable AddKeyboardSink(IOverlayKeyboardSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sinks.Add(sink);
        return new SinkRegistration(this, sink);
    }

    internal void SinkKeyDown(Key key, bool ctrl, bool shift)
    {
        OverlayKey mapped = OverlayKeyMap.Map(key);
        OverlayModifiers modifiers = (ctrl ? OverlayModifiers.Control : OverlayModifiers.None)
            | (shift ? OverlayModifiers.Shift : OverlayModifiers.None);
        ForEachSink(sink => sink.KeyDown(mapped, modifiers));
    }

    internal void SinkKeyUp(Key key)
    {
        OverlayKey mapped = OverlayKeyMap.Map(key);
        ForEachSink(sink => sink.KeyUp(mapped));
    }

    internal void SinkCharacter(char value) => ForEachSink(sink => sink.Character(value));

    private void ForEachSink(Action<IOverlayKeyboardSink> call)
    {
        foreach (IOverlayKeyboardSink sink in _sinks.ToArray())
        {
            try { call(sink); }
            catch (Exception error) { DropSink(sink, error); }
        }
    }

    private bool AnySinkCapturing()
    {
        // A failed overlay draws nothing: an invisible input must not hold the keyboard.
        if (_failed)
            return false;
        foreach (IOverlayKeyboardSink sink in _sinks.ToArray())
        {
            try
            {
                if (sink.Capturing)
                    return true;
            }
            catch (Exception error) { DropSink(sink, error); }
        }

        return false;
    }

    private void DropSink(IOverlayKeyboardSink sink, Exception error)
    {
        _sinks.Remove(sink);
        _log($"[ImGui] keyboard sink removed: {error.GetType().Name}: {error.Message}");
    }

    /// <summary>The client's icons for extensions; zero until <see cref="BindIcons"/>.</summary>
    public IClientOverlayIcons Icons => _icons;

    /// <summary>Fonts merged into the atlas (the renderer reads them).</summary>
    internal OverlayFonts Fonts => _fonts;

    public void AddMergedFont(byte[] ttf, string glyphs, float scale) => _fonts.Add(ttf, glyphs, scale);

    public IntPtr Font(float points) => _fonts.Sized(points);

    /// <summary>Points the icons at the client interface's icon services, once it exists.</summary>
    internal void BindIcons(AcDream.App.Plugins.IPluginImageBackend backend) => _icons.Bind(backend);

    /// <summary>True once Initialize succeeded.</summary>
    public bool IsAvailable => _renderer is not null && !_failed;

    public IDisposable AddFrameCallback(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        _callbacks.Add(draw);
        return new CallbackRegistration(this, draw);
    }

    /// <summary>
    /// Starts the overlay. <paramref name="windowSize"/> gives the window size
    /// in points and the framebuffer size in pixels, read every frame.
    /// </summary>
    public void Initialize(IImGuiDrawRenderer renderer, Func<(Vector2 Points, Vector2 Pixels)> windowSize)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(windowSize);
        if (_context != IntPtr.Zero || _renderer is not null)
            throw new InvalidOperationException("ImGuiOverlay is already initialized.");
        try
        {
            _context = ImGui.CreateContext();
            ImGui.SetCurrentContext(_context);
            ImGuiIOPtr io = ImGui.GetIO();
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
            // No keyboard navigation (like RynthCore): arrows, Space and Enter
            // stay with the game; only a focused text field takes the keyboard.
            io.ConfigErrorRecovery = true;
            io.ConfigErrorRecoveryEnableAssert = false;
            unsafe { io.NativePtr->IniFilename = null; }
            _renderer = renderer;
            _windowSize = windowSize;
            Input.Accepting = true;
            _log($"[ImGui] overlay started (ImGui {ImGui.GetVersion()})");
        }
        catch (Exception error)
        {
            _failed = true;
            _log($"[ImGui] overlay unavailable: {error.GetType().Name}: {error.Message}");
        }
    }

    public void BeginFrame(float deltaSeconds)
    {
        if (!IsAvailable)
        {
            Input.Clear();
            return;
        }

        if (_frameOpen)
            return;
        try
        {
            ImGui.SetCurrentContext(_context);
            ImGuiIOPtr io = ImGui.GetIO();
            (Vector2 points, Vector2 pixels) = _windowSize!();
            if (points.X <= 0 || points.Y <= 0)
            {
                Input.Clear();
                WantsMouse = false;
                _imguiWantsKeyboard = false;
                return;
            }

            Vector2 scale = pixels / points;
            io.DisplaySize = points;
            io.DisplayFramebufferScale = scale;
            io.DeltaTime = MathF.Max(deltaSeconds, 1e-4f);
            _renderer!.PrepareFrame(io, scale);
            Input.ApplyTo(io);
            ImGui.NewFrame();
            _frameOpen = true;
            WantsMouse = io.WantCaptureMouse;
            _imguiWantsKeyboard = io.WantCaptureKeyboard;
        }
        catch (Exception error)
        {
            Fail("begin frame", error);
        }
    }

    public void Render(double deltaSeconds, int viewportWidth, int viewportHeight)
    {
        if (!_frameOpen)
            return;
        ImGui.SetCurrentContext(_context);
        foreach (Action callback in _callbacks.ToArray())
        {
            try
            {
                callback();
            }
            catch (Exception error)
            {
                _callbacks.Remove(callback);
                _log($"[ImGui] frame callback removed: {error.GetType().Name}: {error.Message}");
            }
        }

        try
        {
            ImGui.SetCurrentContext(_context);
            ImGui.Render();
            _frameOpen = false;
            ImDrawDataPtr drawData = ImGui.GetDrawData();
            (Vector2 _, Vector2 pixels) = _windowSize!();
            _renderer!.Render(drawData, pixels);
            if (drawData.TotalVtxCount > 0 && _summariesLogged < MaxFrameSummaries && _summaryClock.Elapsed.TotalSeconds >= 1)
            {
                _summaryClock.Restart();
                _summariesLogged++;
                _log($"[ImGui] frame: lists={drawData.CmdListsCount} vtx={drawData.TotalVtxCount}");
            }
        }
        catch (Exception error)
        {
            _frameOpen = false;
            Fail("render", error);
        }
    }

    public void AbortFrame()
    {
        if (!_frameOpen)
            return;
        _frameOpen = false;
        try
        {
            ImGui.SetCurrentContext(_context);
            ImGui.EndFrame();
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        Input.Accepting = false;
        Input.Clear();
        WantsMouse = false;
        _imguiWantsKeyboard = false;
        _sinks.Clear();
        ClipboardSource = null;
        AbortFrame();
        _callbacks.Clear();
        try
        {
            _renderer?.Dispose();
        }
        finally
        {
            _renderer = null;
            if (_context != IntPtr.Zero)
            {
                ImGui.DestroyContext(_context);
                _context = IntPtr.Zero;
            }

            // Last: the atlas reads the font copies until the context is gone.
            _fonts.Dispose();
        }
    }

    /// <summary>
    /// Starts the overlay on the client's GPU, window and input. Returns what
    /// must be released before the GPU device goes away; null if it couldn't start.
    /// </summary>
    public IDisposable? Start(
        AcDream.App.Rendering.Gpu.IGpuDevice device,
        AcDream.App.Rendering.ICurrentGpuFrameSource frames,
        Silk.NET.Windowing.IWindow window,
        Silk.NET.Input.IInputContext input)
    {
        if (IsAvailable || _context != IntPtr.Zero || _renderer is not null)
            return null;
        ImGuiVulkanRenderer? renderer = null;
        try
        {
            renderer = new ImGuiVulkanRenderer(device, frames, _log, _fonts, _icons.IsIssued);
            Initialize(renderer, () => (
                new Vector2(window.Size.X, window.Size.Y),
                new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y)));
            if (!IsAvailable)
            {
                ReleaseUnstarted(renderer);
                return null;
            }

            var feed = new ImGuiInputFeed(input, Input, window, this);
            OverlayInputGate.Bind(this);
            return new Started(this, feed);
        }
        catch (Exception error)
        {
            _failed = true;
            _log($"[ImGui] overlay unavailable: {error.GetType().Name}: {error.Message}");
            if (renderer is not null)
                ReleaseUnstarted(renderer);
            return null;
        }
    }

    // A start that didn't finish still owns GPU resources: release them now,
    // while the device is alive, and leave the overlay inert.
    private void ReleaseUnstarted(ImGuiVulkanRenderer renderer)
    {
        bool ownedByOverlay = ReferenceEquals(_renderer, renderer);
        try
        {
            Dispose();
        }
        catch (Exception error)
        {
            _log($"[ImGui] overlay release failed: {error.GetType().Name}: {error.Message}");
        }

        if (!ownedByOverlay)
        {
            try { renderer.Dispose(); }
            catch (Exception error)
            {
                _log($"[ImGui] renderer release failed: {error.GetType().Name}: {error.Message}");
            }
        }
    }

    private void Fail(string stage, Exception error)
    {
        _failed = true;
        Input.Accepting = false;
        Input.Clear();
        WantsMouse = false;
        _imguiWantsKeyboard = false;
        _log($"[ImGui] overlay stopped at {stage}: {error.GetType().Name}: {error.Message}");
    }

    private sealed class Started(ImGuiOverlay overlay, ImGuiInputFeed feed) : IDisposable
    {
        public void Dispose()
        {
            OverlayInputGate.Bind(null);
            try { feed.Dispose(); }
            finally { overlay.Dispose(); }
        }
    }

    private sealed class SinkRegistration(ImGuiOverlay owner, IOverlayKeyboardSink sink) : IDisposable
    {
        public void Dispose() => owner._sinks.Remove(sink);
    }

    private sealed class CallbackRegistration(ImGuiOverlay owner, Action draw) : IDisposable
    {
        public void Dispose() => owner._callbacks.Remove(draw);
    }
}
