namespace AcDream.Rynth.Host;

/// <summary>
/// RynthNav's navmesh tiles are not shipped with OpenAC: tools/RynthNavData installs the
/// pinned set. Without them RynthAi's outdoor detours answer "no RynthNav navmesh here" and
/// it keeps its jump ladder; this says once where the tiles go and how to get them.
/// </summary>
internal static class RynthNavDataHint
{
    internal const string InstallCommand = "dotnet run --project tools/RynthNavData -c Release -- install";

    /// <summary>The line to show, or null when <paramref name="navDataDirectory"/> is unknown or has tiles.</summary>
    internal static string? For(string? navDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(navDataDirectory))
            return null;
        try
        {
            if (Directory.Exists(navDataDirectory) && Directory.EnumerateFiles(navDataDirectory, "nav_*.tile").Any())
                return null;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return $"[Rynth] RynthAi's outdoor detours need RynthNav's navmesh tiles in {navDataDirectory}. "
            + $"Install them (1.6 GB download, about 4.7 GB on disk, needs about 6.4 GB free) from an OpenAC checkout with: {InstallCommand}";
    }
}
