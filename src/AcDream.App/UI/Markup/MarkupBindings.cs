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

    private static readonly Type[] NumberTypes =
        [typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)];

    /// <summary>
    /// A required number binding of any of the five numeric types, read as
    /// <see cref="decimal"/>. Also returns the property's type, which the
    /// matching <c>onchange</c> must take.
    /// </summary>
    public static (Func<decimal> Read, Type ValueType) BindRequiredNumberReader(
        string? expression, object binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a number binding");
        PropertyInfo? property = binding.GetType().GetProperty(expression[1..^1]);
        if (property is null || Array.IndexOf(NumberTypes, property.PropertyType) < 0)
        {
            throw new FormatException(
                $"{expression} did not resolve to an int, long, float, double or decimal property on "
                + binding.GetType().Name + $" ({context})");
        }
        return (() => ToDecimal(property.GetValue(binding)), property.PropertyType);
    }

    /// <summary>
    /// A required <c>Action&lt;T&gt;</c> for exactly <paramref name="valueType"/>;
    /// the returned action converts a <see cref="decimal"/> already inside
    /// <c>T</c>'s range.
    /// </summary>
    public static Action<decimal> BindRequiredNumberAction(
        string? attribute, object binding, Type valueType, string context)
    {
        string expected = $"Action<{valueType.Name}>";
        if (string.IsNullOrWhiteSpace(attribute) || !MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an {expected} binding");
        Type actionType = typeof(Action<>).MakeGenericType(valueType);
        PropertyInfo? property = binding.GetType().GetProperty(attribute[1..^1]);
        if (property is null || !actionType.IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an {expected} property on "
                + binding.GetType().Name + $" ({context})");
        }
        object? Target() => property.GetValue(binding);
        return valueType == typeof(int) ? value => (Target() as Action<int>)?.Invoke((int)value)
            : valueType == typeof(long) ? value => (Target() as Action<long>)?.Invoke((long)value)
            : valueType == typeof(float) ? value => (Target() as Action<float>)?.Invoke((float)value)
            : valueType == typeof(double) ? value => (Target() as Action<double>)?.Invoke((double)value)
            : value => (Target() as Action<decimal>)?.Invoke(value);
    }

    /// <summary>
    /// The range a number of <paramref name="valueType"/> can hold, as decimal:
    /// <c>float</c> and <c>double</c> are limited to <see cref="decimal"/>'s.
    /// </summary>
    public static (decimal Min, decimal Max) NumberRange(Type valueType) =>
        valueType == typeof(int) ? (int.MinValue, int.MaxValue)
        : valueType == typeof(long) ? (long.MinValue, long.MaxValue)
        : (decimal.MinValue, decimal.MaxValue);

    public static bool IsIntegralNumber(Type valueType) =>
        valueType == typeof(int) || valueType == typeof(long);

    private static decimal ToDecimal(object? value) => value switch
    {
        int i => i,
        long l => l,
        decimal d => d,
        float f => float.IsNaN(f) ? 0m
            : f >= (float)decimal.MaxValue ? decimal.MaxValue
            : f <= (float)decimal.MinValue ? decimal.MinValue
            : (decimal)f,   // rounds to 7 significant digits, so 0.1f reads as 0.1
        double d => FromBinary(d),
        _ => 0m,
    };

    /// <summary>NaN reads as 0; anything beyond decimal's range reads as its limit.</summary>
    private static decimal FromBinary(double value) =>
        double.IsNaN(value) ? 0m
        : value >= (double)decimal.MaxValue ? decimal.MaxValue
        : value <= (double)decimal.MinValue ? decimal.MinValue
        : (decimal)value;
}
