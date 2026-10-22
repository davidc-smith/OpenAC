using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.ControlsDemo;

/// <summary>
/// One window per group of plugin markup controls, each with a status line
/// that says what the last callback received: Inputs (checkboxes, number
/// fields, radio groups, separators), Keys and colours (hotkey boxes, colour
/// pickers), Layout (a split holding expanders), Table (a list with a header,
/// sorting, resizable columns, multi-select and a context menu), Tree (a tree
/// list) and Dialogs (message boxes and a modal editor). Every window opts in
/// to the plugin theme, so switching Plugin appearance from the shelf's
/// right-click menu shows each control in Classic and in both modern themes.
/// </summary>
public sealed class ControlsDemoPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private readonly List<IDisposable> _windows = [];

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        // The host loads plugin assemblies from memory, so Assembly.Location is
        // empty; a relative markup path is read from the install folder.
        string Markup(string file) => host.PluginDirectory is { } dir ? Path.Combine(dir, file) : file;
        void Add(PluginPanelDescriptor descriptor, string file, object binding) =>
            _windows.Add(host.Ui.RegisterPanel(descriptor, Markup(file), binding));

        Add(new PluginPanelDescriptor("inputs", "Controls: Inputs") { IconText = "CI" }, "inputs.xml", new InputsBinding());
        Add(new PluginPanelDescriptor("pickers", "Controls: Keys and colours") { IconText = "CK" }, "pickers.xml", new PickersBinding());
        Add(new PluginPanelDescriptor("layout", "Controls: Layout") { IconText = "CL" }, "layout.xml", new LayoutBinding());
        Add(new PluginPanelDescriptor("table", "Controls: Table") { IconText = "CT" }, "table.xml", new TableBinding(host.Ui));
        Add(new PluginPanelDescriptor("tree", "Controls: Tree") { IconText = "CR" }, "tree.xml", new TreeBinding());
        var profile = new Profile();
        var editor = new EditorBinding(host.Ui, profile);
        Add(new PluginPanelDescriptor("dialogs", "Controls: Dialogs") { IconText = "CD" }, "dialogs.xml",
            new DialogsBinding(host.Ui, profile, editor));
        // Opened only by the Dialogs window, modal over it.
        Add(new PluginPanelDescriptor("editor", "Edit profile") { StartVisible = false, ShowInSidePanel = false },
            "editor.xml", editor);
    }

    public void Disable()
    {
        foreach (IDisposable window in _windows) window.Dispose();
        _windows.Clear();
    }
}
