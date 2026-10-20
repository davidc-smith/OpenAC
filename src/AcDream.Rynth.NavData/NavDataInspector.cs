using System;
using System.IO;
using System.Linq;
using RynthCore.App.Avalonia;

namespace AcDream.Rynth.NavData;

public enum NavDataInstallState { Missing, Current, Outdated, LocalMarker, RynthCoreManaged }

public sealed record NavDataInspection(NavDataInstallState State, string Directory, string InstalledId);

/// <summary>
/// What a NavData folder holds against a pin, without touching it. The same reading as
/// <see cref="NavDataInstaller"/>: navdata.local = the player's own tiles; our ids are the
/// archive SHA-256 (64 hex); any other id is RynthCore's launcher's (its feed ids).
/// </summary>
public static class NavDataInspector
{
    /// <summary>Our ids are the archive SHA-256: exactly 64 hex characters. Anything else is RynthCore's.</summary>
    internal static bool IsOurId(string id) =>
        id.Length == 64 && id.All(Uri.IsHexDigit);

    public static NavDataInspection Inspect(NavDataPin pin, string navDataDir)
    {
        ArgumentNullException.ThrowIfNull(pin);
        if (File.Exists(Path.Combine(navDataDir, RynthUpdater.NavDataLocalMarker)))
            return new(NavDataInstallState.LocalMarker, navDataDir, string.Empty);

        string idFile = Path.Combine(navDataDir, RynthUpdater.NavDataIdFile);
        string installed = File.Exists(idFile) ? File.ReadAllText(idFile).Trim().ToLowerInvariant() : string.Empty;
        NavDataInstallState state = installed.Length == 0 ? NavDataInstallState.Missing
            : installed == pin.Id.ToLowerInvariant() ? NavDataInstallState.Current
            : IsOurId(installed) ? NavDataInstallState.Outdated
            : NavDataInstallState.RynthCoreManaged;
        return new(state, navDataDir, installed);
    }
}
