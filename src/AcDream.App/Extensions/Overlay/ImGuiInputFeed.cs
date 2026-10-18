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
    private readonly ImGuiOverlay? _sinks;
    private readonly KeyboardClipboard? _clipboard;

    public ImGuiInputFeed(IInputContext input, ImGuiInputQueue queue, IView? window = null, ImGuiOverlay? sinks = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _window = window;
        _sinks = sinks;
        if (sinks is not null && input.Keyboards.FirstOrDefault() is { } first)
        {
            _clipboard = new KeyboardClipboard(first);
            sinks.ClipboardSource = _clipboard;
        }

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
        if (_sinks is not null && ReferenceEquals(_sinks.ClipboardSource, _clipboard))
            _sinks.ClipboardSource = null;

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

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        _queue.KeyChange(key, true);
        _sinks?.SinkKeyDown(
            key,
            Held(keyboard, Key.ControlLeft, Key.ControlRight, Key.SuperLeft, Key.SuperRight),
            Held(keyboard, Key.ShiftLeft, Key.ShiftRight));
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int scancode)
    {
        _queue.KeyChange(key, false);
        _sinks?.SinkKeyUp(key);
    }

    private void OnKeyChar(IKeyboard keyboard, char value)
    {
        _queue.Character(value);
        _sinks?.SinkCharacter(value);
    }

    private static bool Held(IKeyboard keyboard, params Key[] keys) => keys.Any(keyboard.IsKeyPressed);

    private void OnFocusChanged(bool focused) => _queue.Focus(focused);
}

/// <summary>The system clipboard, as the overlay reads and writes it.</summary>
internal interface IClipboardSource
{
    string ClipboardText { get; set; }
}

/// <summary>The window keyboard's clipboard.</summary>
internal sealed class KeyboardClipboard(IKeyboard keyboard) : IClipboardSource
{
    public string ClipboardText
    {
        get => keyboard.ClipboardText ?? string.Empty;
        set => keyboard.ClipboardText = value;
    }
}
