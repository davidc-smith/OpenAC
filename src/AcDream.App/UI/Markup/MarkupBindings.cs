using System.Reflection;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Binding readers and actions the newer markup elements share.
/// MarkupBindings helpers validate: BindRequired* throw when the attribute is
/// absent or mistyped; other Bind* return null when the attribute is absent and
/// throw when it is present but mistyped. <c>context</c> is "&lt;element&gt; &lt;attribute&gt;".
/// </summary>
internal static class MarkupBindings
{
    /// <summary>
    /// A required tri-state binding: a <c>bool</c> property, or a <c>bool?</c>
    /// one whose null means indeterminate.
    /// </summary>
    public static Func<bool?> BindRequiredNullableBoolReader(string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a bool or bool? binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null
            || (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?)))
        {
            throw new FormatException(
                $"{expression} did not resolve to a bool or bool? property on "
                + binding.GetType().Name + $" ({context})");
        }
        return () => property.GetValue(binding) as bool?;
    }

    /// <summary>An optional <c>Action&lt;bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<bool>? BindBoolAction(string? attribute, object binding, string context)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<bool> binding");
        PropertyInfo? property = binding.GetType().GetProperty(attribute[1..^1]);
        if (property is null || !typeof(Action<bool>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<bool> property on "
                + binding.GetType().Name + $" ({context})");
        }
        return value => (property.GetValue(binding) as Action<bool>)?.Invoke(value);
    }
}
