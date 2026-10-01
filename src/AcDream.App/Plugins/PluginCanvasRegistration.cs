using AcDream.Plugin.Abstractions;

namespace AcDream.App.Plugins;

/// <summary>
/// One plugin canvas as the registry holds it: what the plugin asked for,
/// what it has set since, whether it has asked for a repaint, and -- once
/// the interface has mounted it -- how to take it down again. The plugin's
/// handle is this object; the element the interface draws reads it every
/// frame.
///
/// <para>The paint delegate and the pointer and key handlers are dropped
/// on dispose. They are the references from the client into the plugin's
/// code that the interface holds, and a plugin assembly cannot unload
/// while anything still points into it.</para>
/// </summary>
internal sealed class PluginCanvasRegistration : IPluginCanvas
{
    private readonly BufferedUiRegistry _registry;
    private Action<IPluginPainter>? _paint;
    private volatile Action<PluginPointerEvent>? _pointerHandler;
    private volatile Func<PluginKeyEvent, bool>? _keyHandler;
    private volatile IPluginCanvasKeyboardFocus? _keyboardFocus;
    private volatile bool _invalidated = true;
    private volatile int _zOrder;
    private Action? _teardown;
    private Action? _releasePointer;

    internal PluginCanvasRegistration(
        BufferedUiRegistry registry,
        long id,
        PluginUiOwner owner,
        PluginCanvasDescriptor descriptor,
        Action<IPluginPainter> paint)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        ArgumentNullException.ThrowIfNull(descriptor);
        _paint = paint ?? throw new ArgumentNullException(nameof(paint));
        Id = id;
        Owner = owner;
        Descriptor = descriptor;
        IsVisible = descriptor.StartVisible;
        Anchor = descriptor.Anchor;
        Offset = descriptor.Offset;
        _zOrder = descriptor.ZOrder;
    }

    internal long Id { get; }

    internal PluginUiOwner Owner { get; }

    internal PluginCanvasDescriptor Descriptor { get; }

    /// <summary>Handed to the interface once; the interface mounts each registration a single time.</summary>
    internal bool Drained { get; set; }

    /// <summary>True between a successful mount and teardown.</summary>
    internal bool IsMounted => _teardown is not null;

    /// <summary>
    /// Set by the element when its paint callback was dropped: the canvas
    /// stays registered, so the plugin's handle keeps working, but nothing
    /// is drawn for it again.
    /// </summary>
    internal bool IsDropped { get; set; }

    /// <summary>The plugin's paint callback, or null once the canvas is disposed.</summary>
    internal Action<IPluginPainter>? Paint => _paint;

    /// <summary>The layer the plugin chose when it registered the canvas.</summary>
    internal PluginCanvasLayer Layer => Descriptor.Layer;

    /// <summary>Whether the plugin asked for pointer input when it registered the canvas.</summary>
    internal bool AcceptsPointerInput => Descriptor.AcceptsPointerInput;

    /// <summary>Whether the plugin asked for keyboard input when it registered the canvas.</summary>
    internal bool AcceptsKeyboardInput => Descriptor.AcceptsKeyboardInput;

    /// <summary>
    /// The mounted element's answer to the keyboard focus calls; set by
    /// the element when it mounts, cleared by it when it comes down.
    /// </summary>
    internal IPluginCanvasKeyboardFocus? KeyboardFocus
    {
        get => _keyboardFocus;
        set => _keyboardFocus = value;
    }

    /// <summary>
    /// Records how the element ends a press the canvas is holding, so the
    /// plugin's <see cref="ReleasePointer"/> reaches it; cleared by the
    /// element when it comes down.
    /// </summary>
    internal Action? PointerRelease
    {
        get => _releasePointer;
        set => _releasePointer = value;
    }

    public string CanvasId => Descriptor.CanvasId;

    public Action<PluginPointerEvent>? PointerHandler
    {
        get => _pointerHandler;
        set => _pointerHandler = value;
    }

    public void ReleasePointer() => _releasePointer?.Invoke();

    /// <summary>
    /// Read by the canvas's group in the interface every tick, which restacks
    /// the plugin's canvases when it changes.
    /// </summary>
    public int ZOrder
    {
        get => _zOrder;
        set => _zOrder = value;
    }

    public Func<PluginKeyEvent, bool>? KeyHandler
    {
        get => _keyHandler;
        set => _keyHandler = value;
    }

    public bool HasKeyboardFocus => _keyboardFocus?.HasFocus ?? false;

    public bool RequestKeyboardFocus() => _keyboardFocus?.RequestFocus() ?? false;

    public void ReleaseKeyboardFocus() => _keyboardFocus?.ReleaseFocus();

    public int Width => Descriptor.Width;

    public int Height => Descriptor.Height;

    public bool IsAvailable => IsMounted && !IsDropped && _paint is not null;

    public bool IsVisible { get; set; }

    public PluginCanvasAnchor Anchor { get; set; }

    public PluginPoint Offset { get; set; }

    public void Invalidate() => _invalidated = true;

    /// <summary>
    /// Takes the pending repaint request, if any, so several invalidations
    /// before one frame paint once.
    /// </summary>
    internal bool TakeInvalidation()
    {
        if (!_invalidated) return false;
        _invalidated = false;
        return true;
    }

    /// <summary>
    /// Records how the interface takes the canvas down: remove the element
    /// from the tree and give its textures back. Runs at once on dispose.
    /// </summary>
    internal void SetTeardown(Action teardown)
    {
        ArgumentNullException.ThrowIfNull(teardown);
        _teardown = teardown;
    }

    /// <summary>
    /// Runs the teardown once and forgets it. The paint delegate and the
    /// pointer and key handlers stay unless <paramref name="forget"/> is set: an
    /// interface that is going away hands the canvas back to the registry
    /// to be mounted again by the next one, and the plugin's callbacks must
    /// survive that.
    /// </summary>
    internal void Unmount(bool forget)
    {
        Action? teardown = Interlocked.Exchange(ref _teardown, null);
        // The element comes down first: a press it still holds is cancelled,
        // and keyboard focus it still has is lost, to the plugin's handlers
        // on the way, which needs the handlers.
        teardown?.Invoke();
        if (forget)
        {
            _paint = null;
            _pointerHandler = null;
            _keyHandler = null;
        }
    }

    public void Dispose() => _registry.RemoveCanvas(this);
}
