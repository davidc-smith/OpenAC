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
    public static Func<bool?> BindRequiredNullableBoolReader(string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a bool or bool? binding");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is null
            || (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?)))
        {
            throw new FormatException(
                $"{expression} did not resolve to a bool or bool? property on "
                + binding.TypeName + $" ({context})");
        }
        return () => binding.Read(property) as bool?;
    }

    /// <summary>An optional <c>Action&lt;bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<bool>? BindBoolAction(string? attribute, MarkupBindingSource binding, string context) =>
        BindOptionalAction<bool>(attribute, binding, context, "bool");

    /// <summary>An optional <c>Action&lt;int&gt;</c>; null when the attribute is absent.</summary>
    public static Action<int>? BindIntAction(string? attribute, MarkupBindingSource binding, string context) =>
        BindOptionalAction<int>(attribute, binding, context, "int");

    /// <summary>An optional <c>Action&lt;float&gt;</c>; null when the attribute is absent.</summary>
    public static Action<float>? BindFloatAction(string? attribute, MarkupBindingSource binding, string context) =>
        BindOptionalAction<float>(attribute, binding, context, "float");

    /// <summary>An optional <c>Action&lt;int, bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<int, bool>? BindIntBoolAction(string? attribute, MarkupBindingSource binding, string context)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<int, bool> binding");
        PropertyInfo? property = binding.Property(attribute[1..^1]);
        if (property is null || !typeof(Action<int, bool>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<int, bool> property on "
                + binding.TypeName + $" ({context})");
        }
        return (first, second) => (binding.Read(property) as Action<int, bool>)?.Invoke(first, second);
    }

    /// <summary>A required list of floats (any <c>IEnumerable&lt;float&gt;</c>), copied on each read.</summary>
    public static Func<IReadOnlyList<float>> BindRequiredFloatListReader(
        string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a float-list binding");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is null || !typeof(IEnumerable<float>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{expression} did not resolve to an IEnumerable<float> property on "
                + binding.TypeName + $" ({context})");
        }
        return () => binding.Read(property) is IEnumerable<float> values
            ? values.ToArray()
            : Array.Empty<float>();
    }

    /// <summary>An optional <c>Action&lt;IReadOnlyList&lt;float&gt;&gt;</c>; null when the attribute is absent.</summary>
    public static Action<IReadOnlyList<float>>? BindFloatListAction(string? attribute, MarkupBindingSource binding, string context) =>
        BindOptionalAction<IReadOnlyList<float>>(attribute, binding, context, "IReadOnlyList<float>");

    /// <summary>
    /// A required list of ints (any <c>IEnumerable&lt;int&gt;</c>), read on each
    /// call: the plugin's list itself when it is an <c>IReadOnlyList&lt;int&gt;</c>
    /// (read every frame, so not copied), otherwise a copy. Null reads as empty.
    /// </summary>
    public static Func<IReadOnlyList<int>> BindRequiredIntListReader(
        string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be an int-list binding");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is null || !typeof(IEnumerable<int>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{expression} did not resolve to an IEnumerable<int> property on "
                + binding.TypeName + $" ({context})");
        }
        return () => binding.Read(property) switch
        {
            IReadOnlyList<int> list => list,
            IEnumerable<int> values => values.ToArray(),
            _ => Array.Empty<int>(),
        };
    }

    /// <summary>An optional <c>Action&lt;IReadOnlyList&lt;int&gt;&gt;</c>; null when the attribute is absent.</summary>
    public static Action<IReadOnlyList<int>>? BindIntListAction(string? attribute, MarkupBindingSource binding, string context) =>
        BindOptionalAction<IReadOnlyList<int>>(attribute, binding, context, "IReadOnlyList<int>");

    private static readonly IReadOnlySet<string> NoStrings = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// A required set of strings, read on each call: the plugin's set itself
    /// when it is an <c>IReadOnlySet&lt;string&gt;</c>, otherwise any
    /// <c>IEnumerable&lt;string&gt;</c> copied into an ordinal set. Null reads as empty.
    /// </summary>
    public static Func<IReadOnlySet<string>> BindRequiredStringSetReader(
        string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a string-set binding");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is null || !typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{expression} did not resolve to an IEnumerable<string> property on "
                + binding.TypeName + $" ({context})");
        }
        return () => binding.Read(property) switch
        {
            IReadOnlySet<string> set => set,
            IEnumerable<string> values => new HashSet<string>(values, StringComparer.Ordinal),
            _ => NoStrings,
        };
    }

    /// <summary>An optional <c>Action&lt;string, bool&gt;</c>; null when the attribute is absent.</summary>
    public static Action<string, bool>? BindStringBoolAction(string? attribute, MarkupBindingSource binding, string context)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<string, bool> binding");
        PropertyInfo? property = binding.Property(attribute[1..^1]);
        if (property is null || !typeof(Action<string, bool>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<string, bool> property on "
                + binding.TypeName + $" ({context})");
        }
        return (key, value) => (binding.Read(property) as Action<string, bool>)?.Invoke(key, value);
    }

    /// <summary>
    /// Sort keys for a list column: a list of doubles (compared as numbers) or
    /// of strings (compared as the column's sort kind says). Exactly one of the
    /// pair is set.
    /// </summary>
    public static (Func<IReadOnlyList<double>>? Numbers, Func<IReadOnlyList<string>>? Texts) BindSortKeys(
        string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a binding to a list of double or string");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is not null && typeof(IEnumerable<double>).IsAssignableFrom(property.PropertyType))
        {
            return (() => binding.Read(property) is IEnumerable<double> values
                ? values.ToArray()
                : Array.Empty<double>(), null);
        }
        if (property is not null && typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType))
        {
            return (null, () => binding.Read(property) is IEnumerable<string> values
                ? values.ToArray()
                : Array.Empty<string>());
        }
        throw new FormatException(
            $"{expression} did not resolve to an IReadOnlyList<double> or IReadOnlyList<string> property on "
            + binding.TypeName + $" ({context})");
    }

    /// <summary>A required <c>float</c> binding, read live.</summary>
    public static Func<float> BindRequiredFloatReader(string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a float binding");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is null || property.PropertyType != typeof(float))
        {
            throw new FormatException(
                $"{expression} did not resolve to a float property on "
                + binding.TypeName + $" ({context})");
        }
        return () => binding.Read(property) is float value ? value : 0f;
    }

    /// <summary>A required <c>string</c> property, read on each call; null reads as empty.</summary>
    public static Func<string> BindRequiredStringReader(string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a string binding");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is null || property.PropertyType != typeof(string))
        {
            throw new FormatException(
                $"{expression} did not resolve to a string property on "
                + binding.TypeName + $" ({context})");
        }
        return () => binding.Read(property) as string ?? string.Empty;
    }

    /// <summary>A required <c>Action&lt;string&gt;</c>.</summary>
    public static Action<string> BindRequiredStringAction(string? attribute, MarkupBindingSource binding, string context) =>
        BindOptionalAction<string>(attribute, binding, context, "string")
        ?? throw new FormatException($"{context} must be an Action<string> binding");

    /// <summary>
    /// A required colour pair: <c>value</c> a <c>uint</c>, <c>int</c> or
    /// <c>string</c> (#AARRGGBB) property and <c>onchange</c> an <c>Action</c> of
    /// the same type, read and written as packed 0xAARRGGBB. Text that is not
    /// exactly #AARRGGBB reads as opaque white, as a bound colour attribute does.
    /// <c>element</c> is the element name; the contexts are "&lt;element&gt; value"
    /// and "&lt;element&gt; onchange".
    /// </summary>
    public static (Func<uint> Read, Action<uint> Write) BindRequiredColor(
        string? value, string? onchange, MarkupBindingSource binding, string element)
    {
        string valueContext = element + " value";
        string changeContext = element + " onchange";
        if (string.IsNullOrWhiteSpace(value) || !MarkupDocument.IsBinding(value))
            throw new FormatException($"{valueContext} must be a uint, int or string binding");
        PropertyInfo? property = binding.Property(value[1..^1]);
        Type? type = property?.PropertyType;
        if (property is null || (type != typeof(uint) && type != typeof(int) && type != typeof(string)))
        {
            throw new FormatException(
                $"{value} did not resolve to a uint, int or string property on "
                + binding.TypeName + $" ({valueContext})");
        }
        if (type == typeof(uint))
        {
            Action<uint> set = BindOptionalAction<uint>(onchange, binding, changeContext, "uint")
                ?? throw new FormatException($"{changeContext} must be an Action<uint> binding");
            return (() => binding.Read(property) is uint packed ? packed : MarkupColorValue.White, set);
        }
        if (type == typeof(int))
        {
            Action<int> set = BindOptionalAction<int>(onchange, binding, changeContext, "int")
                ?? throw new FormatException($"{changeContext} must be an Action<int> binding");
            return (() => binding.Read(property) is int packed ? unchecked((uint)packed) : MarkupColorValue.White,
                packed => set(unchecked((int)packed)));
        }
        Action<string> setText = BindOptionalAction<string>(onchange, binding, changeContext, "string")
            ?? throw new FormatException($"{changeContext} must be an Action<string> binding");
        return (() => MarkupColorValue.ParseStrict(binding.Read(property) as string) ?? MarkupColorValue.White,
            packed => setText(MarkupColorValue.Format(packed)));
    }

    private static Action<T>? BindOptionalAction<T>(
        string? attribute, MarkupBindingSource binding, string context, string typeName)
    {
        if (attribute is null)
            return null;
        if (!MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an Action<{typeName}> binding");
        PropertyInfo? property = binding.Property(attribute[1..^1]);
        if (property is null || !typeof(Action<T>).IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an Action<{typeName}> property on "
                + binding.TypeName + $" ({context})");
        }
        return value => (binding.Read(property) as Action<T>)?.Invoke(value);
    }

    private static readonly Type[] NumberTypes =
        [typeof(int), typeof(long), typeof(float), typeof(double), typeof(decimal)];

    /// <summary>
    /// A required number binding of any of the five numeric types, read as
    /// <see cref="decimal"/>. Also returns the property's type, which the
    /// matching <c>onchange</c> must take.
    /// </summary>
    public static (Func<decimal> Read, Type ValueType) BindRequiredNumberReader(
        string? expression, MarkupBindingSource binding, string context)
    {
        if (string.IsNullOrWhiteSpace(expression) || !MarkupDocument.IsBinding(expression))
            throw new FormatException($"{context} must be a number binding");
        PropertyInfo? property = binding.Property(expression[1..^1]);
        if (property is null || Array.IndexOf(NumberTypes, property.PropertyType) < 0)
        {
            throw new FormatException(
                $"{expression} did not resolve to an int, long, float, double or decimal property on "
                + binding.TypeName + $" ({context})");
        }
        return (() => ToDecimal(binding.Read(property)), property.PropertyType);
    }

    /// <summary>
    /// A required <c>Action&lt;T&gt;</c> for exactly <paramref name="valueType"/>;
    /// the returned action converts a <see cref="decimal"/> already inside
    /// <c>T</c>'s range.
    /// </summary>
    public static Action<decimal> BindRequiredNumberAction(
        string? attribute, MarkupBindingSource binding, Type valueType, string context)
    {
        string expected = $"Action<{valueType.Name}>";
        if (string.IsNullOrWhiteSpace(attribute) || !MarkupDocument.IsBinding(attribute))
            throw new FormatException($"{context} must be an {expected} binding");
        Type actionType = typeof(Action<>).MakeGenericType(valueType);
        PropertyInfo? property = binding.Property(attribute[1..^1]);
        if (property is null || !actionType.IsAssignableFrom(property.PropertyType))
        {
            throw new FormatException(
                $"{attribute} did not resolve to an {expected} property on "
                + binding.TypeName + $" ({context})");
        }
        object? Target() => binding.Read(property);
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
