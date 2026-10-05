namespace AcDream.App.UI;

/// <summary>
/// The Plugin appearance window the dock's gear opens. The gear opens and
/// closes it, as a plugin's slot does for that plugin's window. It is built
/// on first open and kept, hidden, between opens.
/// </summary>
/// <param name="root">The interface the window is added to.</param>
/// <param name="build">Builds the window, given what closes it (for its own Close button).</param>
internal sealed class PluginAppearanceWindow(UiRoot root, Func<Action, UiElement> build)
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
        _window.Visible = true;
        root.BringToFront(_window);
    }

    private void Close()
    {
        if (_window is not null)
            _window.Visible = false;
    }
}
