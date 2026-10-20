using System.Reflection;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// Brings RynthCore's map tile loader to rest between tests. A face that was drawn starts
/// WorldMapMaps.Refresh (a pool task that scans and then opens or switches a map file);
/// Shutdown alone neither clears a pending switch nor stops an open still running, so a
/// late task could poison the next test. Polls only; bounded at about 5 s per wait.
/// </summary>
internal static class MapTilesReset
{
    private const string Loading = "Loading the map...";

    /// <param name="awaitScan">True when the test drew a map face, so a Refresh was started.</param>
    public static void Settle(bool awaitScan = false)
    {
        if (awaitScan) SpinWait.SpinUntil(() => WorldMapMaps.Scanned, 5000);
        WaitForOpen();
        WorldMapTiles.BeginFrame(IntPtr.Zero);   // consumes a pending Switch (which restarts the open)
        WaitForOpen();
        WorldMapTiles.Shutdown();
        // So the next test's scan wait is about its own Refresh, not this one's.
        typeof(WorldMapMaps).GetField("_scanned", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, false);
    }

    private static void WaitForOpen() =>
        SpinWait.SpinUntil(() => WorldMapTiles.Status != Loading, 5000);
}
