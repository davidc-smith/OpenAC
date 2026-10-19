using System;

namespace AcDream.Rynth.Plugin.RynthAi.StandIns;

/// <summary>
/// Stands in for <c>Microsoft.Win32.Registry</c> in RynthAi (tools/RynthInteropRedirect
/// --redirect-type, on every OS). RynthAi reads one value, the Asheron's Call
/// InstallPath, to find the dats; the host sets <see cref="InstallPath"/> to the
/// client's dat directory before RynthAi starts. Every other key is missing.
/// </summary>
public static class RegistryStandIn
{
    internal const string AcKey = @"SOFTWARE\WOW6432Node\Turbine\Asheron's Call";

    /// <summary>The root RynthAi opens its key from (their <c>Registry.LocalMachine</c>).</summary>
    public static readonly RegistryKeyStandIn LocalMachine = new(isRoot: true);

    /// <summary>The dat directory; null answers no key, as a machine without AC installed.</summary>
    public static string? InstallPath { get; set; }
}

/// <summary>Stands in for <c>Microsoft.Win32.RegistryKey</c>: only the members RynthAi calls.</summary>
public sealed class RegistryKeyStandIn : IDisposable
{
    private readonly bool _isRoot;

    internal RegistryKeyStandIn(bool isRoot) => _isRoot = isRoot;

    public RegistryKeyStandIn? OpenSubKey(string name) =>
        _isRoot && RegistryStandIn.InstallPath is not null
            && string.Equals(name, RegistryStandIn.AcKey, StringComparison.OrdinalIgnoreCase)
            ? new RegistryKeyStandIn(isRoot: false)
            : null;

    public object? GetValue(string name) =>
        !_isRoot && string.Equals(name, "InstallPath", StringComparison.OrdinalIgnoreCase)
            ? RegistryStandIn.InstallPath
            : null;

    public void Dispose()
    {
    }
}
