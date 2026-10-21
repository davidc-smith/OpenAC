namespace AcDream.App.Extensions.Overlay;

/// <summary>
/// What the retail UI's raw input bindings check so a click or key meant for
/// an ImGui window doesn't also reach the retail panel underneath. Static so
/// the gate costs the upstream bindings one condition each.
/// </summary>
internal static class OverlayInputGate
{
    private static ImGuiOverlay? _overlay;

    public static bool SuppressMouse => _overlay?.WantsMouse == true;

    public static bool SuppressKeyboard => _overlay?.WantsKeyboard == true;

    public static void Bind(ImGuiOverlay? overlay) => _overlay = overlay;

    private static OverlayKeyGate? _keys;

    /// <summary>True when an overlay sink took this key's current press (or its release).</summary>
    public static bool KeyTaken(Silk.NET.Input.Key key) => _keys?.IsTaken(key) == true;

    /// <summary>True while a taken key is down: typed characters belong to the sink.</summary>
    public static bool CharTaken => _keys?.CharTaken == true;

    public static void BindKeys(OverlayKeyGate? keys) => _keys = keys;

    /// <summary>Unbinds the gate only when it is still the bound one.</summary>
    public static void UnbindKeys(OverlayKeyGate keys)
    {
        if (ReferenceEquals(_keys, keys))
            _keys = null;
    }
}
