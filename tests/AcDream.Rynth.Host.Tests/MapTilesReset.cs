using System.Reflection;
using RynthCore.Engine.ImGuiBackend;
using RynthCore.Engine.ImGuiBackend.Panels;

namespace AcDream.Rynth.Host.Tests;

/// <summary>
/// Brings RynthCore's map tile loader to rest between tests. A face that was drawn starts
/// WorldMapMaps.Refresh (a pool task that scans, marks itself scanned, then Apply opens or
/// switches a map file); Shutdown alone neither clears a pending switch nor stops an open
/// still running, so a late task could poison the next test. Polls only; bounded at about
/// 5 s per wait. The two private fields it touches are looked up loudly: a RynthCore bump
/// that renames one fails here instead of quietly weakening the isolation.
/// </summary>
internal static class MapTilesReset
{
    private const string Loading = "Loading the map...";

    private static readonly FieldInfo Scanned = Field(typeof(WorldMapMaps), "_scanned");
    private static readonly FieldInfo SwitchTo = Field(typeof(WorldMapTiles), "_switchTo");

    /// <param name="awaitScan">True when the test drew a map face, so a Refresh was started.</param>
    public static void Settle(bool awaitScan = false)
    {
        if (awaitScan)
        {
            Assert.True(SpinWait.SpinUntil(() => WorldMapMaps.Scanned, 5000), "the World Map scan never finished");
            // Refresh marks itself scanned before Apply runs: wait for Apply's own effect, the
            // file it picks either opened or queued as a switch, so no part of the task is left.
            string target = ApplyTarget();
            Assert.True(SpinWait.SpinUntil(() =>
                string.Equals(WorldMapTiles.CurrentPath, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SwitchTo.GetValue(null) as string, target, StringComparison.OrdinalIgnoreCase), 5000),
                "the World Map scan never opened or switched to " + target);
        }
        WaitForOpen();
        WorldMapTiles.BeginFrame(IntPtr.Zero);   // consumes a pending Switch (which restarts the open)
        WaitForOpen();
        WorldMapTiles.Shutdown();
        SwitchTo.SetValue(null, null);
        // So the next test's scan wait is about its own Refresh, not this one's.
        Scanned.SetValue(null, false);
    }

    // The path WorldMapMaps.Apply opens: the setting's tier if installed, else the best, else the Standard name.
    private static string ApplyTarget() =>
        WorldMapMaps.Resolve(WorldMapSettings.Detail) is WorldMapMaps.Tier t
            ? WorldMapMaps.PathOf(t)
            : WorldMapMaps.PathOf(WorldMapMaps.Tiers[^1]);

    private static void WaitForOpen() =>
        Assert.True(SpinWait.SpinUntil(() => WorldMapTiles.Status != Loading, 5000), "a World Map open never finished");

    private static FieldInfo Field(Type type, string name) =>
        type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"RynthCore's {type.Name}.{name} is gone; update {nameof(MapTilesReset)}.");
}
