namespace AcDream.App.UI.Markup;

/// <summary>
/// The colours the player picked last, newest first, at most
/// <see cref="Capacity"/>, shared by every colour picker that gets this list.
/// With a state (the store's <see cref="MarkupControlStateStore.Shared"/>) the
/// list is remembered for the character as "#AARRGGBB" text, comma-separated;
/// without one it lasts only as long as the list does. UI thread only.
/// </summary>
public sealed class MarkupRecentColors
{
    public const int Capacity = 8;
    public const string Control = "color";
    public const string Kind = "recent";

    private readonly RememberedValue _remembered;
    private List<uint> _colors = new();

    public MarkupRecentColors(IMarkupControlState? state)
    {
        _remembered = new RememberedValue(state, Control, Kind);
    }

    public IReadOnlyList<uint> Colors
    {
        get
        {
            Refresh();
            return _colors;
        }
    }

    /// <summary>Puts a picked colour first (moving it if it is already there).</summary>
    public void Add(uint argb)
    {
        Refresh();
        _colors.Remove(argb);
        _colors.Insert(0, argb);
        if (_colors.Count > Capacity)
            _colors.RemoveRange(Capacity, _colors.Count - Capacity);
        _remembered.Remember(Encode(_colors));
    }

    private void Refresh()
    {
        if (_remembered.TryRefresh(out string? text))
            _colors = Decode(text);
    }

    internal static string Encode(IEnumerable<uint> colors) =>
        string.Join(",", colors.Select(c => MarkupColorValue.Format(c)));

    /// <summary>The stored list: unreadable entries skipped, repeats dropped, at most <see cref="Capacity"/>.</summary>
    internal static List<uint> Decode(string? text)
    {
        var colors = new List<uint>();
        if (string.IsNullOrEmpty(text))
            return colors;
        foreach (string part in text.Split(','))
        {
            if (MarkupColorValue.ParseStrict(part) is { } c && !colors.Contains(c))
                colors.Add(c);
            if (colors.Count == Capacity)
                break;
        }
        return colors;
    }
}
