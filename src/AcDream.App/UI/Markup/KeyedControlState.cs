namespace AcDream.App.UI.Markup;

/// <summary>
/// The remembered state of one repeat copy: every control name is stored as
/// <c>name#key</c>, with the keys of nested repeats joined outermost first
/// (<c>name#outer/inner</c>), so copies don't share one remembered value.
/// A key's own <c>%</c> and <c>/</c> are written <c>%25</c> and <c>%2F</c>,
/// so a key with a slash can't be read as a nested path.
/// </summary>
internal sealed class KeyedControlState : IMarkupControlState
{
    private readonly IMarkupControlState _root;
    private readonly string _path;

    public KeyedControlState(IMarkupControlState inner, string key)
    {
        (_root, _path) = inner is KeyedControlState keyed
            ? (keyed._root, keyed._path + "/" + Escape(key))
            : (inner, Escape(key));
    }

    private static string Escape(string key) =>
        key.AsSpan().IndexOfAny('%', '/') < 0 ? key : key.Replace("%", "%25").Replace("/", "%2F");

    /// <summary>The window's state the names are stored in.</summary>
    internal IMarkupControlState Root => _root;

    /// <summary>The keys after the <c>#</c>, outermost first.</summary>
    internal string Path => _path;

    public int Generation => _root.Generation;

    public string? Get(string control, string kind) => _root.Get(Name(control), kind);

    public void Set(string control, string kind, string value) => _root.Set(Name(control), kind, value);

    public void Remove(string control, string kind) => _root.Remove(Name(control), kind);

    private string Name(string control) => control + "#" + _path;

    /// <summary>
    /// Whether <paramref name="control"/> is <c>name#path</c> for one of
    /// <paramref name="names"/> and one of <paramref name="paths"/>, or
    /// <c>name#path/...</c> for a repeat nested in it. Compares in place.
    /// </summary>
    internal static bool IsUnder(
        string control,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> names,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> paths)
    {
        ReadOnlySpan<char> text = control;
        int hash = text.IndexOf('#');
        if (hash < 0 || !names.Contains(text[..hash]))
            return false;
        ReadOnlySpan<char> path = text[(hash + 1)..];
        if (paths.Contains(path))
            return true;
        for (int slash = path.IndexOf('/'); slash >= 0;)
        {
            if (paths.Contains(path[..slash]))
                return true;
            int next = path[(slash + 1)..].IndexOf('/');
            slash = next < 0 ? -1 : slash + 1 + next;
        }
        return false;
    }
}
