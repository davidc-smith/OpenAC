namespace AcDream.Plugin.Abstractions;

/// <summary>
/// A question a plugin asks the player in a dialog box, opened with
/// <see cref="IUiRegistry.Confirm"/>.
/// </summary>
/// <param name="Title">The dialog's caption. May be empty.</param>
/// <param name="Message">The question or message; long text wraps. May be empty.</param>
/// <param name="Buttons">
/// The button captions, left to right: one to four of them, none blank. The
/// last one is the cancel button: Escape, the close button, the owner window
/// closing and the plugin unloading all answer with its index. Null means
/// <see cref="DefaultButtons"/>.
/// </param>
/// <param name="OwnerView">
/// The id or title of one of the plugin's own windows. The dialog is centred
/// on it and blocks it until it is answered. Null, an unknown window or a
/// hidden one centres the dialog on the screen and blocks nothing.
/// </param>
public sealed record PluginDialog(
    string Title,
    string Message,
    IReadOnlyList<string>? Buttons = null,
    string? OwnerView = null)
{
    /// <summary>The buttons of a dialog that names none: OK and Cancel.</summary>
    public static IReadOnlyList<string> DefaultButtons { get; } = ["OK", "Cancel"];

    /// <summary>The buttons the dialog shows: <see cref="Buttons"/>, or <see cref="DefaultButtons"/> when that is null.</summary>
    public IReadOnlyList<string> EffectiveButtons => Buttons ?? DefaultButtons;
}
