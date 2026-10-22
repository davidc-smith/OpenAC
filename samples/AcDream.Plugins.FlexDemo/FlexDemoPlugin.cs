using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.FlexDemo;

/// <summary>
/// Four windows laid out with markup flex instead of coordinates: a finder
/// (a toolbar over a list that takes the spare height, over a footer), a
/// settings form whose summary line and button caption change length at
/// runtime, so the window grows to fit them, and a gallery of spell icons
/// that wraps and scrolls, over a strip of recent spells that scrolls
/// sideways (swipe, tilt the wheel, or hold Shift). Resize the finder and the
/// gallery, make the settings window short to scroll it, switch the shared
/// appearance from the dock's gear, and press "Show details" to watch them
/// reflow. The fourth window, Flex Controls, puts the newer markup controls
/// inside flex: a checkbox, a segmented radio, a number, a separator, two
/// expanders that push their neighbours when they fold, and a split whose
/// panes are a column and a row.
/// </summary>
public sealed class FlexDemoPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private IDisposable? _finder;
    private IDisposable? _settings;
    private IDisposable? _gallery;
    private IDisposable? _controls;
    private readonly FinderBinding _finderBinding = new();
    private readonly SettingsBinding _settingsBinding = new();
    private readonly ControlsBinding _controlsBinding = new();

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
        _gallery = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("gallery", "Flex Gallery") { IconText = "FG", StartVisible = true },
            Markup("gallery.xml"), new GalleryBinding());
        _controls = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("controls", "Flex Controls") { IconText = "FC", StartVisible = true },
            Markup("controls.xml"), _controlsBinding);
    }

    public void Disable()
    {
        _finder?.Dispose();
        _finder = null;
        _settings?.Dispose();
        _settings = null;
        _gallery?.Dispose();
        _gallery = null;
        _controls?.Dispose();
        _controls = null;
    }

    /// <summary>The new markup controls inside flex: what each one last reported.</summary>
    public sealed class ControlsBinding
    {
        public bool Loot { get; private set; } = true;
        public Action<bool> SetLoot => value => { Loot = value; Status = $"Loot {value}"; };
        public int Speed { get; private set; }
        public Action<int> SetSpeed => value => { Speed = value; Status = $"Speed {value}"; };
        public int HealAt { get; private set; } = 60;
        public Action<int> SetHealAt => value => { HealAt = value; Status = $"Heal at {value}%"; };
        public string Status { get; private set; } = "Fold an expander or drag the split";
    }

    /// <summary>The gallery's icons are literal spell ids; it binds nothing.</summary>
    public sealed class GalleryBinding;

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
        public int ProfileIndex => ((IList<string>)Profiles).IndexOf(Profile);
        public Action<int> PickProfile => index => Profile = Profiles[index];
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
