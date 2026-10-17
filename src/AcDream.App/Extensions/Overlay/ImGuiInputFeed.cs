using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Subscribes the overlay's input queue to the window's mice and keyboards,
/// and to the window's focus so held keys and buttons release when it's lost.
/// </summary>
internal sealed class ImGuiInputFeed : IDisposable
{
    private readonly IInputContext _input;
    private readonly ImGuiInputQueue _queue;
    private readonly IView? _window;

    public ImGuiInputFeed(IInputContext input, ImGuiInputQueue queue, IView? window = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _window = window;
        foreach (IMouse mouse in input.Mice)
        {
            mouse.MouseMove += OnMouseMove;
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.Scroll += OnScroll;
        }

        foreach (IKeyboard keyboard in input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        if (_window is not null)
            _window.FocusChanged += OnFocusChanged;
    }

    public void Dispose()
    {
        if (_window is not null)
            _window.FocusChanged -= OnFocusChanged;

        foreach (IMouse mouse in _input.Mice)
        {
            mouse.MouseMove -= OnMouseMove;
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.Scroll -= OnScroll;
        }

        foreach (IKeyboard keyboard in _input.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
            keyboard.KeyChar -= OnKeyChar;
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position) => _queue.MouseMove(position);

    private void OnMouseDown(IMouse mouse, MouseButton button) => _queue.MouseButton(button, true);

    private void OnMouseUp(IMouse mouse, MouseButton button) => _queue.MouseButton(button, false);

    private void OnScroll(IMouse mouse, ScrollWheel wheel) => _queue.Scroll(new Vector2(wheel.X, wheel.Y));

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode) => _queue.KeyChange(key, true);

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => _queue.KeyChange(key, false);

    private void OnKeyChar(IKeyboard keyboard, char value) => _queue.Character(value);

    private void OnFocusChanged(bool focused) => _queue.Focus(focused);
}
