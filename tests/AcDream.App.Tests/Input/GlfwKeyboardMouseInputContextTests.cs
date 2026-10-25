using System.Reflection;
using AcDream.App.Input;
using WindowHandle = Silk.NET.GLFW.WindowHandle;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace AcDream.App.Tests.Input;

/// <summary>
/// The window's input context holds the keyboard and the mouse only. The
/// library's own context also builds sixteen game controllers and sixteen
/// joysticks, and building them makes the platform enumerate every attached
/// game device at startup (about 90 ms) although the client never reads one.
/// The keyboard and mouse are the library's own objects, so typing, the
/// pointer, the cursor and the clipboard behave exactly as before.
/// </summary>
public sealed class GlfwKeyboardMouseInputContextTests
{
    // The context hosts library types that are not public. A library update
    // that renames or reshapes one fails here, not in a player's first frame.
    [Fact]
    public void TheLibraryMembersTheContextHostsAreThere()
    {
        Type keyboard = Resolve(GlfwKeyboardMouseInputContext.KeyboardTypeName);
        Type mouse = Resolve(GlfwKeyboardMouseInputContext.MouseTypeName);
        Type events = Resolve(GlfwKeyboardMouseInputContext.EventsTypeName);
        Type view = Resolve(GlfwKeyboardMouseInputContext.ViewTypeName);
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        Assert.True(typeof(IKeyboard).IsAssignableFrom(keyboard));
        Assert.True(typeof(IMouse).IsAssignableFrom(mouse));
        Assert.True(typeof(IDisposable).IsAssignableFrom(events));
        Assert.NotNull(keyboard.GetConstructor(Instance, Type.EmptyTypes));
        Assert.NotNull(mouse.GetConstructor(Instance, Type.EmptyTypes));
        Assert.NotNull(events.GetConstructor(Instance, [typeof(WindowHandle).MakePointerType()]));
        foreach (Type device in new[] { keyboard, mouse })
        {
            Assert.NotNull(device.GetMethod("Subscribe", Instance, [events]));
            Assert.NotNull(device.GetMethod("Unsubscribe", Instance, [events]));
        }

        Assert.NotNull(mouse.GetMethod("Update", Instance, Type.EmptyTypes));
        Assert.NotNull(mouse.GetMethod("Dispose", Instance, Type.EmptyTypes));
        Assert.Equal(typeof(Action), view.GetEvent("ProcessEvents", Instance)?.EventHandlerType);
    }

    [Fact]
    [Trait("Lane", "Windows")]
    public void AWindowGetsOneKeyboardOneMouseAndNoGameControllers()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Lane=Windows requires a Windows desktop.");

        using IWindow window = Window.Create(WindowOptions.Default with
        {
            API = GraphicsAPI.None,
            IsVisible = false,
            Size = new(64, 64),
        });
        window.Initialize();
        int handlersBefore = ProcessEventsHandlerCount(window);

        IInputContext input = GlfwKeyboardMouseInputContext.Create(window);
        try
        {
            Assert.IsType<GlfwKeyboardMouseInputContext>(input);
            Assert.Equal(window.Handle, input.Handle);
            Assert.Empty(input.Gamepads);
            Assert.Empty(input.Joysticks);
            Assert.Empty(input.OtherDevices);
            IKeyboard keyboard = Assert.Single(input.Keyboards);
            IMouse mouse = Assert.Single(input.Mice);
            Assert.IsType(Resolve(GlfwKeyboardMouseInputContext.KeyboardTypeName), keyboard);
            Assert.IsType(Resolve(GlfwKeyboardMouseInputContext.MouseTypeName), mouse);

            // Both read the window they were subscribed to.
            Assert.False(keyboard.IsKeyPressed(Key.A));
            Assert.False(mouse.IsButtonPressed(MouseButton.Left));
            Assert.Equal(CursorMode.Normal, mouse.Cursor.CursorMode);

            // The mouse is updated once per pass over the window's events.
            Assert.Equal(handlersBefore + 1, ProcessEventsHandlerCount(window));
            window.DoEvents();
            window.DoEvents();
        }
        finally
        {
            input.Dispose();
        }

        Assert.Equal(handlersBefore, ProcessEventsHandlerCount(window));
    }

    private static Type Resolve(string assemblyQualifiedName) =>
        Type.GetType(assemblyQualifiedName, throwOnError: true)!;

    private static int ProcessEventsHandlerCount(IView window)
    {
        FieldInfo field = Resolve(GlfwKeyboardMouseInputContext.ViewTypeName).GetField(
            "ProcessEvents",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (field.GetValue(window) as Delegate)?.GetInvocationList().Length ?? 0;
    }
}
