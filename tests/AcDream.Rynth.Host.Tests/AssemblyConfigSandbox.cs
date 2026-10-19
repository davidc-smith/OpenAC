using System.Runtime.CompilerServices;
using AcDream.TestSupport;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// Every RynthHost test loads the Oracle and Tracker plugins, which write under the user's
/// ApplicationData folder too. Keep the whole test process out of the real folder; classes that
/// need their own folder (RynthInventoryTests) nest a sandbox on top of this one.
/// </summary>
internal static class AssemblyConfigSandbox
{
    [ModuleInitializer]
    internal static void Install()
    {
        _ = TestDats.Directory;   // capture the real profile before the sandbox moves HOME
        var sandbox = new RynthConfigSandbox("rynth-host-tests-");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => sandbox.Dispose();
    }
}

/// <summary>Where the installed AC dats are: ACDREAM_DAT_DIR, else the REAL profile's AsheronsCall folder.</summary>
internal static class TestDats
{
    // Captured before RynthConfigSandbox repoints HOME at a temp folder.
    private static readonly string RealProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string? Override = Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");

    internal static string Directory =>
        string.IsNullOrWhiteSpace(Override) ? Path.Combine(RealProfile, "AsheronsCall") : Override;
}
