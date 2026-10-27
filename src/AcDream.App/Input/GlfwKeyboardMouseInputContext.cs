using System.Runtime.CompilerServices;
using Silk.NET.Input;
using Silk.NET.Windowing;
using WindowHandle = Silk.NET.GLFW.WindowHandle;

namespace AcDream.App.Input;

/// <summary>
/// The window's input context: the keyboard and the mouse, and no game
/// controllers. The library's own context also builds sixteen gamepads and
/// sixteen joysticks, and asking the windowing layer about the first of them
/// makes it enumerate every attached game device, about 90 ms on the main
/// thread before the first frame, for devices the client never reads. The
/// library has no option to leave them out, so this context hosts the
/// library's own keyboard, mouse and window-event objects (not public types,
/// reached through <see cref="UnsafeAccessorAttribute"/>) and wires them the
/// way the library's context does: typing, the pointer, the cursor and the
/// clipboard are the same code as before. A test pins every member used.
/// </summary>
internal sealed unsafe class GlfwKeyboardMouseInputContext : IInputContext
{
    internal const string KeyboardTypeName = "Silk.NET.Input.Glfw.GlfwKeyboard, Silk.NET.Input.Glfw";
    internal const string MouseTypeName = "Silk.NET.Input.Glfw.GlfwMouse, Silk.NET.Input.Glfw";
    internal const string EventsTypeName = "Silk.NET.Input.Glfw.GlfwEvents, Silk.NET.Input.Glfw";
    internal const string ViewTypeName = "Silk.NET.Windowing.Internals.ViewImplementationBase, Silk.NET.Windowing.Common";

    private readonly IView _view;
    private readonly object _events;
    private readonly IKeyboard _keyboard;
    private readonly IMouse _mouse;
    private readonly Action _processEvents;
    private bool _disposed;

    private GlfwKeyboardMouseInputContext(IView view)
    {
        _view = view;
        Handle = view.Handle;
        _keyboard = (IKeyboard)NewKeyboard();
        _mouse = (IMouse)NewMouse();
        Keyboards = [_keyboard];
        Mice = [_mouse];

        // The order the library registers a window in: one event source for
        // the window, then the keyboard, then the mouse.
        _events = NewEvents((WindowHandle*)Handle);
        SubscribeKeyboard(_keyboard, _events);
        SubscribeMouse(_mouse, _events);

        // The library updates its devices after each pass over the window's
        // events: the mouse resets its scroll and times its double clicks.
        _processEvents = ProcessEvents;
        AddProcessEvents(view, _processEvents);
    }

    public nint Handle { get; }

    public IReadOnlyList<IGamepad> Gamepads => [];

    public IReadOnlyList<IJoystick> Joysticks => [];

    public IReadOnlyList<IKeyboard> Keyboards { get; }

    public IReadOnlyList<IMouse> Mice { get; }

    public IReadOnlyList<IInputDevice> OtherDevices => [];

    /// <summary>Raised by game controllers only, so never here.</summary>
    public event Action<IInputDevice, bool>? ConnectionChanged
    {
        add { }
        remove { }
    }

    public static IInputContext Create(IView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!view.IsInitialized)
            throw new InvalidOperationException(
                "Couldn't create input context as the view was not initialized.");
        if (view.Native?.Glfw is null)
            throw new NotSupportedException(
                "The keyboard and mouse input context needs a GLFW window.");
        return new GlfwKeyboardMouseInputContext(view);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        RemoveProcessEvents(_view, _processEvents);
        UnsubscribeKeyboard(_keyboard, _events);
        UnsubscribeMouse(_mouse, _events);
        ((IDisposable)_events).Dispose();
        DisposeMouse(_mouse);
    }

    private void ProcessEvents() => UpdateMouse(_mouse);

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    [return: UnsafeAccessorType(KeyboardTypeName)]
    private static extern object NewKeyboard();

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    [return: UnsafeAccessorType(MouseTypeName)]
    private static extern object NewMouse();

    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    [return: UnsafeAccessorType(EventsTypeName)]
    private static extern object NewEvents(WindowHandle* handle);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Subscribe")]
    private static extern void SubscribeKeyboard(
        [UnsafeAccessorType(KeyboardTypeName)] object keyboard,
        [UnsafeAccessorType(EventsTypeName)] object events);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Unsubscribe")]
    private static extern void UnsubscribeKeyboard(
        [UnsafeAccessorType(KeyboardTypeName)] object keyboard,
        [UnsafeAccessorType(EventsTypeName)] object events);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Subscribe")]
    private static extern void SubscribeMouse(
        [UnsafeAccessorType(MouseTypeName)] object mouse,
        [UnsafeAccessorType(EventsTypeName)] object events);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Unsubscribe")]
    private static extern void UnsubscribeMouse(
        [UnsafeAccessorType(MouseTypeName)] object mouse,
        [UnsafeAccessorType(EventsTypeName)] object events);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Update")]
    private static extern void UpdateMouse([UnsafeAccessorType(MouseTypeName)] object mouse);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Dispose")]
    private static extern void DisposeMouse([UnsafeAccessorType(MouseTypeName)] object mouse);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "add_ProcessEvents")]
    private static extern void AddProcessEvents(
        [UnsafeAccessorType(ViewTypeName)] object view,
        Action handler);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "remove_ProcessEvents")]
    private static extern void RemoveProcessEvents(
        [UnsafeAccessorType(ViewTypeName)] object view,
        Action handler);
}
