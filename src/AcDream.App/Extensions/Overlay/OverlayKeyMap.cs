using AcDream.ClientExtensions;
using Silk.NET.Input;

namespace AcDream.App.Extensions.Overlay;

/// <summary>Silk.NET keys → the keys an overlay keyboard sink tells apart.</summary>
internal static class OverlayKeyMap
{
    public static OverlayKey Map(Key key) => key switch
    {
        Key.Enter => OverlayKey.Enter,
        Key.Escape => OverlayKey.Escape,
        Key.Backspace => OverlayKey.Backspace,
        Key.Delete => OverlayKey.Delete,
        Key.Left => OverlayKey.Left,
        Key.Right => OverlayKey.Right,
        Key.Up => OverlayKey.Up,
        Key.Down => OverlayKey.Down,
        Key.Home => OverlayKey.Home,
        Key.End => OverlayKey.End,
        Key.Insert => OverlayKey.Insert,
        Key.C => OverlayKey.C,
        Key.V => OverlayKey.V,
        Key.X => OverlayKey.X,
        _ => OverlayKey.Other,
    };
}
