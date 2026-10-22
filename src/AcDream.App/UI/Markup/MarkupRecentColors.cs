namespace AcDream.App.UI.Markup;

/// <summary>
/// The colours the player picked last, newest first, at most
/// <see cref="Capacity"/>, shared by every colour picker that gets this list.
/// With a state (the store's <see cref="MarkupControlStateStore.Shared"/>) the
/// list is remembered for the character as "#AARRGGBB" text, comma-separated;
/// without one it lasts only as long as the list does. UI thread only.
/// <para>Each window's build makes its own list over the same shared state,
/// so the stored text is the truth: every read and every add re-reads it, and
/// decodes it again when it changed (another window's pick, or a character
/// switch, which makes the state answer for the new character).</para>
/// </summary>
public sealed class MarkupRecentColors
{
    public const int Capacity = 8;
    public const string Control = "color";
    public const string Kind = "recent";

    private readonly IMarkupControlState? _state;
    private bool _read;
    private string? _text;           // the stored text the list was decoded from
    private List<uint> _colors = new();
    private IReadOnlyList<uint>? _snapshot;

    public MarkupRecentColors(IMarkupControlState? state)
    {
        _state = state;
    }

    /// <summary>A read-only copy of the list, the same one until the list changes.</summary>
    public IReadOnlyList<uint> Colors
    {
        get
        {
            Refresh();
            return _snapshot ??= Array.AsReadOnly(_colors.ToArray());
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
        _snapshot = null;
        if (_state is null)
            return;
        _text = Encode(_colors);
        _state.Set(Control, Kind, _text);
    }

    private void Refresh()
    {
        if (_state is null)
            return;
        string? text = _state.Get(Control, Kind);
        if (_read && text == _text)
            return;
        _read = true;
        _text = text;
        _colors = Decode(text);
        _snapshot = null;
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
