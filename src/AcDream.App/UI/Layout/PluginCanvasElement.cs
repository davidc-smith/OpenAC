using System.Collections.Generic;
using System.Numerics;
using System.Text;
using AcDream.App.Input;
using AcDream.App.Plugins;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.Plugin.Abstractions;

namespace AcDream.App.UI.Layout;

/// <summary>
/// One plugin canvas in the interface tree: a rectangle on the shared
/// overlay layer that shows an off-screen texture the plugin painted.
///
/// <para>The texture is retained. Each frame the element asks the
/// registration whether the plugin invalidated; if so, and a frame is
/// open, it paints once through the shared surface into a target of its
/// own and shows that. Otherwise it shows what it showed last. A canvas
/// that is never invalidated costs one quad a frame.</para>
///
/// <para>Targets are pooled the way the creature viewport's are: a target
/// is written only when no frame still in flight can be reading it, which
/// with the frame flight's guarantee means one last used in the slot the
/// current frame occupies. The target being shown is never the one being
/// painted, so a repaint never tears. The pool grows to the flight depth
/// plus one and no further.</para>
///
/// <para>Targets are painted at the interface's pixel scale
/// (<see cref="CanvasPixelScale"/>), so on a high-density display each texel
/// lands on one device pixel instead of being magnified. The element
/// reads the scale every frame it draws; when it changes, every target is
/// given back, the canvas is invalidated, and the next paint allocates at
/// the new size.</para>
///
/// <para>A canvas that opted in to pointer input answers the hit-test for
/// its own rectangle while it has a handler, and for nothing outside it.
/// The root gives it the pointer on a press, as it does any element that
/// owns its interior drag, so the moves and the release reach it wherever
/// the pointer goes; the plugin sees every event in the canvas's own
/// pixels. The handler runs under a guard of its own: one that throws or
/// stays slow is dropped, and the canvas goes back to click-through while
/// its painting carries on.</para>
///
/// <para>A canvas that opted in to keyboard input takes the interface's
/// keyboard focus, like a text field, while it is shown and has a key
/// handler: from a left press on it, or when the plugin asks and nothing
/// else has focus. Focus is the root's; the canvas only reports what the
/// root tells it, so exactly one focus lost follows each focus gained,
/// whichever way focus goes. While focused it hands the plugin keys,
/// typed text and its own key repeat, under a third guard.</para>
/// </summary>
internal sealed class PluginCanvasElement : UiElement, IPluginCanvasKeyboardFocus
{
    /// <summary>
    /// How long one repaint may take. Twice the frame guard's budget: a
    /// repaint is a whole map or HUD, not a strip of an existing frame, and
    /// it happens when the plugin asks rather than every frame.
    /// </summary>
    internal const double RepaintBudgetMilliseconds = 4.0;

    /// <summary>The most targets a canvas may hold; the flight depth plus one is the real bound.</summary>
    internal const int MaximumTargets = 8;

    /// <summary>How long a key is held before the host repeats it, as a text field does.</summary>
    internal const double KeyRepeatDelaySeconds = 0.40;

    /// <summary>How often a held key repeats after the delay, as a text field does.</summary>
    internal const double KeyRepeatIntervalSeconds = 0.04;

    private sealed class CanvasTarget(IGpuRenderTarget target, GpuTextureSlot slot)
    {
        internal IGpuRenderTarget Target { get; } = target;
        internal GpuTextureSlot Slot { get; } = slot;
        internal uint Handle => UiTextureTableHandle.FromSlot(Slot);
        internal int LastUsedFrameSlot { get; set; } = -1;
        internal bool HasContent { get; set; }
    }

    private readonly PluginCanvasRegistration _registration;
    private readonly PluginCanvasSurface _surface;
    private readonly Func<PluginImages?> _images;
    private readonly Func<PluginFonts?> _fonts;
    private readonly UiDrawCallbackGuard _guard;
    private readonly UiDrawCallbackGuard _inputGuard;
    private readonly UiDrawCallbackGuard _keyGuard;
    private readonly Func<PluginKeyModifiers> _modifiers;
    private readonly Func<bool> _keyboardCaptured;
    private readonly Action<string> _report;
    private readonly List<CanvasTarget> _targets = [];
    private CanvasTarget? _shown;
    private bool _targetsUnavailable;

    /// <summary>The canvas size the targets were made for.</summary>
    private int _targetsWidth;
    private int _targetsHeight;
    private bool _released;
    private PluginPointerButton _heldButton;
    private readonly Action? _pointerRelease;
    private readonly Action<CanvasShapeProblem, string> _shapeProblems;
    private bool _reportedBadShape;
    private bool _reportedShapeBudget;
    private float _pixelScale = 1f;
    private float _interfaceScale = 1f;
    private bool _focused;
    private PluginKey _repeatKey;
    private double _repeatTimer;

    internal PluginCanvasElement(
        PluginCanvasRegistration registration,
        PluginCanvasSurface surface,
        Func<PluginImages?> images,
        Action<string>? report = null,
        Func<double>? nowMilliseconds = null,
        Func<PluginKeyModifiers>? modifiers = null,
        Func<PluginFonts?>? fonts = null,
        Func<bool>? keyboardCaptured = null)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _fonts = fonts ?? (static () => null);
        _report = report ?? (line => Serilog.Log.Warning("{Line}", line));
        _modifiers = modifiers ?? (static () => PluginKeyModifiers.None);
        _shapeProblems = ReportShapeProblem;
        _keyboardCaptured = keyboardCaptured ?? (static () => false);
        _guard = new UiDrawCallbackGuard(
            $"plugin canvas {registration.Owner.Id}/{registration.CanvasId}",
            _report,
            nowMilliseconds,
            RepaintBudgetMilliseconds);
        _inputGuard = new UiDrawCallbackGuard(
            $"plugin canvas {registration.Owner.Id}/{registration.CanvasId} pointer handler",
            _report,
            nowMilliseconds,
            UiDrawCallbackGuard.FrameBudgetMilliseconds,
            callUnit: "events");
        _keyGuard = new UiDrawCallbackGuard(
            $"plugin canvas {registration.Owner.Id}/{registration.CanvasId} key handler",
            _report,
            nowMilliseconds,
            UiDrawCallbackGuard.FrameBudgetMilliseconds,
            callUnit: "events");
        Name = $"PluginCanvas:{registration.Owner.Id}:{registration.CanvasId}";
        // Click-through is the element's own state; the layer it is added
        // to leaves it alone for a canvas that takes input.
        ClickThrough = !registration.AcceptsPointerInput;
        // No drag-and-drop from a press on the canvas: the press is the
        // plugin's, held until the button comes up, wherever the pointer went.
        CapturesPointerDrag = registration.AcceptsPointerInput;
        Anchors = AnchorEdges.None;
        (_targetsWidth, _targetsHeight) = registration.Size;
        Width = _targetsWidth;
        Height = _targetsHeight;
        // Read by the tree before the visibility check, so a canvas the
        // plugin hid and shows again is ticked and drawn on the next frame.
        VisibleSource = () => _registration.IsVisible && !_registration.IsDropped && !_released;
        Visible = VisibleSource();
        if (registration.AcceptsPointerInput)
        {
            _pointerRelease = ReleasePointer;
            registration.PointerRelease = _pointerRelease;
        }
        if (registration.AcceptsKeyboardInput)
        {
            // Typed text reaches only an edit control, and the root sends
            // keys nowhere else while one has focus.
            IsEditControl = true;
            registration.KeyboardFocus = this;
        }
    }

    internal PluginCanvasRegistration Registration => _registration;

    internal UiDrawCallbackGuard Guard => _guard;

    internal int TargetCount => _targets.Count;

    /// <summary>The pixel scale the targets are painted at.</summary>
    internal float PixelScale => _pixelScale;

    /// <summary>The interface texture currently shown, or 0 before the first paint.</summary>
    internal uint ShownTextureHandle => _shown is { HasContent: true } shown ? shown.Handle : 0u;

    /// <summary>True once the pointer handler was dropped; the canvas is click-through from then on.</summary>
    internal bool IsInputDropped => _inputGuard.IsTripped;

    /// <summary>True once the key handler was dropped; the canvas cannot take focus from then on.</summary>
    internal bool IsKeyInputDropped => _keyGuard.IsTripped;

    /// <summary>True while the root's keyboard focus is this canvas.</summary>
    public bool HasFocus => _focused;

    /// <summary>
    /// Whether the canvas may hold keyboard focus right now: it opted in,
    /// is shown, someone is listening, and neither the listener nor the
    /// paint callback has been dropped. Read by the root on a left press.
    /// </summary>
    public override bool AcceptsFocus
    {
        get => TakesKeyboard;
        // Computed from the canvas's own state; nothing sets it from outside.
        set { }
    }

    private bool TakesKeyboard =>
        _registration.AcceptsKeyboardInput
        && !_keyGuard.IsTripped
        && _registration.KeyHandler is not null
        && VisibleSource!();

    /// <summary>
    /// Whether the canvas answers for its rectangle right now: it opted in,
    /// someone is listening, and the listener has not been dropped.
    /// </summary>
    private bool TakesInput =>
        _registration.AcceptsPointerInput && !_inputGuard.IsTripped && _registration.PointerHandler is not null;

    protected override bool ClipsChildren => true;

    protected override void OnTick(double deltaSeconds)
    {
        Layout();
        if (!_focused || !KeepsFocus()) return;
        if (_repeatKey == PluginKey.Unknown) return;
        _repeatTimer -= deltaSeconds;
        if (_repeatTimer > 0) return;
        _repeatTimer = KeyRepeatIntervalSeconds;
        KeyDown(_repeatKey, isRepeat: true);
    }

    // While something is being dragged no canvas answers, so the drop
    // reaches the window or the world beneath.
    protected override bool OnHitTest(float localX, float localY) =>
        TakesInput && FindRoot()?.DragSource is null && base.OnHitTest(localX, localY);

    public override bool OnEvent(in UiEvent e)
    {
        if (!ReferenceEquals(e.Target, this))
            return false;

        switch (e.Type)
        {
            case UiEventType.MouseDown:
                return Press(PluginPointerButton.Left, e.Data1, e.Data2);
            case UiEventType.RightDown:
                return Press(PluginPointerButton.Right, e.Data1, e.Data2);
            case UiEventType.MiddleDown:
                return Press(PluginPointerButton.Middle, e.Data1, e.Data2);

            case UiEventType.MouseMove:
                if (_heldButton == PluginPointerButton.None) return false;
                Deliver(PluginPointerEventKind.Move, e.Data1, e.Data2, _heldButton);
                return true;

            case UiEventType.MouseUp:
                return Release(PluginPointerButton.Left, e.Data1, e.Data2);
            case UiEventType.RightUp:
                return Release(PluginPointerButton.Right, e.Data1, e.Data2);
            case UiEventType.MiddleUp:
                return Release(PluginPointerButton.Middle, e.Data1, e.Data2);

            case UiEventType.Scroll:
            {
                // The root's scroll coordinates belong to the top-level child
                // it hit, not to this element; the pointer's own position does.
                if (FindRoot() is not { } root || !TakesInput) return false;
                Vector2 screen = ScreenPosition;
                Deliver(
                    PluginPointerEventKind.Wheel,
                    root.MouseX - (int)screen.X,
                    root.MouseY - (int)screen.Y,
                    PluginPointerButton.None,
                    e.Data0);
                return true;
            }

            case UiEventType.CaptureChanged:
            {
                // The root took the pointer back with a button still held:
                // the canvas was hidden or removed, or something else
                // captured. The plugin's release clears the button first,
                // so it never sees a cancel it asked for.
                if (_heldButton == PluginPointerButton.None) return false;
                PluginPointerButton held = _heldButton;
                _heldButton = PluginPointerButton.None;
                if (FindRoot() is { } root)
                {
                    Vector2 screen = ScreenPosition;
                    Deliver(PluginPointerEventKind.Cancelled, root.MouseX - (int)screen.X, root.MouseY - (int)screen.Y, held);
                }
                return true;
            }

            case UiEventType.FocusGained:
                _focused = true;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.FocusGained, PluginKey.Unknown, _modifiers()));
                return true;

            case UiEventType.FocusLost:
                if (!_focused) return true;
                _focused = false;
                _repeatKey = PluginKey.Unknown;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.FocusLost, PluginKey.Unknown, _modifiers()));
                return true;

            case UiEventType.KeyDown:
            {
                if (!_focused) return false;
                if (!KeepsFocus()) return true;
                PluginKey key = PluginKeyMap.FromSilk((Silk.NET.Input.Key)e.Data0);
                // A key the contract cannot name is still the canvas's:
                // nothing behind a focused canvas acts on keys.
                if (key == PluginKey.Unknown) return true;
                _repeatKey = key;
                _repeatTimer = KeyRepeatDelaySeconds;
                KeyDown(key, isRepeat: false);
                return true;
            }

            case UiEventType.KeyUp:
            {
                if (!_focused) return false;
                if (!KeepsFocus()) return true;
                PluginKey key = PluginKeyMap.FromSilk((Silk.NET.Input.Key)e.Data0);
                if (key == PluginKey.Unknown) return true;
                if (key == _repeatKey) _repeatKey = PluginKey.Unknown;
                DeliverKey(new PluginKeyEvent(PluginKeyEventKind.Up, key, _modifiers()));
                return true;
            }

            case UiEventType.Char:
            {
                if (!_focused) return false;
                if (!KeepsFocus()) return true;
                // Silk hands over UTF-16 units; a lone surrogate or a control
                // character is not text a plugin can draw.
                if (!Rune.IsValid(e.Data0) || Rune.IsControl(new Rune(e.Data0))) return true;
                DeliverKey(new PluginKeyEvent(
                    PluginKeyEventKind.Text, PluginKey.Unknown, _modifiers(), Text: new Rune(e.Data0).ToString()));
                return true;
            }

            // Click, double-click and right-click are the root's reading of a
            // press and release the plugin already saw; nothing beneath the
            // canvas should act on them either.
            case UiEventType.Click:
            case UiEventType.DoubleClick:
            case UiEventType.RightClick:
                return true;
        }
        return false;
    }

    private bool Press(PluginPointerButton button, int localX, int localY)
    {
        if (!TakesInput) return false;
        if (_heldButton == PluginPointerButton.None)
            _heldButton = button;
        Deliver(PluginPointerEventKind.Down, localX, localY, button);
        return true;
    }

    private bool Release(PluginPointerButton button, int localX, int localY)
    {
        if (_heldButton != button) return false;
        _heldButton = PluginPointerButton.None;
        Deliver(PluginPointerEventKind.Up, localX, localY, button);
        return true;
    }

    /// <summary>
    /// Ends the press the canvas holds, on the plugin's request. The button
    /// is forgotten before the root is asked, so the capture change that
    /// follows is not reported back as a cancel.
    /// </summary>
    internal void ReleasePointer()
    {
        if (_heldButton == PluginPointerButton.None) return;
        _heldButton = PluginPointerButton.None;
        if (FindRoot() is { } root && ReferenceEquals(root.Captured, this))
            root.ReleaseCapture();
    }

    private void Deliver(PluginPointerEventKind kind, int localX, int localY, PluginPointerButton button, int wheelDelta = 0)
    {
        Action<PluginPointerEvent>? handler = _registration.PointerHandler;
        if (handler is null || _inputGuard.IsTripped) return;
        var pointer = new PluginPointerEvent(kind, new PluginPoint(localX, localY), button, _modifiers(), wheelDelta);
        _inputGuard.Invoke(() => handler(pointer));
        if (_inputGuard.IsTripped)
        {
            // Nobody is listening any more: let go of the pointer so the
            // press does not hang, and stop answering the hit-test.
            ReleasePointer();
        }
    }

    /// <summary>
    /// Gives focus back when the canvas can no longer hold it: the plugin
    /// hid it or took its handler away, or a modal dialog opened. Checked
    /// every tick and before every key, so a key between a change and the
    /// next tick does not reach the plugin. Removal is the root's to notice.
    /// </summary>
    private bool KeepsFocus()
    {
        if (TakesKeyboard && FindRoot()?.Modal is null) return true;
        ReleaseFocus();
        return false;
    }

    /// <summary>
    /// A key went down or repeated: the plugin sees it, and an Escape it
    /// did not handle gives focus back, as Escape leaves a text field.
    /// </summary>
    private void KeyDown(PluginKey key, bool isRepeat)
    {
        bool handled = DeliverKey(new PluginKeyEvent(PluginKeyEventKind.Down, key, _modifiers(), isRepeat));
        if (key == PluginKey.Escape && !handled)
            ReleaseFocus();
    }

    /// <summary>
    /// Hands one event to the key handler under its guard. Returns the
    /// handler's answer; false when nobody heard it.
    /// </summary>
    private bool DeliverKey(PluginKeyEvent key)
    {
        Func<PluginKeyEvent, bool>? handler = _registration.KeyHandler;
        if (handler is null || _keyGuard.IsTripped) return false;
        bool handled = false;
        _keyGuard.Invoke(() => handled = handler(key));
        if (_keyGuard.IsTripped)
        {
            // Nobody is listening any more: give the keyboard back.
            ReleaseFocus();
            return false;
        }
        return handled;
    }

    /// <summary>
    /// Takes the keyboard on the plugin's request, which never takes it
    /// from anything else: the chat bar, a text field, another canvas or a
    /// dialog keeps it, and so does a key rebind being captured.
    /// </summary>
    public bool RequestFocus()
    {
        if (_focused) return true;
        if (_released || !TakesKeyboard || !IsShownInTree()) return false;
        if (FindRoot() is not { } root) return false;
        if (root.KeyboardFocus is not null || root.Modal is not null || _keyboardCaptured())
            return false;
        root.SetKeyboardFocus(this);
        return _focused;
    }

    /// <summary>
    /// Gives the keyboard back if the canvas still has it; the root's focus
    /// change delivers the one focus lost.
    /// </summary>
    public void ReleaseFocus()
    {
        if (FindRoot() is { } root && ReferenceEquals(root.KeyboardFocus, this))
            root.SetKeyboardFocus(null);
    }

    private bool IsShownInTree()
    {
        for (UiElement? element = this; element is not null; element = element.Parent)
        {
            if (!element.Visible) return false;
        }
        return true;
    }

    /// <summary>
    /// Places the canvas by anchor plus offset inside its layer, which the
    /// overlay host keeps equal to the viewport. Re-run every tick because
    /// the plugin may move the canvas at any time and the viewport can
    /// change size.
    /// </summary>
    internal void Layout()
    {
        (int width, int height) = _registration.Size;
        Width = width;
        Height = height;
        float layerWidth = Parent?.Width ?? 0f;
        float layerHeight = Parent?.Height ?? 0f;
        float x = (float)_registration.Offset.X;
        float y = (float)_registration.Offset.Y;
        switch (_registration.Anchor)
        {
            case PluginCanvasAnchor.TopCenter:
            case PluginCanvasAnchor.Center:
            case PluginCanvasAnchor.BottomCenter:
                x += (layerWidth - Width) * 0.5f;
                break;
            case PluginCanvasAnchor.TopRight:
            case PluginCanvasAnchor.CenterRight:
            case PluginCanvasAnchor.BottomRight:
                x += layerWidth - Width;
                break;
        }
        switch (_registration.Anchor)
        {
            case PluginCanvasAnchor.CenterLeft:
            case PluginCanvasAnchor.Center:
            case PluginCanvasAnchor.CenterRight:
                y += (layerHeight - Height) * 0.5f;
                break;
            case PluginCanvasAnchor.BottomLeft:
            case PluginCanvasAnchor.BottomCenter:
            case PluginCanvasAnchor.BottomRight:
                y += layerHeight - Height;
                break;
        }
        Left = MathF.Floor(x + 0.5f);
        Top = MathF.Floor(y + 0.5f);
    }

    protected override void OnDraw(UiRenderContext ctx)
    {
        if (_released) return;
        int? frameSlot = _surface.CurrentFrameSlot;
        if (frameSlot is { } slot)
        {
            FollowSize();
            FollowPixelScale();
            RepaintIfInvalidated(slot);
            if (_shown is not null)
                _shown.LastUsedFrameSlot = slot;
        }

        if (_shown is not { HasContent: true } shown) return;
        // The target holds premultiplied colour (see PluginCanvasSurface).
        ctx.DrawSpritePremultiplied(shown.Handle, 0f, 0f, Width, Height, 0f, 0f, 1f, 1f, Vector4.One);
    }

    private void RepaintIfInvalidated(int frameSlot)
    {
        if (_guard.IsTripped || _registration.Paint is null) return;
        if (!_registration.TakeInvalidation()) return;

        CanvasTarget? target = AcquireWritable(frameSlot);
        if (target is null)
        {
            // No target can be written this frame; keep the request for the next.
            _registration.Invalidate();
            return;
        }

        // Sharper font bakes are made here, outside the guard: baking takes
        // longer than a paint may. They follow the interface's scale rather
        // than this canvas's, which is lower only when the canvas is too
        // large for the device, so two canvases of one plugin never make
        // its fonts bake back and forth.
        PluginFonts? fonts = _fonts();
        fonts?.PrepareScale(_interfaceScale);
        bool drew = _surface.Repaint(
            _registration, target.Target, _guard, _images(), fonts, _shapeProblems, _pixelScale);
        target.LastUsedFrameSlot = frameSlot;
        if (_guard.IsTripped)
        {
            // The callback was dropped: what it left in the target is not to
            // be trusted, and the registration says why the canvas is gone.
            target.HasContent = false;
            _shown = null;
            _registration.IsDropped = true;
            Visible = false;
            return;
        }
        target.HasContent = drew;
        _shown = target;
    }

    private CanvasTarget? AcquireWritable(int frameSlot)
    {
        foreach (CanvasTarget candidate in _targets)
        {
            if (ReferenceEquals(candidate, _shown)) continue;
            if (candidate.LastUsedFrameSlot == -1 || candidate.LastUsedFrameSlot == frameSlot)
                return candidate;
        }
        if (_targets.Count >= MaximumTargets || _targetsUnavailable)
            return null;

        IGpuDevice device = _surface.Services.Device;
        int width = CanvasPixelScale.DeviceSize(_registration.Width, _pixelScale);
        int height = CanvasPixelScale.DeviceSize(_registration.Height, _pixelScale);
        IGpuRenderTarget target;
        try
        {
            target = device.CreateRenderTarget(new GpuRenderTargetDescription(
                $"plugin-canvas-{_registration.Owner.Id}-{_registration.CanvasId}-{_targets.Count}",
                width,
                height,
                GpuTextureFormat.Rgba8UnormRenderTarget,
                DepthFormat: null,
                SampleCount: 1));
        }
        catch (Exception failure)
        {
            _targetsUnavailable = true;
            _report(
                $"Plugin canvas '{_registration.Owner.Id}/{_registration.CanvasId}' cannot be painted: "
                + $"no off-screen target ({width}x{height}): {failure.Message}.");
            return null;
        }

        try
        {
            IGpuSampler sampler = device.CreateSampler(GpuSamplerDescription.WorldClamp);
            GpuTextureSlot slot = device.RegisterTexture(target.ColorTexture, sampler);
            var created = new CanvasTarget(target, slot);
            _targets.Add(created);
            return created;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Where the painter says a shape went wrong. Each kind is reported once
    /// per canvas: the paint callback runs again every time the plugin
    /// invalidates, and the same bad shape would otherwise fill the log.
    /// </summary>
    private void ReportShapeProblem(CanvasShapeProblem problem, string detail)
    {
        string canvas = $"{_registration.Owner.Id}/{_registration.CanvasId}";
        switch (problem)
        {
            case CanvasShapeProblem.InvalidInput when !_reportedBadShape:
                _reportedBadShape = true;
                _report(
                    $"Plugin canvas '{canvas}': {detail}, so that shape drew nothing. "
                    + "A shape given input it cannot draw draws nothing; this is reported once per canvas.");
                break;
            case CanvasShapeProblem.VertexBudget when !_reportedShapeBudget:
                _reportedShapeBudget = true;
                _report(
                    $"Plugin canvas '{canvas}': {detail}, and the paint counts as over its budget. "
                    + "This is reported once per canvas.");
                break;
        }
    }

    /// <summary>
    /// Gives every target back to the device and stops drawing. Idempotent.
    /// The element is removed from the tree by whoever mounted it, through
    /// the parent, so the root sees the subtree go.
    ///
    /// <para>The device owns the wait for the frames in flight: a slot
    /// release and a texture disposal are both deferred behind them inside
    /// the device, so they are asked for at once here. Deferring the ask
    /// itself would put a call into the device on a ledger the device
    /// drains while it is being torn down, after it has stopped taking
    /// requests; that is how a canvas open at shutdown reached a disposed
    /// device.</para>
    /// </summary>
    internal void ReleaseTargets()
    {
        if (_released) return;
        _released = true;
        if (ReferenceEquals(_registration.PointerRelease, _pointerRelease))
            _registration.PointerRelease = null;
        // Focus needs nothing here: the element left the tree first, and
        // the root took focus back from it on the way out.
        if (ReferenceEquals(_registration.KeyboardFocus, this))
            _registration.KeyboardFocus = null;
        GiveBackTargets();
    }

    /// <summary>
    /// Reads the pixel scale for this frame. A change gives every target
    /// back -- the one shown too, since the repaint that follows in this
    /// same draw replaces it -- and invalidates, so the canvas is painted
    /// again at the new size. A target that could not be made at the old
    /// size may be possible at the new one, so that is tried afresh.
    /// </summary>
    private void FollowPixelScale()
    {
        _interfaceScale = CanvasPixelScale.ForInterface(_surface.Services.FramebufferPerPoint());
        float scale = CanvasPixelScale.ForCanvas(
            _interfaceScale,
            _registration.Width,
            _registration.Height,
            _surface.Services.Device.Capabilities.MaxImageDimension2D);
        if (scale == _pixelScale) return;
        _pixelScale = scale;
        GiveBackTargets();
        _targetsUnavailable = false;
        _registration.Invalidate();
    }

    /// <summary>
    /// Reads the size the plugin last asked for. A change gives every target
    /// back, as a change of pixel scale does, and the repaint the resize
    /// asked for paints at the new size in this same draw.
    /// </summary>
    private void FollowSize()
    {
        (int width, int height) = _registration.Size;
        if (width == _targetsWidth && height == _targetsHeight) return;
        _targetsWidth = width;
        _targetsHeight = height;
        Width = width;
        Height = height;
        GiveBackTargets();
        _targetsUnavailable = false;
        _registration.Invalidate();
    }

    private void GiveBackTargets()
    {
        _shown = null;
        IGpuDevice device = _surface.Services.Device;
        foreach (CanvasTarget target in _targets)
        {
            device.ReleaseTextureSlot(target.Slot);
            target.Target.Dispose();
        }
        _targets.Clear();
    }
}
