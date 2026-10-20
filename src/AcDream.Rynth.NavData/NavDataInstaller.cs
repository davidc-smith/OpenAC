using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RynthCore.App.Avalonia;

namespace AcDream.Rynth.NavData;

/// <summary>
/// Installs exactly the pinned archive with RynthCore's own UpdateNavDataAsync: disk space,
/// download checked against our size and SHA-256 (a verified cached zip is reused), unpack
/// beside the folder with the file count check, the player's own files copied across,
/// navdata.id written, folders swapped with one NavData.previous kept. The state check is
/// theirs too (RynthUpdater.CheckNavData, which needs their feed): navdata.local = left
/// alone, a matching navdata.id = nothing to do. A launcher-managed folder (a feed id) is
/// left alone unless forced.
/// </summary>
public static class NavDataInstaller
{
    public static async Task<string> InstallAsync(NavDataPin pin, string navDataDir, string cacheDir,
        IProgress<NavDataProgress>? progress = null, CancellationToken ct = default, bool force = false)
    {
        RynthUpdater.NavDataEntry entry = pin.ToEntry();
        if (File.Exists(Path.Combine(navDataDir, RynthUpdater.NavDataLocalMarker)))
            return $"{navDataDir} has {RynthUpdater.NavDataLocalMarker}: its tiles are left alone.";

        string idFile = Path.Combine(navDataDir, RynthUpdater.NavDataIdFile);
        string installed = File.Exists(idFile) ? File.ReadAllText(idFile).Trim().ToLowerInvariant() : string.Empty;
        if (installed == entry.Id)
            return $"{navDataDir} already has the pinned tiles ({entry.Files} files).";

        // RynthCore's own launcher keeps its feed's ids (not our 64-hex SHA-256) there:
        // replacing its tiles would make the launcher and this tool swap the folder back and forth.
        if (!force && installed.Length > 0 && !NavDataInspector.IsOurId(installed))
            return $"{navDataDir} holds RynthCore launcher tiles ({RynthUpdater.NavDataIdFile} {installed}); its launcher manages that folder. Pass --dir <other folder> or --force to replace them.";

        var updater = new RynthUpdater(trustedKeys: Array.Empty<string>(), stateDir: cacheDir) { NavDataCacheDir = cacheDir };
        var manifest = new RynthUpdater.Manifest(0, string.Empty, DateTime.UnixEpoch, string.Empty,
            new RynthUpdater.InstallerEntry(0, string.Empty, string.Empty, 0, string.Empty),
            Array.Empty<RynthUpdater.PluginEntry>(), entry);
        var check = new RynthUpdater.CheckResult(manifest, 0, Array.Empty<RynthUpdater.PluginStatus>())
        {
            NavData = new RynthUpdater.NavDataStatus(entry, navDataDir, installed, RynthUpdater.NavDataState.NeedsUpdate),
        };
        IProgress<DownloadProgress>? theirs = progress is null ? null : new Adapter(progress);
        return await updater.UpdateNavDataAsync(check, theirs, ct);
    }

    internal sealed class Adapter(IProgress<NavDataProgress> ours) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) =>
            ours.Report(new NavDataProgress((NavDataStage)(int)value.Stage, value.Percent, value.Detail));
    }
}
