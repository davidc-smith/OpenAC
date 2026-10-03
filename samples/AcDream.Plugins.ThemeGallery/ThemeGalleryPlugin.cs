using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.ThemeGallery;

/// <summary>
/// One window with every markup control, opted into the shared plugin
/// theme. It exists to be looked at -- switch between Classic, Moss and
/// Brass from the dock's gear (Plugin appearance) -- and does nothing else.
/// </summary>
public sealed class ThemeGalleryPlugin : IAcDreamPlugin
{
    private IPluginHost? _host;
    private IDisposable? _window;
    private IDisposable? _swatches;
    private readonly GalleryBinding _binding = new();

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        // The host loads plugin assemblies from memory, so Assembly.Location is
        // empty; a relative markup path is read from the install folder.
        string markup = host.PluginDirectory is { } dir ? Path.Combine(dir, "gallery.xml") : "gallery.xml";
        _window = host.Ui.RegisterPanel(
            new PluginPanelDescriptor("gallery", "Theme Gallery") { IconText = "TG", StartVisible = true },
            markup, _binding);
        // A second window with its own SVG, so the dock shows both a plugin icon.svg
        // and a per-window IconFile side by side.
        _swatches = host.Ui.RegisterPanelContent(
            new PluginPanelDescriptor("swatches", "Swatches") { IconText = "SW", IconFile = "icons/swatch.svg", StartVisible = false },
            """
            <panel x="420" y="120" w="220" h="96" title="Swatches" theme="plugin">
              <label x="12" y="36" text="This window's dock icon is icons/swatch.svg." />
            </panel>
            """,
            _binding);
    }

    public void Disable()
    {
        _window?.Dispose();
        _window = null;
        _swatches?.Dispose();
        _swatches = null;
    }

    /// <summary>Plain properties the markup binds to; every action only changes what is shown.</summary>
    public sealed class GalleryBinding
    {
        private int _tab;
        private readonly List<string> _log = ["Gallery opened."];
        private readonly bool[] _spellEnabled = [true, true, false, true];

        public bool StatusTab => _tab == 0;
        public bool SpellsTab => _tab == 1;
        public bool SettingsTab => _tab == 2;
        public Action ShowStatus => () => _tab = 0;
        public Action ShowSpells => () => _tab = 1;
        public Action ShowSettings => () => _tab = 2;

        public string Target { get; private set; } = "Asheron";
        public Action<string> SetTarget => value => Target = value;

        public IReadOnlyList<string> Profiles { get; } = ["Mage", "Melee", "Archer", "Tinker", "Support", "Lifestone runner"];
        public string Profile { get; private set; } = "Mage";
        public Action<string> SetProfile => value => { Profile = value; Write($"Profile: {value}"); };

        public bool AutoRebuff { get; private set; } = true;
        public Action ToggleAutoRebuff => () => AutoRebuff = !AutoRebuff;
        public bool Announce { get; private set; }
        public Action ToggleAnnounce => () => Announce = !Announce;

        public float Threshold { get; private set; } = 60f;
        public Action<float> SetThreshold => value => Threshold = value;

        public IReadOnlyList<string> Spells { get; } =
            ["Strength Self VII", "Invulnerability Self VII", "Impregnability Self VII", "Focus Self VII"];
        public IReadOnlyList<bool> SpellEnabled => _spellEnabled;
        public Action<int> ToggleSpell => index => { if (index >= 0 && index < _spellEnabled.Length) _spellEnabled[index] = !_spellEnabled[index]; };
        public int Spell { get; private set; } = 1;
        public Action<int> SelectSpell => index => Spell = index;

        public IReadOnlyList<string> Log { get; private set; } = ["Gallery opened."];
        public float Mana => 0.62f;

        public bool Running { get; private set; }
        public Action Start => () => { Running = true; Write("Started."); };
        public Action Stop => () => { Running = false; Write("Stopped."); };

        private void Write(string line)
        {
            _log.Add(line);
            Log = _log.ToArray();
        }
    }
}
