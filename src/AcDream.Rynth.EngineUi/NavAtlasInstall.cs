using System;
using System.IO;

namespace AcDream.Rynth.EngineUi;

/// <summary>
/// RynthCore's installer puts RynthNav's places database in the RynthCore folder
/// (NavData/locations.json); their RynthNavAtlas reads dungeon names from there when no
/// RynthNav plugin reports another path. OpenAC has no installer, so the dashboard writes
/// the copy embedded here. On Windows an existing file is left alone (a real RynthCore
/// install keeps its own); elsewhere the folder is ours and a different file is replaced.
/// </summary>
internal static class NavAtlasInstall
{
    internal const string ResourceName = "AcDream.Rynth.EngineUi.locations.json";

    internal static string PathUnder(string coreDir) => Path.Combine(coreDir, "NavData", "locations.json");

    /// <summary>True when the file is in place afterwards. IO errors are logged, never thrown.</summary>
    internal static bool Ensure(string coreDir, bool isWindows, Action<string> log)
    {
        string target = PathUnder(coreDir);
        try
        {
            using Stream? source = typeof(NavAtlasInstall).Assembly.GetManifestResourceStream(ResourceName);
            if (source is null)
            {
                log("[RynthAi] the embedded Atlas is missing; dungeons show as LB numbers.");
                return File.Exists(target);
            }

            // A navdata.id means the pinned archive manages this folder and ships its own locations.json.
            if (File.Exists(Path.Combine(coreDir, "NavData", "navdata.id")))
                return File.Exists(target);

            var existing = new FileInfo(target);
            if (existing.Exists && (isWindows || existing.Length == source.Length))
                return true;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string temp = target + ".tmp";
            using (FileStream output = File.Create(temp))
                source.CopyTo(output);
            File.Move(temp, target, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"[RynthAi] the Atlas could not be installed to {target}: {ex.Message}; dungeons show as LB numbers.");
            return false;
        }
    }
}
