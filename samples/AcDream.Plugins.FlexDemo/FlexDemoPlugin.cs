using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.FlexDemo;

/// <summary>
/// Two windows laid out with markup flex instead of coordinates: a finder
/// (a toolbar over a list that takes the spare height, over a footer) and a
/// settings form whose summary line and button caption change length at
/// runtime, so the window grows to fit them. Resize the finder, switch the
/// shared appearance from the dock's gear, and press "Show details" to watch
/// both reflow.
/// </summary>
public sealed class FlexDemoPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private IDisposable? _finder;
    private IDisposable? _settings;
    private readonly FinderBinding _finderBinding = new();
    private readonly SettingsBinding _settingsBinding = new();

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        // The host loads plugin assemblies from memory, so Assembly.Location is
        // empty; a relative markup path is read from the install folder.
        string Markup(string file) => host.PluginDirectory is { } dir ? Path.Combine(dir, file) : file;
        _finder = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("finder", "Flex Finder") { IconText = "FF", StartVisible = true },
            Markup("finder.xml"), _finderBinding);
        _settings = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("settings", "Flex Settings") { IconText = "FS", StartVisible = true },
            Markup("settings.xml"), _settingsBinding);
    }

    public void Disable()
    {
        _finder?.Dispose();
        _finder = null;
        _settings?.Dispose();
        _settings = null;
    }

    /// <summary>A search box over the names it finds.</summary>
    public sealed class FinderBinding
    {
        private static readonly string[] Names =
        [
            "Asheron", "Bael'Zharon", "Ispar", "Holtburg", "Shoushi", "Yaraq",
            "Rithwic", "Arwic", "Cragstone", "Eastham", "Glenden Wood", "Lytelthorpe",
        ];

        public string Search { get; private set; } = string.Empty;
        public Action<string> SetSearch => value => Search = value;
        public IReadOnlyList<string> Results { get; private set; } = Names;
        public int Selected { get; private set; } = -1;
        public Action<int> Select => index => Selected = index;
        public string Status => Selected >= 0 && Selected < Results.Count
            ? $"Selected {Results[Selected]}"
            : $"{Results.Count} found";

        public Action Find => Run;
        public Action<string> Submit => value => { Search = value; Run(); };
        public Action Clear => () => { Search = string.Empty; Run(); };
        public Action Done => () => Selected = -1;

        private void Run()
        {
            Results = Names.Where(n => n.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToArray();
            Selected = -1;
        }
    }

    /// <summary>A short form whose summary and detail button change length on demand.</summary>
    public sealed class SettingsBinding
    {
        private bool _detail;

        public IReadOnlyList<string> Profiles { get; } = ["Mage", "Melee", "Archer", "Lifestone runner"];
        public string Profile { get; private set; } = "Mage";
        public Action<string> SetProfile => value => Profile = value;
        public string Target { get; private set; } = "Asheron";
        public Action<string> SetTarget => value => Target = value;
        public bool AutoRebuff { get; private set; } = true;
        public Action ToggleAutoRebuff => () => AutoRebuff = !AutoRebuff;
        public bool Announce { get; private set; }
        public Action ToggleAnnounce => () => Announce = !Announce;

        public string Summary => _detail
            ? $"{Profile} on {Target}: rebuff {(AutoRebuff ? "on" : "off")}, announcements {(Announce ? "on" : "off")}"
            : Profile;
        public string DetailCaption => _detail ? "Hide details" : "Show details";
        public Action ToggleDetail => () => _detail = !_detail;
        public Action Apply => () => { };
    }
}
