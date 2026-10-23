namespace AcDream.App.UI.Markup;

/// <summary>
/// The remembered state of one repeat copy: every control name is stored as
/// <c>name#key</c>, with the keys of nested repeats joined outermost first
/// (<c>name#outer/inner</c>), so copies don't share one remembered value.
/// </summary>
internal sealed class KeyedControlState : IMarkupControlState
{
    private readonly IMarkupControlState _root;
    private readonly string _path;

    public KeyedControlState(IMarkupControlState inner, string key)
    {
        (_root, _path) = inner is KeyedControlState keyed
            ? (keyed._root, keyed._path + "/" + key)
            : (inner, key);
    }

    /// <summary>The window's state the names are stored in.</summary>
    internal IMarkupControlState Root => _root;

    /// <summary>The keys after the <c>#</c>, outermost first.</summary>
    internal string Path => _path;

    public int Generation => _root.Generation;

    public string? Get(string control, string kind) => _root.Get(Name(control), kind);

    public void Set(string control, string kind, string value) => _root.Set(Name(control), kind, value);

    public void Remove(string control, string kind) => _root.Remove(Name(control), kind);

    private string Name(string control) => control + "#" + _path;

    /// <summary>Whether <paramref name="control"/> was stored by a copy at one of <paramref name="paths"/> or one nested in it.</summary>
    internal static bool IsUnder(string control, IReadOnlySet<string> paths)
    {
        int hash = control.IndexOf('#');
        if (hash < 0)
            return false;
        string path = control[(hash + 1)..];
        if (paths.Contains(path))
            return true;
        for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            if (paths.Contains(path[..slash]))
                return true;
        }
        return false;
    }
}
