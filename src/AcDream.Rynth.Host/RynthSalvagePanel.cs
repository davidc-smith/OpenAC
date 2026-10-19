namespace AcDream.Rynth.Host;

/// <summary>
/// RynthCore's salvage panel, kept by the host: the tool it was opened with and the
/// items added since. OpenAC's own salvage window is not involved; Execute sends the
/// list in one salvage request, as the panel's Salvage button does.
/// </summary>
internal sealed class RynthSalvagePanel
{
    /// <summary>The most ids one salvage request takes (RynthCore's MaxSalvageDirectItems).</summary>
    internal const int MaxItems = 100;

    private readonly IRynthGameView _view;
    private readonly List<uint> _items = [];
    private uint _toolId;

    public RynthSalvagePanel(IRynthGameView view) => _view = view;

    public bool Open(uint toolId)
    {
        if (toolId == 0 || !_view.IsOwnedTinkeringTool(toolId))
            return false;
        _toolId = toolId;
        _items.Clear();
        return true;
    }

    public bool Add(uint itemId)
    {
        if (_toolId == 0 || itemId == 0 || itemId == _toolId)
            return false;
        if (_items.Contains(itemId))
            return true;
        if (_items.Count >= MaxItems || !_view.IsOwnedItem(itemId))
            return false;
        _items.Add(itemId);
        return true;
    }

    public bool Execute()
    {
        if (_toolId == 0 || _items.Count == 0 || !_view.Salvage(_toolId, _items.ToArray()))
            return false;
        _items.Clear();
        return true;
    }

    /// <summary>Closes the panel (logout).</summary>
    public void Close()
    {
        _toolId = 0;
        _items.Clear();
    }
}
