using System.Collections;
using System.Reflection;
using RynthCore.Engine.Compatibility;

namespace AcDream.Rynth.Host.Tests;

/// <summary>Their InventoryModel keeps static state with no reset; tests clear it between cases.</summary>
internal static class InventoryModelReset
{
    public static void Run()
    {
        Type t = typeof(InventoryModel);
        foreach (FieldInfo f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (f.IsLiteral)
                continue;
            if (f.IsInitOnly)
            {
                // Collections are emptied; arrays (scratch buffers), tick constants and the lock are kept.
                object? value = f.GetValue(null);
                if (value is IDictionary d) d.Clear();
                else if (value is IList l && !f.FieldType.IsArray) l.Clear();
                else if (value is not null && !f.FieldType.IsArray
                         && value.GetType().GetMethod("Clear", Type.EmptyTypes) is { } clear)
                    clear.Invoke(value, null);
                continue;
            }

            if (f.FieldType == typeof(InventoryView)) f.SetValue(null, InventoryView.Empty);
            else if (f.Name == "_dirty") f.SetValue(null, 1);
            else if (f.FieldType == typeof(string)) f.SetValue(null, string.Empty);
            else f.SetValue(null, f.FieldType.IsValueType ? Activator.CreateInstance(f.FieldType) : null);
        }
    }
}
