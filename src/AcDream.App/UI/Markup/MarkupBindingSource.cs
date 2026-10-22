using System.Reflection;

namespace AcDream.App.UI.Markup;

/// <summary>
/// Where markup bindings resolve: names are looked up on <see cref="Type"/>
/// and values read from <see cref="Current"/>. A window's source is its
/// binding object; a repeat copy's source is the list's element type with the
/// copy's item, which the repeat swaps when a kept key gets a new object.
/// While <see cref="Current"/> is null (a repeat's build-time check) reads
/// give null and bound actions do nothing.
/// </summary>
internal sealed class MarkupBindingSource(Type type)
{
    public Type Type { get; } = type;

    public object? Current { get; set; }

    public string TypeName => Type.Name;

    public static MarkupBindingSource For(object binding) =>
        new(binding.GetType()) { Current = binding };

    public PropertyInfo? Property(string name) => Type.GetProperty(name);

    public object? Read(PropertyInfo property) =>
        Current is { } target ? property.GetValue(target) : null;
}
