namespace AcDream.App.UI;

/// <summary>
/// A menu an element opens on a right click. <see cref="UiRoot"/> offers each
/// element's menu a right click the element itself left unhandled, from the
/// clicked element outwards, so the innermost menu wins.
/// </summary>
internal interface IUiContextMenu
{
    /// <summary>Opens at a point in canvas coordinates; false when there is nothing to show.</summary>
    bool TryOpen(int screenX, int screenY);
}
