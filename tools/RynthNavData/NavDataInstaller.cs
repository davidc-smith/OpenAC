using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RynthCore.App.Avalonia;

namespace AcDream.Tools.RynthNavData;

/// <summary>
/// Installs exactly the pinned archive with RynthCore's own UpdateNavDataAsync: disk space,
/// download checked against our size and SHA-256 (a verified cached zip is reused), unpack
/// beside the folder with the file count check, the player's own files copied across,
/// navdata.id written, folders swapped with one NavData.previous kept. The state check is
/// theirs too (RynthUpdater.CheckNavData, which needs their feed): navdata.local = left
/// alone, a matching navdata.id = nothing to do.
/// </summary>
internal static class NavDataInstaller
{
    internal static async Task<string> InstallAsync(NavDataPin pin, string navDataDir, string cacheDir,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        RynthUpdater.NavDataEntry entry = pin.ToEntry();
        if (File.Exists(Path.Combine(navDataDir, RynthUpdater.NavDataLocalMarker)))
            return $"{navDataDir} has {RynthUpdater.NavDataLocalMarker}: its tiles are left alone.";

        string idFile = Path.Combine(navDataDir, RynthUpdater.NavDataIdFile);
        string installed = File.Exists(idFile) ? File.ReadAllText(idFile).Trim().ToLowerInvariant() : string.Empty;
        if (installed == entry.Id)
            return $"{navDataDir} already has the pinned tiles ({entry.Files} files).";

        var updater = new RynthUpdater(trustedKeys: Array.Empty<string>(), stateDir: cacheDir) { NavDataCacheDir = cacheDir };
        var manifest = new RynthUpdater.Manifest(0, string.Empty, DateTime.UnixEpoch, string.Empty,
            new RynthUpdater.InstallerEntry(0, string.Empty, string.Empty, 0, string.Empty),
            Array.Empty<RynthUpdater.PluginEntry>(), entry);
        var check = new RynthUpdater.CheckResult(manifest, 0, Array.Empty<RynthUpdater.PluginStatus>())
        {
            NavData = new RynthUpdater.NavDataStatus(entry, navDataDir, installed, RynthUpdater.NavDataState.NeedsUpdate),
        };
        return await updater.UpdateNavDataAsync(check, progress, ct);
    }
}
