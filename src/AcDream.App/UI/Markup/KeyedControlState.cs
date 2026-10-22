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

    public int Generation => _root.Generation;

    public string? Get(string control, string kind) => _root.Get(Name(control), kind);

    public void Set(string control, string kind, string value) => _root.Set(Name(control), kind, value);

    public void Remove(string control, string kind) => _root.Remove(Name(control), kind);

    private string Name(string control) => control + "#" + _path;
}
