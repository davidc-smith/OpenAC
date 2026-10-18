namespace AcDream.App.UI;

/// <summary>
/// The stacking bands of the interface root's children, back to front.
/// Raising an element to the front keeps it inside its band, so a window
/// clicked to the front never climbs over the plugin canvases drawn above
/// windows, and a dialog raised every tick never sinks beneath them.
/// </summary>
public enum UiBand
{
    /// <summary>Real windows, and anything else the root raises without naming a band.</summary>
    Windows,

    /// <summary>
    /// The full-screen pre-game screens (connecting, character select and
    /// creation, credits): over every window and every plugin canvas, under
    /// dialogs and tooltips.
    /// </summary>
    Screens,

    /// <summary>
    /// Confirmation dialogs and tooltips: over everything except what the
    /// overlay pass draws (menus, the drag ghost) and pinned children.
    /// </summary>
    DialogsAndTooltips,
}

/// <summary>
/// Where each <see cref="UiBand"/> lies in the root's z-order, and the two
/// values that belong to no band.
///
/// <para>Bottom to top: the overlay band and everything else below
/// <see cref="Layout.UiOverlayZOrder.WindowFloor"/>; windows from the floor
/// up to <see cref="WindowsCeiling"/>; the layer of plugin canvases drawn
/// above windows at <see cref="CanvasesAboveWindows"/>; screens; dialogs and
/// tooltips; and <see cref="Pinned"/> children (the SpewBox, the credits'
/// click surface), which nothing is ever raised over.</para>
/// </summary>
internal static class UiBands
{
    /// <summary>The front of the window band, exclusive.</summary>
    public const int WindowsCeiling = 1_000_000_000;

    /// <summary>
    /// Where the click-through layer of plugin canvases drawn above windows
    /// sits. It is in no band: nothing is raised into it or past it by a
    /// raise in another band.
    /// </summary>
    public const int CanvasesAboveWindows = WindowsCeiling;

    /// <summary>The back of the screens band.</summary>
    public const int ScreensFloor = CanvasesAboveWindows + 1;

    /// <summary>The back of the dialogs-and-tooltips band, and the front of the screens band, exclusive.</summary>
    public const int DialogsFloor = 1_500_000_000;

    /// <summary>
    /// Children here are pinned on top: never raised, never counted when
    /// another child is raised, and never reached by a raise.
    /// </summary>
    public const int Pinned = int.MaxValue;

    /// <summary>
    /// Where the interface's upper render layer starts: everything at or
    /// above the canvases drawn over windows is drawn after every window,
    /// text included (see <see cref="Rendering.TextRenderer"/>).
    /// </summary>
    public const int UpperRenderLayerFloor = CanvasesAboveWindows;

    /// <summary>The band a z-order lies in, or null for the canvas layer and pinned children.</summary>
    public static UiBand? Of(int zOrder) => zOrder switch
    {
        Pinned => null,
        >= DialogsFloor => UiBand.DialogsAndTooltips,
        >= ScreensFloor => UiBand.Screens,
        >= WindowsCeiling => null,
        _ => UiBand.Windows,
    };

    /// <summary>
    /// Where an element moved into the band starts, and the band's front,
    /// exclusive. A window band member below the floor (an overlay, an
    /// imported layout root one level back) is in the band but keeps its
    /// own value until it is raised.
    /// </summary>
    public static (int Floor, int Ceiling) Range(UiBand band) => band switch
    {
        UiBand.Windows => (Layout.UiOverlayZOrder.WindowFloor, WindowsCeiling),
        UiBand.Screens => (ScreensFloor, DialogsFloor),
        UiBand.DialogsAndTooltips => (DialogsFloor, Pinned),
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, null),
    };
}
