using System;
using System.Collections.Generic;
using AcDream.UI.Abstractions.Input;
using Silk.NET.Input;

namespace AcDream.UI.Abstractions.Tests.Input;

internal sealed class FakeMouseSource : IMouseSource
{
    public event Action<MouseButton, ModifierMask>? MouseDown;
    public event Action<MouseButton, ModifierMask>? MouseUp;
    public event Action<float, float>? MouseMove;
    public event Action<float>? Scroll;

    private readonly HashSet<MouseButton> _held = new();

    public bool IsHeld(MouseButton button) => _held.Contains(button);

    public bool WantCaptureMouse { get; set; }
    public bool WantCaptureKeyboard { get; set; }

    /// <summary>Keys claimed one by one, as a focused dialog claims its answer keys.</summary>
    public HashSet<Key> ClaimedKeys { get; } = new();

    public bool ClaimsKey(Key key) => WantCaptureKeyboard || ClaimedKeys.Contains(key);

    public void EmitMouseDown(MouseButton button, ModifierMask mods)
    {
        _held.Add(button);
        MouseDown?.Invoke(button, mods);
    }

    public void EmitMouseUp(MouseButton button, ModifierMask mods)
    {
        _held.Remove(button);
        MouseUp?.Invoke(button, mods);
    }

    public void EmitMouseMove(float dx, float dy) => MouseMove?.Invoke(dx, dy);
    public void EmitScroll(float delta) => Scroll?.Invoke(delta);
}
