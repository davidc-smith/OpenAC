namespace AcDream.App.UI.Markup;

/// <summary>
/// What one markup window's controls remember for the logged-in character:
/// short string values keyed by the control's <c>name</c> and a kind such as
/// <c>"expanded"</c> or <c>"widths"</c>.
/// </summary>
public interface IMarkupControlState
{
    /// <summary>
    /// Changes whenever every remembered value may have changed, which is when
    /// another character logs in. A control that caches a value re-reads it
    /// when this moves.
    /// </summary>
    int Generation { get; }

    string? Get(string control, string kind);

    void Set(string control, string kind, string value);
}
