using AcDream.Plugin.Abstractions;

namespace AcDream.Plugins.WorldMarkersDemo;

/// <summary>
/// Hangs a row of spell icons over the selected object -- or over the player
/// when nothing is selected -- and pins a larger, half-transparent icon a few
/// metres east of where the player stood when the demo first saw them. It
/// exists to be looked at, for example to check the icon overlay on a
/// high-density display, and does nothing else.
/// </summary>
public sealed class WorldMarkersDemoPlugin : IAcDreamPlugin
{
    // The first spells in the client's spell table; any spell with an icon would do.
    private static readonly uint[] RowSpellIds = [1u, 2u, 3u, 4u, 5u];
    private const uint SpotSpellId = 6u;
    private const double MetresPerMapUnit = 240d;
    private static readonly PluginColor Hostile = new(220, 60, 60);

    private readonly List<PluginImage> _row = [];
    private IPluginHost? _host;
    private IPluginWorldMarkerLayer? _layer;
    private PluginImage _spotImage = PluginImage.None;
    private PluginNavigationPosition? _spot;
    private uint _shownOver;
    private bool _dirty;

    public void Initialize(IPluginHost host) => _host = host;

    public void Enable()
    {
        if (_host is not { } host) return;
        _layer = host.Ui.WorldMarkers.CreateLayer();
        host.Events.Tick += OnTick;
        host.Events.Logoff += OnLogoff;
    }

    public void Disable()
    {
        if (_host is { } host)
        {
            host.Events.Tick -= OnTick;
            host.Events.Logoff -= OnLogoff;
        }
        _layer?.Dispose();
        _layer = null;
    }

    // The client clears every layer on logoff; forget what was shown so the
    // next stay sets the icons again.
    private void OnLogoff()
    {
        _shownOver = 0u;
        _spot = null;
    }

    /// <summary>Images can only be asked for once the interface is up, and the icons follow the selection.</summary>
    private void OnTick(double deltaSeconds)
    {
        if (_host is not { } host || _layer is not { } layer) return;
        IPluginImages images = host.Ui.Images;
        if (!images.IsAvailable)
        {
            // The interface went away (a reconnect drops images): ask again when it is back.
            _row.Clear();
            _spotImage = PluginImage.None;
            return;
        }
        if (_row.Count == 0)
        {
            foreach (uint spellId in RowSpellIds)
                _row.Add(images.FromSpellIcon(spellId));
            _spotImage = images.FromSpellIcon(SpotSpellId);
            _dirty = true;
        }

        PluginNavigationSnapshot navigation = host.Automation.Navigation.Snapshot;
        if (!navigation.IsAvailable) return;
        if (_spot is null)
        {
            PluginNavigationPosition here = navigation.Position;
            _spot = here with
            {
                EastWest = here.EastWest + 5d / MetresPerMapUnit,
                Elevation = here.Elevation + 1.5d / MetresPerMapUnit,
            };
            _dirty = true;
        }

        uint over = host.Selection.SelectedObjectId ?? navigation.LocalObjectId;
        if (over != _shownOver)
        {
            _shownOver = over;
            _dirty = true;
        }
        if (!_dirty || over == 0u) return;
        _dirty = false;

        var icons = new List<PluginWorldIcon>(_row.Count + 1);
        for (int i = 0; i < _row.Count; i++)
        {
            icons.Add(new PluginWorldIcon(PluginMarkerAnchor.Object(over), _row[i])
            {
                // Two framed, one faded, the rest plain: one of each look.
                Border = i < 2 ? Hostile : null,
                Tint = i == 2 ? new PluginColor(255, 255, 255, 110) : PluginColor.White,
            });
        }
        icons.Add(new PluginWorldIcon(PluginMarkerAnchor.At(_spot.Value), _spotImage)
        {
            SizePixels = 40f,
            Tint = new PluginColor(255, 255, 255, 180),
            MaxRange = 120f,
        });
        layer.SetIcons(icons);
    }
}
