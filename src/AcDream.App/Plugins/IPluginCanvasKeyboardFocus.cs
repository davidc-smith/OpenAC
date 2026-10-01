namespace AcDream.App.Plugins;

/// <summary>
/// How a mounted canvas element answers its registration's keyboard focus
/// calls. The element sets itself on the registration when it mounts and
/// clears itself when it comes down, so a canvas that is not mounted
/// answers as a host without a window does.
/// </summary>
internal interface IPluginCanvasKeyboardFocus
{
    /// <summary>Whether the element has the interface's keyboard focus.</summary>
    bool HasFocus { get; }

    /// <summary>Takes keyboard focus if every condition allows it; true when the element has it afterwards.</summary>
    bool RequestFocus();

    /// <summary>Gives keyboard focus back, if the element still has it.</summary>
    void ReleaseFocus();
}
