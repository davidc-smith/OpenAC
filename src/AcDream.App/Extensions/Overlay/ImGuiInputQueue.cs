using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// Collects input events as they arrive (Silk callbacks) and hands them to
/// ImGui at the start of the next frame. Thread-safe; touches no game state.
/// </summary>
internal sealed class ImGuiInputQueue
{
    private readonly object _gate = new();
    private readonly List<Action<ImGuiIOPtr>> _pending = new();

    /// <summary>Events are dropped while false (overlay inert), so nothing piles up.</summary>
    public bool Accepting { get; set; }

    /// <summary>Discards everything queued.</summary>
    public void Clear()
    {
        lock (_gate)
            _pending.Clear();
    }

    public void MouseMove(Vector2 points) => Add(io => io.AddMousePosEvent(points.X, points.Y));

    public void MouseButton(MouseButton button, bool down)
    {
        int index = button switch
        {
            Silk.NET.Input.MouseButton.Left => 0,
            Silk.NET.Input.MouseButton.Right => 1,
            Silk.NET.Input.MouseButton.Middle => 2,
            _ => -1,
        };
        if (index >= 0)
            Add(io => io.AddMouseButtonEvent(index, down));
    }

    public void Scroll(Vector2 wheel) => Add(io => io.AddMouseWheelEvent(wheel.X, wheel.Y));

    public void KeyChange(Key key, bool down)
    {
        ImGuiKey mapped = ImGuiKeyMap.Map(key);
        if (mapped == ImGuiKey.None)
            return;
        Add(io =>
        {
            io.AddKeyEvent(mapped, down);
            ImGuiKey modifier = mapped switch
            {
                ImGuiKey.LeftCtrl or ImGuiKey.RightCtrl => ImGuiKey.ModCtrl,
                ImGuiKey.LeftShift or ImGuiKey.RightShift => ImGuiKey.ModShift,
                ImGuiKey.LeftAlt or ImGuiKey.RightAlt => ImGuiKey.ModAlt,
                ImGuiKey.LeftSuper or ImGuiKey.RightSuper => ImGuiKey.ModSuper,
                _ => ImGuiKey.None,
            };
            if (modifier != ImGuiKey.None)
                io.AddKeyEvent(modifier, down);
        });
    }

    public void Character(char value) => Add(io => io.AddInputCharacterUTF16(value));

    /// <summary>Window focus; losing it makes ImGui release every held key and button.</summary>
    public void Focus(bool focused) => Add(io => io.AddFocusEvent(focused));

    /// <summary>Applies everything queued since the last call.</summary>
    public void ApplyTo(ImGuiIOPtr io)
    {
        Action<ImGuiIOPtr>[] events;
        lock (_gate)
        {
            events = _pending.ToArray();
            _pending.Clear();
        }

        foreach (Action<ImGuiIOPtr> apply in events)
            apply(io);
    }

    private void Add(Action<ImGuiIOPtr> apply)
    {
        lock (_gate)
        {
            if (Accepting)
                _pending.Add(apply);
        }
    }
}
