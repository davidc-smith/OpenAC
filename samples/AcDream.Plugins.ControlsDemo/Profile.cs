namespace AcDream.Plugins.ControlsDemo;

/// <summary>The settings the Dialogs window shows and the modal editor changes.</summary>
public sealed class Profile
{
    public string Name { get; set; } = "Hunter";
    public int Level { get; set; } = 126;
    public bool UseOnLogin { get; set; }

    public void Reset()
    {
        Name = "Hunter";
        Level = 126;
        UseOnLogin = false;
    }
}
