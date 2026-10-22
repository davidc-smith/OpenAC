using AcDream.App.UI;
using Silk.NET.Input;

namespace AcDream.App.Input;

internal interface IInputCaptureSource
{
    bool WantCaptureMouse { get; }
    bool WantCaptureKeyboard { get; }
    bool DevToolsWantCaptureKeyboard { get; }

    /// <summary>
    /// Whether <paramref name="key"/>'s press is kept from the game. It can
    /// be true while <see cref="WantCaptureKeyboard"/> is false: a focused
    /// plugin dialog claims only the keys that answer it.
    /// </summary>
    bool ClaimsKey(Key key) => WantCaptureKeyboard;
}

internal sealed class DevToolsInputCaptureSource
{
    private readonly Func<bool>? _wantsMouse, _wantsKeyboard;
    public DevToolsInputCaptureSource(bool enabled, Func<bool>? wantsMouse = null, Func<bool>? wantsKeyboard = null)
    {
        _ = enabled;
        _wantsMouse = wantsMouse;
        _wantsKeyboard = wantsKeyboard;
    }

    public bool WantCaptureMouse => _wantsMouse?.Invoke() == true;

    public bool WantCaptureKeyboard => _wantsKeyboard?.Invoke() == true;
}

internal sealed class RetainedUiInputCaptureSlot : IHotkeyFocusSource
{
    public UiRoot? Root { get; private set; }

    public IDisposable Bind(UiRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (Root is not null)
            throw new InvalidOperationException("Retained UI input capture is already bound.");
        Root = root;
        return new Binding(this, root);
    }

    public bool WantCaptureMouse => Root?.WantsMouse ?? false;
    public bool WantCaptureKeyboard => Root?.WantsKeyboard ?? false;

    public bool HasKeyboardFocus => Root?.KeyboardFocus is { HoldsKeyboard: true };
    public bool ClaimsKey(Key key) => Root?.FocusClaimsKey(key) ?? false;
    public bool IsCapturingKeys => Root?.KeyboardFocus?.CapturesAllKeys == true;
    public bool IsModalOpen => Root?.Modal is not null;

    private void Unbind(UiRoot expected)
    {
        if (ReferenceEquals(Root, expected))
            Root = null;
    }

    private sealed class Binding : IDisposable
    {
        private RetainedUiInputCaptureSlot? _owner;
        private readonly UiRoot _expected;

        public Binding(RetainedUiInputCaptureSlot owner, UiRoot expected)
        {
            _owner = owner;
            _expected = expected;
        }

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.Unbind(_expected);
    }
}

internal sealed class CompositeInputCaptureSource : IInputCaptureSource
{
    private readonly DevToolsInputCaptureSource _devTools;
    private readonly RetainedUiInputCaptureSlot _retained;

    public CompositeInputCaptureSource(
        DevToolsInputCaptureSource devTools,
        RetainedUiInputCaptureSlot retained)
    {
        _devTools = devTools ?? throw new ArgumentNullException(nameof(devTools));
        _retained = retained ?? throw new ArgumentNullException(nameof(retained));
    }

    public bool WantCaptureMouse =>
        _devTools.WantCaptureMouse || _retained.WantCaptureMouse;

    public bool WantCaptureKeyboard =>
        DevToolsWantCaptureKeyboard || _retained.WantCaptureKeyboard;

    public bool DevToolsWantCaptureKeyboard =>
        _devTools.WantCaptureKeyboard;

    public bool ClaimsKey(Key key) =>
        DevToolsWantCaptureKeyboard || _retained.ClaimsKey(key);
}
