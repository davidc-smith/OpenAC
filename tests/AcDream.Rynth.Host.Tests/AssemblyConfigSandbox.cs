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
        TestNavData.CaptureRealDirectory();   // likewise the real RynthCore folder
        var sandbox = new RynthConfigSandbox("rynth-host-tests-");
        TestNavData.Prepare(sandbox.Root);
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

/// <summary>
/// RynthNav resolves its tile folder once per process (a static NavDataConfig), so every test
/// shares one: a sandbox folder, with copies of the real installed tiles around Holtburg when
/// they are there (Task 4's real-tile tests skip otherwise). Never the real folder: RynthNav
/// writes _player.txt into it.
/// </summary>
internal static class TestNavData
{
    // The installed tiles: ACDREAM_NAVDATA_DIR, else the REAL <ApplicationData>/RynthCore/NavData.
    private static string _realDirectory = string.Empty;

    /// <summary>Call before RynthConfigSandbox repoints ApplicationData at a temp folder.</summary>
    internal static void CaptureRealDirectory() =>
        _realDirectory = Environment.GetEnvironmentVariable("ACDREAM_NAVDATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "NavData");

    /// <summary>Holtburg's landblock; the copied tiles are the 5x5 block around it.</summary>
    internal const uint HoltburgLandblock = 0xA9B4;

    internal static string Directory { get; private set; } = string.Empty;

    /// <summary>True when the Holtburg tile was copied in.</summary>
    internal static bool HasTiles { get; private set; }

    internal static void Prepare(string sandboxRoot)
    {
        Directory = Path.Combine(sandboxRoot, "NavData");
        System.IO.Directory.CreateDirectory(Directory);
        int cx = (int)(HoltburgLandblock >> 8), cy = (int)(HoltburgLandblock & 0xFF);
        for (int x = cx - 2; x <= cx + 2; x++)
            for (int y = cy - 2; y <= cy + 2; y++)
            {
                string name = $"nav_{(x << 8) | y:X4}.tile";
                string source = Path.Combine(_realDirectory, name);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(Directory, name), overwrite: true);
            }

        HasTiles = File.Exists(Path.Combine(Directory, $"nav_{HoltburgLandblock:X4}.tile"));
        Environment.SetEnvironmentVariable("RYNTHNAV_NAVDATA", Directory);
    }
}
