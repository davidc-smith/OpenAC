namespace AcDream.Rynth.Host;

/// <summary>
/// RynthSuite plugins find their folders through RynthInstallPaths: the
/// RYNTHCORE_DIR / RYNTHSUITE_DIR environment variables, then the Windows
/// registry, then C:\Games\RynthCore and C:\Games\RynthSuite. On Windows that
/// lookup is theirs and is left alone (a RynthSuite install shares its
/// profiles). Elsewhere the C:\ defaults mean nothing, so unset variables
/// point under ApplicationData, beside the RynthCore folder our other plugins
/// already use. Call before the first plugin loads: they resolve once.
/// </summary>
internal static class RynthInstallFolders
{
    internal const string CoreVariable = "RYNTHCORE_DIR";
    internal const string SuiteVariable = "RYNTHSUITE_DIR";

    internal static void Apply() => Apply(
        Environment.GetEnvironmentVariable,
        Environment.SetEnvironmentVariable,
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    internal static void Apply(Func<string, string?> get, Action<string, string?> set, bool isWindows, string applicationData)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        if (isWindows || string.IsNullOrEmpty(applicationData))
            return;
        if (string.IsNullOrWhiteSpace(get(CoreVariable)))
            set(CoreVariable, Path.Combine(applicationData, "RynthCore"));
        if (string.IsNullOrWhiteSpace(get(SuiteVariable)))
            set(SuiteVariable, Path.Combine(applicationData, "RynthSuite"));
    }
}
