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
}
