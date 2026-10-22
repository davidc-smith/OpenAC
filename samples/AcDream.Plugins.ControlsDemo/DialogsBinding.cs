using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The Dialogs window: message boxes with two, three and four buttons, one
/// with no owner (centred on the screen, blocking nothing), two queued for
/// this window, and a modal editor. The last button is always the safe one,
/// because Escape, the close button and unloading answer with it.
/// </summary>
public sealed class DialogsBinding
{
    private const string Owner = "dialogs";

    private readonly IUiRegistry ui;
    private readonly Profile profile;
    private readonly EditorBinding editor;

    public DialogsBinding(IUiRegistry ui, Profile profile, EditorBinding editor)
    {
        this.ui = ui;
        this.profile = profile;
        this.editor = editor;
        editor.Report = message => Status = message;
    }

    public Action AskDefault => () => Ask(new PluginDialog(
        "Two buttons", "No buttons given means OK and Cancel. Escape answers Cancel.", OwnerView: Owner));

    public Action AskThree => () => Ask(new PluginDialog(
        "Unsaved changes", "Save the route before closing it?", ["Save", "Discard", "Cancel"], Owner));

    public Action AskFour => () => Ask(new PluginDialog(
        "Pick a pace", "Four buttons widen the box to fit them.", ["Walk", "Jog", "Run", "Stay"], Owner));

    public Action AskUnowned => () => Ask(new PluginDialog(
        "No owner", "Without an owner window the box is centred on the screen and blocks nothing.", ["Got it"]));

    public Action AskTwo => () =>
    {
        Ask(new PluginDialog("First of two", "The second box waits until this one is answered.", ["Next"], Owner));
        Ask(new PluginDialog("Second of two", "Boxes for one window show one at a time.", ["Done"], Owner));
    };

    public Action EditProfile => () =>
    {
        editor.Load();
        Status = ui.ShowModal("editor", Owner) ? "Editing the profile" : "No editor window";
    };

    /// <summary>Items are Edit profile...|Reset profile.</summary>
    public Action<int> ProfileMenu => index =>
    {
        if (index == 0) { EditProfile(); return; }
        Ask(new PluginDialog("Reset profile", $"Reset \"{profile.Name}\" to its defaults?", ["Reset", "Keep"], Owner),
            answer => { if (answer == 0) profile.Reset(); });
    };

    public string ProfileSummary => $"{profile.Name}, level {profile.Level}{(profile.UseOnLogin ? ", on login" : "")}";

    public string Status { get; private set; } = "Press a button";

    private void Ask(PluginDialog dialog, Action<int>? then = null)
    {
        bool shown = ui.Confirm(dialog, index =>
        {
            Status = $"{dialog.Title}: {dialog.EffectiveButtons[index]}";
            then?.Invoke(index);
        });
        if (!shown) Status = "This host has no dialogs";
    }
}
