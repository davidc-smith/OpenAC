using System.Reflection;

namespace AcDream.Launcher;

/// <summary>The product name this launcher ships as (Directory.Build.props OpenACProduct).</summary>
internal static class LauncherProduct
{
    internal static string Name { get; } =
        typeof(LauncherProduct).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "OpenAC";
}
