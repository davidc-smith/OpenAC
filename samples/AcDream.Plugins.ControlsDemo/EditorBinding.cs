using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// The modal "Edit profile" window. It edits a copy, so Cancel and the close
/// button change nothing; Save copies it back. Hiding the window ends the
/// block on the Dialogs window.
/// </summary>
public sealed class EditorBinding(IUiRegistry ui, Profile profile)
{
    public string Name { get; private set; } = profile.Name;
    public Action<string> SetName => value => Name = value;
    public int Level { get; private set; } = profile.Level;
    public Action<int> SetLevel => value => Level = value;
    public bool UseOnLogin { get; private set; } = profile.UseOnLogin;
    public Action<bool> SetUseOnLogin => value => UseOnLogin = value;

    /// <summary>Starts a fresh copy; called before the window is shown.</summary>
    public void Load()
    {
        Name = profile.Name;
        Level = profile.Level;
        UseOnLogin = profile.UseOnLogin;
    }

    public Action Save => () =>
    {
        profile.Name = Name.Trim().Length == 0 ? profile.Name : Name.Trim();
        profile.Level = Level;
        profile.UseOnLogin = UseOnLogin;
        ui.HidePanel("editor");
    };

    public Action Cancel => () => ui.HidePanel("editor");
}
