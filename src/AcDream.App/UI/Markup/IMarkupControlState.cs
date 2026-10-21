using System;

namespace AcDream.App.UI.Markup;

/// <summary>
/// What one markup window's controls remember for the logged-in character:
/// short string values keyed by the control's <c>name</c> and a kind such as
/// <c>"expanded"</c> or <c>"widths"</c>. Use it only on the UI thread (the one
/// that builds, draws and dispatches input to markup windows); never from
/// plugin code or binding callbacks.
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

    /// <summary>Remembers <paramref name="value"/>, which must not be null.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    void Set(string control, string kind, string value);
}
