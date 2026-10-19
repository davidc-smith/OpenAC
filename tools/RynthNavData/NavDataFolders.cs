using System;
using System.IO;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// The same folders the client uses (AcDream.Rynth.Host.RynthInstallFolders): off Windows
/// RYNTHCORE_DIR defaults under ApplicationData and the tiles go in its NavData folder unless
/// RYNTHNAV_NAVDATA names another. On Windows RynthCore's own lookup decides.
/// </summary>
internal static class NavDataFolders
{
    internal static string NavData()
    {
        if (Environment.GetEnvironmentVariable("RYNTHNAV_NAVDATA") is { Length: > 0 } explicitDir)
            return explicitDir;
        if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RYNTHCORE_DIR")))
            Environment.SetEnvironmentVariable("RYNTHCORE_DIR",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore"));
        return Path.Combine(RynthCore.Install.RynthInstallPaths.CoreDir, "NavData");
    }

    /// <summary>Where downloaded archives wait: ~/Library/Caches on macOS, RynthCore's own cache elsewhere.</summary>
    internal static string Cache() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "RynthCore", "updates")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RynthCore", "updates");
}
