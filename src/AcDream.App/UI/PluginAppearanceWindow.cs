namespace AcDream.App.UI;

/// <summary>
/// The Plugin appearance window the dock's gear opens. The gear opens and
/// closes it, as a plugin's slot does for that plugin's window. It is built
/// on first open and kept, hidden, between opens.
/// </summary>
/// <param name="root">The interface the window is added to.</param>
/// <param name="build">Builds the window, given what closes it (for its own Close button).</param>
/// <param name="onOpen">Called with the window on every open, just before it is shown (to place it).</param>
internal sealed class PluginAppearanceWindow(UiRoot root, Func<Action, UiElement> build, Action<UiElement>? onOpen = null)
{
    private UiElement? _window;

    /// <summary>Whether the window is shown.</summary>
    internal bool IsOpen => _window is { Visible: true };

    /// <summary>Closes the window when it is shown, else opens it in front.</summary>
    internal void Toggle()
    {
        if (IsOpen)
        {
            Close();
            return;
        }
        if (_window is null)
        {
            _window = build(Close);
            root.AddChild(_window);
        }
        onOpen?.Invoke(_window);
        _window.Visible = true;
        root.BringToFront(_window);
    }

    /// <summary>Opens the window when it is closed (placed as any open places it), else brings it to the front.</summary>
    internal void Show()
    {
        if (IsOpen)
        {
            root.BringToFront(_window!);
            return;
        }
        Toggle();
    }

    private void Close()
    {
        if (_window is not null)
            _window.Visible = false;
    }
}
