using System.Reflection;

namespace AcDream.Platform;

/// <summary>
/// The product this build ships as (Directory.Build.props <c>OpenACProduct</c>) and the names
/// derived from it. OpenAC Gateway and OpenAC Gateway Rynth differ only here, so the two can be
/// installed side by side without sharing a data folder or an app bundle.
/// </summary>
public static class OpenACProductIdentity
{
    private static readonly Assembly Self = typeof(OpenACProductIdentity).Assembly;

    public static string Name { get; } =
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? throw new InvalidOperationException("AcDream.Platform was built without a product name.");

    /// <summary>The product in lower case, words joined by hyphens (<c>openac-gateway</c>).</summary>
    public static string Slug { get; } =
        Self.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "OpenAC.ProductSlug")?.Value
        ?? throw new InvalidOperationException("AcDream.Platform was built without a product slug.");

    /// <summary>The per-user data folder on Windows and macOS.</summary>
    public static string DataFolderName => Name;

    /// <summary>The per-user data folder under the XDG data home on Linux.</summary>
    public static string LinuxDataFolderName => Slug;

    /// <summary>The macOS launcher bundle's folder name.</summary>
    public static string MacAppBundleName { get; } = Name + ".app";
}
