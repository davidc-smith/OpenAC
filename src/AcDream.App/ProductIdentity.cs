using System.Reflection;

namespace AcDream.App;

/// <summary>The product name this build ships as (Directory.Build.props OpenACProduct).</summary>
internal static class ProductIdentity
{
    internal static string Name { get; } =
        typeof(ProductIdentity).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "OpenAC";
}
